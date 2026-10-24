# Test evidence and acceptance

## Current local evidence — 2026-10-24

Environment: Debian 13 x64, .NET SDK 10.0.402 / runtime 10.0.13, FFmpeg 7.1.5 from the host, PowerShell 7.6.8 (download verified against upstream SHA256 manifest). **This is not a Windows playback qualification.**

- Core suite: **50 passed, 0 failed, 0 skipped** on Linux. Includes SQLite schema guards/backup/restore/relocation, history/resume/favourites/playlists, 10,000-record paging, Unicode paths, playlist parsing/cycles/limits/local HLS, forbidden protocols, URL/header credential redaction, JSON preferences, queue/repeat/shuffle, retry budgets, live-source timing, real HTTP ranges/failures/path containment, cancellation/missing files, actual local tag/frame extraction and corrupt media helper failures.
- DASH fixture transport: the host FFmpeg decoded generated DASH through the loopback server in a separate focused test. Native Windows playback runs only in the Windows job, not as a silent skip on Linux.
- WPF `win-x64` cross-build: passed, **0 warnings and 0 errors**.
- Installer helpers on PowerShell 7: **22 assertions passed**. Real ZIP extraction, traversal/absolute-path/alternate-stream/case-duplicate rejection, SHA256 corruption/duplicate-manifest rejection, atomic state backup, owned-file removal, interrupted-activation recovery and preservation of unowned files.
- Native Windows dependencies: libmpv archive and DLL, FFmpeg archive and official Vulkan source archives pinned by SHA256. PE imports inspected; required Vulkan loader copies supplied for both libmpv and FFmpeg. The loader successfully builds in Windows CI.

## Windows Server CI evidence

