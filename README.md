# BeastScreen

A native C# / .NET 10 WPF media player for Windows 11 x64, using libmpv for decoding and rendering, SQLite for the local library, and JSON preferences. GPL-3.0-or-later.

**Development preview: not yet a qualified release.** The application cross-compiles on Linux. Interactive Windows playback, the clean-machine installer lifecycle, hardware decoding and the native redistribution audit are release gates. See [test evidence](docs/testing.md) and [milestones](docs/roadmap.md). There is currently no published installer package. Do not treat a green cross-build as proof of Windows playback.

## Build on Windows

Use a dedicated source checkout at `C:\BeastScreen\repo`. Build prerequisites are .NET SDK **10.0.402**, Visual Studio 2022/2026 C++ build tools with CMake, and 7-Zip. These are development prerequisites only; packaged builds are self-contained.

```powershell
Set-Location C:\BeastScreen\repo
.\scripts\Restore-Native.ps1
dotnet restore BeastScreen.sln --locked-mode
dotnet test tests\BeastScreen.Tests --no-restore
dotnet run --project src\BeastScreen.Desktop
```

On Linux, restore, test the core, and cross-build WPF with `EnableWindowsTargeting`; you cannot run the WPF UI there. No extra worktree is needed. See [architecture](docs/architecture.md) for the native rendering approach and [dependencies](docs/dependencies.md) for exact provenance and licensing gates.

## Implemented code

- Native embedded video with a dedicated libmpv event thread and deterministic shutdown. WPF controls occupy separate layout areas; dialogs use their own native window.
- File/folder/URL opening, file drop, queue, seeking, speed, shuffle/repeat, A–B loops, chapters, editions, audio/video/subtitle tracks, external subtitles, delay, crop, rotation, zoom, screenshots and diagnostics.
- Selected-folder background scans, cancellation, debounced folder notifications, missing records, search/sort, paginated list/grid, favourites, resume/history, bookmarks, playlist persistence, M3U/M3U8/PLS import/export, SQLite backup/restore and relocation.
- JSON playback/picture/audio preferences, keyboard shortcuts, fullscreen, mini player, media-key messages, per-user single instance with file forwarding, optional Open With registration, and maintenance installer source.

These capabilities are implemented but Windows behaviour still needs validation. Artist/album/genre fields are supported by storage and search; automatic embedded metadata and thumbnail/artwork generation, Windows system media transport controls, website extraction and disc navigation are not delivered. No external metadata service or telemetry is enabled.

## Data and installation

Application files belong in `C:\BeastScreen\app`, downloads in `C:\BeastScreen\downloads`, and user data in `%LOCALAPPDATA%\BeastScreen`. Source and media are never installation cleanup targets. See [installation](docs/installation.md), [compatibility](docs/compatibility.md), and [troubleshooting](docs/troubleshooting.md).

No universal codec claim is made. DRM services and encrypted commercial discs are unsupported. Native ARM64 and Windows 10 are not advertised.
