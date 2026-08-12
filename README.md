# Queue to Playlist (Jellyfin 10.11.11 prototype)

Queue to Playlist is a small server plugin plus an optional web-client companion for people who
want a little less decision fatigue:

- a **Next up** panel for video playlists;
- **Shuffle & play**, which persists a Fisher–Yates shuffle to the playlist;
- **Randomizer**, which creates a fresh queue plan and starts at a random item without rewriting the
  playlist; and
- **What should I watch?**, with dice-roll and wheel presentation modes backed by a library-wide
  picker that prefers unwatched videos.

This is intentionally a prototype. Jellyfin plugins cannot safely patch every official client at
runtime, so the server DLL owns the authenticated selection logic and the optional JavaScript/CSS
companion owns presentation. The companion is compatible with the JS Injector plugin and can also
be included in a custom `jellyfin-web` build.

## Build

The project targets `net9.0` and references the Jellyfin 10.11.11 packages. On a machine with the
.NET 9 SDK:

```powershell
dotnet restore .\Jellyfin.Plugin.QueueToPlaylist\Jellyfin.Plugin.QueueToPlaylist.csproj
dotnet publish .\Jellyfin.Plugin.QueueToPlaylist\Jellyfin.Plugin.QueueToPlaylist.csproj -c Release -o .\artifacts\plugin
# or: .\build.ps1
```

Copy the published DLL (and its PDB when debugging) into a folder named
`QueueToPlaylist_0.1.0.0` under the Jellyfin `plugins` directory, then restart the server. Typical
locations are `C:\ProgramData\Jellyfin\Server\plugins` on Windows and
`/var/lib/jellyfin/plugins` on Debian packages; use the path shown by your Jellyfin installation.

## Easiest GitHub release workflow

After uploading this project to GitHub, commit the files under `.github/workflows/`. Then create a
version tag from the repository's **Releases → Draft a new release** page, using a tag such as
`v0.1.0`. GitHub Actions will compile the plugin and attach
`QueueToPlaylist_0.1.0.zip` to that release. Download that ZIP, extract the inner
`QueueToPlaylist_0.1.0` folder into Jellyfin's `plugins` directory, and restart Jellyfin.

The workflow also stores the ZIP as an Actions artifact, so you can test a build before publishing
a release. The repository must have Actions enabled and the first run may take a few minutes while
NuGet downloads the Jellyfin 10.11.11 dependencies.

## Install the companion UI

Add the contents of [`web/queue-to-playlist.js`](web/queue-to-playlist.js) and
[`web/queue-to-playlist.css`](web/queue-to-playlist.css) to the JS Injector plugin, or include them
in a custom web build. The UI uses the normal Jellyfin access token and the plugin's
`/QueueToPlaylist/*` API; it never reads media paths or bypasses Jellyfin permissions.

After restart, open Dashboard → Plugins → Queue to Playlist to adjust the candidate pool and
played-item behavior. The floating panel appears after the companion script is loaded. It shows the
next six items and provides playlist shuffle/randomizer plus dice/wheel picker buttons.

## API sketch

| Endpoint | Purpose |
| --- | --- |
| `GET /QueueToPlaylist/playlists` | Video playlists visible to the caller |
| `GET /QueueToPlaylist/playlists/{id}/queue?mode=ordered\|shuffle\|randomizer&excludeId={id}` | Queue plan and next-up items; randomizer can avoid the currently playing item |
| `POST /QueueToPlaylist/playlists/{id}/shuffle` | Persist a shuffled playlist order |
| `GET /QueueToPlaylist/picker?mode=dice\|wheel&libraryId={id}` | Pick a playable library item and candidate cards |

All endpoints require the normal Jellyfin authenticated session. The plugin filters candidates with
the signed-in user's visibility and played-state rules before returning anything.

## Known prototype limits

- The companion starts playback through `window.playbackManager`, which is present in Jellyfin Web;
  a client that does not expose that object falls back to opening the selected item's details page.
- The server picker currently returns movies and episodes. A later iteration could add music, a
  “shortest first” mode, genre/rating filters, history-based de-duplication, or a party-mode lock.
- The plugin is source-first; the checked-in `manifest.json` is a publishing example and its
  placeholder source URL/checksum must be replaced before adding it to a real repository.

Jellyfin plugins are GPL-compatible projects because they link to Jellyfin's GPL server assemblies.
