using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.QueueToPlaylist.Configuration;

/// <summary>
/// Settings for the chooser and its candidate pool.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Maximum number of items considered by the library picker.</summary>
    public int CandidatePoolSize { get; set; } = 250;

    /// <summary>Hide played items from the picker unless the client asks to include them.</summary>
    public bool ExcludePlayedByDefault { get; set; } = true;

    /// <summary>Give unwatched items a higher chance in randomizer mode.</summary>
    public bool PreferUnwatched { get; set; } = true;
}
