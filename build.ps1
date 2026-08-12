param(
    [string]$Configuration = 'Release',
    [string]$Output = '.\artifacts\plugin'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Jellyfin.Plugin.QueueToPlaylist\Jellyfin.Plugin.QueueToPlaylist.csproj'

dotnet publish $project -c $Configuration -o $Output
New-Item -ItemType Directory -Force -Path (Join-Path $Output 'web') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'web\queue-to-playlist.js') (Join-Path $Output 'web') -Force
Copy-Item (Join-Path $PSScriptRoot 'web\queue-to-playlist.css') (Join-Path $Output 'web') -Force
Write-Host "Published Queue to Playlist to $Output"
Write-Host "Copy the DLL into a Jellyfin plugins subfolder named QueueToPlaylist_0.1.0.0."