[Run 40128220184](https://github.com/NoLifeeSSJ/BeastScreen/actions/runs/40128220184), source commit `49e92ec`, completed successfully on `windows-2025`: frozen restore, Release build, **50 core tests and 15 native/integration cases (65 passed, 0 failed, 0 skipped)**, 22 installer helper assertions under **both PowerShell 7 and Windows PowerShell 5.1**, the WPF playback smoke, self-contained packaging, and **9 controlled installer lifecycle assertions**. The separate dependency-review job also passes. Run `40123983733` is the preceding baseline evidence.

The real playback test decodes a synthetic Matroska/H.264 640×360 30 fps file with AAC audio tracks. It checks position/duration, pause, exact seeking, the selected audio-track property, external SRT loading, actual native child-window size changes, fullscreen window state and a generated frame screenshot. WARP/software rendering and null audio are deliberate in this server environment. Physical speaker output, text appearance, input focus, hardware decode and Windows 11 behaviour remain unverified. Shutdown/redaction changes are included in the tested source commit.

The lifecycle test uses the actual self-contained executable and native startup/data checks while substituting a controlled release transport and registration observation. It exercises fresh/repeated install, update, repair, rollback, corrupt-checksum rejection, preservation of the previous active version and uninstall preserving an unowned file. It is **not** a test of real GitHub Release downloads, actual shortcut/association registration or a pristine Windows 11 installation.

The native suite generates real PNG frames from H.264/MP4, HEVC/MKV, AV1/MKV, VP9/WebM and MPEG-2/MPEG-PS. It checks decoded PCM parameters for AAC/M4A, FLAC, MP3, Vorbis/Ogg and Opus. Video matrix samples are two seconds, 160×96, 24 fps, 8-bit; these are correctness checks, not high-resolution performance results. Missing/corrupt inputs must raise real native errors, and an intentionally invalid WPF smoke invocation must produce a failure report and exit code 1. That negative fixture is a passing assertion, not a skipped or accepted test failure.

The real WPF player's network scenario uses a disposable loopback-only HTTP server with byte ranges, controlled 503s, a continuously served MP3 radio feed, static unencrypted HLS and DASH. It verifies recovery, seeking, exactly four requests for an initial failure plus three retries, cancellation with no extra request, live/nonseekable controls, zero live resume offsets, and refusal to persist query-bearing stream URLs or live bookmarks. A separate test decodes the DASH fixture using the packaged FFmpeg helper. Windows fixture paths use forward slashes for FFmpeg's DASH muxer and verify all fragments remain beside the manifest.

The tests exposed and repaired growing radio-duration estimates being treated as finite media, bookmarks persisting private stream URLs, incorrect smoke failure exit codes, and misplaced Windows DASH fixtures. Bounded, redacted engine diagnostics are enabled for explicit smoke modes only. These controlled HTTP tests do not qualify HTTPS authentication, external services, live adaptive manifests, RTSP, multicast or interrupted physical networks.

Results are generated under ignored `artifacts/test-results`; CI retains current-run evidence. Counts must be updated when tests change. No zero-test result counts as a pass.

## Commands

```powershell
.\scripts\Restore-Native.ps1
dotnet restore BeastScreen.sln --locked-mode
dotnet test tests\BeastScreen.Tests --no-restore --logger trx --results-directory artifacts\test-results
dotnet build src\BeastScreen.Desktop --no-restore
.\tests\Installer.Tests.ps1
.\scripts\Generate-Fixtures.ps1
# On Windows, after a Release build:
dotnet build BeastScreen.sln -c Release --no-restore
$fixture=(Get-ChildItem artifacts\fixtures -Filter *.mkv).FullName
.\scripts\Generate-ExtendedFixtures.ps1 -BaseFixture $fixture
dotnet test tests\BeastScreen.PlaybackTests -c Release --no-build --logger trx --results-directory artifacts\test-results
```

The Windows app supports `--smoke-test <fixture.mkv> --result <result.json>` and exits nonzero if real decode/duration/pause/seek/track/subtitle/screenshot checks fail. A synthetic eight-second 640×360 H.264/AAC file has two audio tracks and a separate SRT. The CI smoke uses WARP/software rendering and null audio on Windows Server 2025; it cannot prove audible output or Windows 11 GPU behaviour. `--health-check` checks native initialization; `--check-data` reads the user's existing schema without migrating it.

## Required interactive Windows 11 checks — not run

The user now reports BeastScreen working on Windows 11. This confirms basic operation, but the source version, machine details and the individual checks below have not been supplied; their detailed outcomes remain unverified.

- Video visible inside the native surface; physical audio; rendered subtitle appearance; selected audio/video tracks; chapter/edition selection; seek/pause/duration and frame stepping.
- Hardware decode on supported H.264/HEVC/AV1, forced software decode and fallback; dropped frames, HDR-to-SDR and supported HDR output; audio-device changes and multichannel/passthrough with appropriate hardware.
- Keyboard and mouse when the native surface has focus, double-click/fullscreen, compact mode, topmost, resize/DPI transitions and multiple monitors; screen-reader labels, tab focus and contrast. WPF/native airspace must be observed, not inferred.
- Unicode, unusual/long names, extensionless files, removable/UNC/mapped sources; missing/corrupt/unsupported files; resume and playlist order across restart.
- Real network interruption/recovery and additional service/protocol fixtures; the controlled direct-HTTP, static HLS/DASH and radio scenarios above already pass on Windows Server.
- A codec/container/subtitle sample matrix as listed in compatibility.md. Generate or use redistributable media only; record codec/build details rather than extensions.

## Installer qualification — not run on a clean Windows machine

Run fresh install, repeated install, update between real releases, same-version repair, rollback, launch and uninstall both keeping/removing app data. Exercise every optional shortcut/association path, custom install path, lack of write permission, conflicting operation, running-player shutdown, wrong checksum/duplicate manifest, unavailable release, interrupted download/extraction/activation, and schema-too-new rollback. Verify the previous working application survives each failure. Confirm source and media remain untouched.

Repeat on a Windows 11 x64 VM with no Git, Python, Visual Studio, .NET SDK or separately installed .NET runtime. The package is self-contained, but only this test establishes clean-machine readiness. Windows PowerShell 5.1 must run the script as well as PowerShell 7. Do not convert unrun rows into completed milestones.
