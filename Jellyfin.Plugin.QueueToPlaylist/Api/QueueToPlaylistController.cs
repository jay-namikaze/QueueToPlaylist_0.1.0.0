using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.QueueToPlaylist.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.QueueToPlaylist.Api;

/// <summary>
/// Server API used by the companion web client. It deliberately does not replace Jellyfin's
/// playback queue; it returns a queue plan that the client can hand to its normal player.
/// </summary>
[Authorize]
[ApiController]
[Route("QueueToPlaylist")]
public sealed class QueueToPlaylistController : ControllerBase
{
    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;

    /// <summary>Initializes a new instance of the <see cref="QueueToPlaylistController"/> class.</summary>
    public QueueToPlaylistController(
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IDtoService dtoService)
    {
        _playlistManager = playlistManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _dtoService = dtoService;
    }

    /// <summary>Lists video playlists visible to the signed-in user.</summary>
    [HttpGet("playlists")]
    public ActionResult<IReadOnlyList<PlaylistSummary>> GetPlaylists()
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var result = _playlistManager.GetPlaylists(user.Id)
            .Where(p => p.PlaylistMediaType == MediaType.Video && p.IsVisible(user))
            .Select(p => new PlaylistSummary(p.Id, p.Name, p.GetManageableItems().Count))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Ok(result);
    }

    /// <summary>
    /// Returns a playlist queue. <c>shuffle</c> randomizes all entries; <c>randomizer</c> picks a
    /// different first item and then supplies the remaining entries in a fresh order.
    /// </summary>
    [HttpGet("playlists/{playlistId:guid}/queue")]
    public ActionResult<QueuePlan> GetQueue(
        [FromRoute, Required] Guid playlistId,
        [FromQuery] string mode = "ordered",
        [FromQuery] Guid? excludeId = null)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var playlist = _playlistManager.GetPlaylistForUser(playlistId, user.Id);
        if (playlist is null || !playlist.IsVisible(user))
        {
            return NotFound();
        }

        var items = playlist.GetManageableItems()
            .Where(pair => pair.Item2.IsVisible(user) && pair.Item2.MediaType == MediaType.Video)
            .Select(pair => ToQueueItem(pair.Item2, user))
            .ToList();

        var normalizedMode = (mode ?? "ordered").Trim().ToLowerInvariant();
        if (normalizedMode is not ("ordered" or "shuffle" or "randomizer"))
        {
            return BadRequest("mode must be ordered, shuffle, or randomizer.");
        }
        if (normalizedMode is "shuffle" or "randomizer")
        {
            Shuffle(items);
        }

        if (normalizedMode == "randomizer" && excludeId.HasValue && items.Count > 1)
        {
            var differentIndex = items.FindIndex(item => item.Id != excludeId.Value);
            if (differentIndex > 0)
            {
                (items[0], items[differentIndex]) = (items[differentIndex], items[0]);
            }
        }

        Guid? selectedId = null;
        if (normalizedMode == "randomizer" && items.Count > 1)
        {
            // The first result is guaranteed to be different from the second result, which avoids
            // the common "randomizer picked the same thing again" feeling in a playlist.
            selectedId = items[0].Id;
        }

        return Ok(new QueuePlan(playlist.Id, playlist.Name, normalizedMode, selectedId, items));
    }

    /// <summary>Persists a Fisher-Yates shuffle back into a playlist.</summary>
    [HttpPost("playlists/{playlistId:guid}/shuffle")]
    public async Task<ActionResult<QueuePlan>> ShufflePlaylist([FromRoute, Required] Guid playlistId)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var playlist = _playlistManager.GetPlaylistForUser(playlistId, user.Id);
        if (playlist is null || !playlist.IsVisible(user))
        {
            return NotFound();
        }

        var entries = playlist.GetManageableItems()
            .Where(pair => pair.Item2.IsVisible(user) && pair.Item2.MediaType == MediaType.Video)
            .Select(pair => pair.Item1.ItemId?.ToString("N"))
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToList();
        Shuffle(entries);

        for (var index = 0; index < entries.Count; index++)
        {
            await _playlistManager.MoveItemAsync(playlistId.ToString("N"), entries[index], index, user.Id)
                .ConfigureAwait(false);
        }

        return GetQueue(playlistId, "ordered");
    }

    /// <summary>
    /// Picks a fresh item from a library. The UI can animate this result as a dice roll or a wheel
    /// without having to expose the whole library to the browser.
    /// </summary>
    [HttpGet("picker")]
    public ActionResult<PickerResult> Pick(
        [FromQuery] Guid? libraryId,
        [FromQuery] string mode = "dice",
        [FromQuery] int count = 10,
        [FromQuery] bool? excludePlayed = null)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var poolSize = Math.Clamp(configuration.CandidatePoolSize, 10, 1000);
        var requestedCount = Math.Clamp(count, 2, 30);
        var hidePlayed = excludePlayed ?? configuration.ExcludePlayedByDefault;
        var query = new InternalItemsQuery(user)
        {
            Recursive = true,
            IsFolder = false,
            ParentId = libraryId ?? Guid.Empty,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            Limit = poolSize,
            EnableTotalRecordCount = false,
            DtoOptions = new DtoOptions { Fields = [ItemFields.PrimaryImageAspectRatio] }
        };

        var candidates = _libraryManager.GetItemList(query)
            .Where(item => item.IsVisible(user) && item.MediaType == MediaType.Video)
            .Where(item => !hidePlayed || !item.IsPlayed(user, null))
            .ToList();

        if (candidates.Count == 0 && hidePlayed)
        {
            // A full library should still be usable: gracefully fall back to played content.
            candidates = _libraryManager.GetItemList(query)
                .Where(item => item.IsVisible(user) && item.MediaType == MediaType.Video)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return NotFound("No playable video items were found in this library.");
        }

        Shuffle(candidates);
        var selected = candidates[0];

        // Prefer unwatched content, but retain variety for a library containing mostly played items.
        if (configuration.PreferUnwatched && !selected.IsUnplayed(user, null))
        {
            var unwatched = candidates.FirstOrDefault(item => item.IsUnplayed(user, null));
            if (unwatched is not null)
            {
                selected = unwatched;
            }
        }

        var wheel = new[] { selected }
            .Concat(candidates.Where(item => item.Id != selected.Id))
            .Take(Math.Min(requestedCount, candidates.Count))
            .Select(item => ToQueueItem(item, user))
            .ToList();

        return Ok(new PickerResult(
            (mode ?? "dice").Trim().ToLowerInvariant() is "wheel" ? "wheel" : "dice",
            ToQueueItem(selected, user),
            wheel));
    }

    private QueueItem ToQueueItem(BaseItem item, User user)
    {
        var dto = _dtoService.GetBaseItemDto(item, new DtoOptions
        {
            Fields = [ItemFields.PrimaryImageAspectRatio],
            EnableImages = true,
            EnableUserData = true
        }, user);
        return new QueueItem(item.Id, item.Name, item.GetBaseItemKind().ToString(), $"/Items/{item.Id}/Images/Primary", dto);
    }

    private User? GetUser()
    {
        var id = User.FindFirstValue("Jellyfin-UserId")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(id, out var userId) ? _userManager.GetUserById(userId) : null;
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swap = Random.Shared.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }
}

/// <summary>Small playlist representation for the companion client.</summary>
public sealed record PlaylistSummary(Guid Id, string Name, int Count);

/// <summary>Queue information returned to the web client.</summary>
public sealed record QueuePlan(Guid PlaylistId, string PlaylistName, string Mode, Guid? SelectedId, IReadOnlyList<QueueItem> Items);

/// <summary>Playable item plus the normal Jellyfin DTO used by clients.</summary>
public sealed record QueueItem(Guid Id, string Name, string Kind, string ImagePath, BaseItemDto Dto);

/// <summary>Result for dice and wheel picker animations.</summary>
public sealed record PickerResult(string Mode, QueueItem Selected, IReadOnlyList<QueueItem> Candidates);
