# Test evidence and acceptance

## Current local evidence — 2026-10-24

Environment: Debian 13 x64, .NET SDK 10.0.402 / runtime 10.0.13, FFmpeg 7.1.5 from the host, PowerShell 7.6.8 (download verified against upstream SHA256 manifest). **This is not a Windows playback qualification.**

- Core suite: **37 passed, 0 failed, 0 skipped**. Includes SQLite schema guards/backup/restore/relocation, history/resume/favourites/playlists, 10,000-record paging, Unicode paths, playlist parsing/cycles/limits, forbidden protocols, redaction, JSON preferences, queue/repeat/shuffle, retry budget, cancellation/missing files, real embedded-audio tag extraction, extensionless video frame generation and corrupt/missing media helper failures.
- WPF `win-x64` cross-build: passed, **0 warnings and 0 errors**.
- Installer helpers on PowerShell 7: **19 assertions passed**. Real ZIP extraction, traversal/absolute-path/alternate-stream/case-duplicate rejection, SHA256 corruption/duplicate-manifest rejection, atomic state backup, owned-file removal and preservation of an unowned user file.
- Native Windows dependencies: libmpv archive and DLL, FFmpeg archive and official Vulkan source archives pinned by SHA256. PE imports inspected; missing Vulkan loader identified. Building that loader needs Windows C++ build tools and remains unrun locally.

Results are generated under ignored `artifacts/test-results`; CI retains current-run evidence. Counts must be updated when tests change. No zero-test result counts as a pass.

## Commands

```powershell
.\scripts\Restore-Native.ps1
dotnet restore BeastScreen.sln --locked-mode
dotnet test tests\BeastScreen.Tests --no-restore --logger trx --results-directory artifacts\test-results
dotnet build src\BeastScreen.Desktop --no-restore
.\tests\Installer.Tests.ps1
.\scripts\Generate-Fixtures.ps1
```

The Windows app supports `--smoke-test <fixture.mkv> --result <result.json>` and exits nonzero if real decode/duration/pause/seek/track/subtitle/screenshot checks fail. A synthetic eight-second 640×360 H.264/AAC file has two audio tracks and a separate SRT. The CI smoke uses WARP/software rendering and null audio on Windows Server 2025; it cannot prove audible output or Windows 11 GPU behaviour. `--health-check` checks native initialization; `--check-data` reads the user's existing schema without migrating it.

## Required interactive Windows 11 checks — not run

- Video visible inside the native surface; physical audio; rendered subtitle appearance; selected audio/video tracks; chapter/edition selection; seek/pause/duration and frame stepping.
- Hardware decode on supported H.264/HEVC/AV1, forced software decode and fallback; dropped frames, HDR-to-SDR and supported HDR output; audio-device changes and multichannel/passthrough with appropriate hardware.
- Keyboard and mouse when the native surface has focus, double-click/fullscreen, compact mode, topmost, resize/DPI transitions and multiple monitors; screen-reader labels, tab focus and contrast. WPF/native airspace must be observed, not inferred.
- Unicode, unusual/long names, extensionless files, removable/UNC/mapped sources; missing/corrupt/unsupported files; resume and playlist order across restart.
- Controlled local HTTP fixtures for direct media, HLS/DASH/radio, disconnect/reconnect/cancel/retry bound and live/nonseekable controls. Additional engine protocols require service fixtures.
- A codec/container/subtitle sample matrix as listed in compatibility.md. Generate or use redistributable media only; record codec/build details rather than extensions.

## Installer qualification — not run on a clean Windows machine

Run fresh install, repeated install, update between real releases, same-version repair, rollback, launch and uninstall both keeping/removing app data. Exercise every optional shortcut/association path, custom install path, lack of write permission, conflicting operation, running-player shutdown, wrong checksum/duplicate manifest, unavailable release, interrupted download/extraction/activation, and schema-too-new rollback. Verify the previous working application survives each failure. Confirm source and media remain untouched.

Repeat on a Windows 11 x64 VM with no Git, Python, Visual Studio, .NET SDK or separately installed .NET runtime. The package is self-contained, but only this test establishes clean-machine readiness. Windows PowerShell 5.1 must run the script as well as PowerShell 7. Do not convert unrun rows into completed milestones.
