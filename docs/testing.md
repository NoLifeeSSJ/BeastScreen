# Test evidence and acceptance

## Current local evidence — 2026-10-24

Environment: Debian 13 x64, .NET SDK 10.0.402 / runtime 10.0.13, FFmpeg 7.1.5 from the host, PowerShell 7.6.8 (download verified against upstream SHA256 manifest). **This is not a Windows playback qualification.**

- Core suite: **41 passed, 0 failed, 0 skipped** on Linux. Includes SQLite schema guards/backup/restore/relocation, history/resume/favourites/playlists, 10,000-record paging, Unicode paths, playlist parsing/cycles/limits/local HLS, forbidden protocols, URL/header credential redaction, JSON preferences, queue/repeat/shuffle, retry budget, cancellation/missing files, real embedded-audio tag extraction, extensionless video frame generation and corrupt/missing media helper failures.
- Follow-up suite: **50 core tests pass locally**, including real HTTP ranges/failures/containment and seven live-source classification regressions. [First expanded Windows run](https://github.com/NoLifeeSSJ/BeastScreen/actions/runs/40125524966) passed all 12 native codec/error cases but failed the network scenario on live-radio duration classification. HTTP recovery, seeking, retry exhaustion and cancellation completed before that failure. The radio and smoke exit-code fixes, bookmark privacy check and new expected-failure exit test await the next Windows run (14 native cases); do not count those fixes as Windows-verified yet.
- WPF `win-x64` cross-build: passed, **0 warnings and 0 errors**.
- Installer helpers on PowerShell 7: **22 assertions passed**. Real ZIP extraction, traversal/absolute-path/alternate-stream/case-duplicate rejection, SHA256 corruption/duplicate-manifest rejection, atomic state backup, owned-file removal, interrupted-activation recovery and preservation of unowned files.
- Native Windows dependencies: libmpv archive and DLL, FFmpeg archive and official Vulkan source archives pinned by SHA256. PE imports inspected; required Vulkan loader copies supplied for both libmpv and FFmpeg. The loader successfully builds in Windows CI.

## Windows Server CI evidence

[Run 40123983733](https://github.com/NoLifeeSSJ/BeastScreen/actions/runs/40123983733), source commit `42c7cb8`, completed successfully on `windows-2025`: frozen restore, Release build, **41 tests (0 failed, 0 skipped)**, 22 installer helper assertions under **both PowerShell 7 and Windows PowerShell 5.1**, actual playback smoke, self-contained packaging, and **9 controlled installer lifecycle assertions**. The separate dependency-review job also passes. The earlier complete run `40123368941` verified the preceding source version.

The real playback test decodes a synthetic Matroska/H.264 640×360 30 fps file with AAC audio tracks. It checks position/duration, pause, exact seeking, the selected audio-track property, external SRT loading, actual native child-window size changes, fullscreen window state and a generated frame screenshot. WARP/software rendering and null audio are deliberate in this server environment. Physical speaker output, text appearance, input focus, hardware decode and Windows 11 behaviour remain unverified. Shutdown/redaction changes are included in the tested source commit.

The lifecycle test uses the actual self-contained executable and native startup/data checks while substituting a controlled release transport and registration observation. It exercises fresh/repeated install, update, repair, rollback, corrupt-checksum rejection, preservation of the previous active version and uninstall preserving an unowned file. It is **not** a test of real GitHub Release downloads, actual shortcut/association registration or a pristine Windows 11 installation.

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

Windows 11 access is unavailable, as confirmed by the user on 2026-10-24. This does not block controlled Windows Server tests or justify qualifying Windows 11 without testing it.

- Video visible inside the native surface; physical audio; rendered subtitle appearance; selected audio/video tracks; chapter/edition selection; seek/pause/duration and frame stepping.
- Hardware decode on supported H.264/HEVC/AV1, forced software decode and fallback; dropped frames, HDR-to-SDR and supported HDR output; audio-device changes and multichannel/passthrough with appropriate hardware.
- Keyboard and mouse when the native surface has focus, double-click/fullscreen, compact mode, topmost, resize/DPI transitions and multiple monitors; screen-reader labels, tab focus and contrast. WPF/native airspace must be observed, not inferred.
- Unicode, unusual/long names, extensionless files, removable/UNC/mapped sources; missing/corrupt/unsupported files; resume and playlist order across restart.
- Controlled local HTTP fixtures for direct media, HLS/DASH/radio, disconnect/reconnect/cancel/retry bound and live/nonseekable controls. Additional engine protocols require service fixtures.
- A codec/container/subtitle sample matrix as listed in compatibility.md. Generate or use redistributable media only; record codec/build details rather than extensions.

## Installer qualification — not run on a clean Windows machine

Run fresh install, repeated install, update between real releases, same-version repair, rollback, launch and uninstall both keeping/removing app data. Exercise every optional shortcut/association path, custom install path, lack of write permission, conflicting operation, running-player shutdown, wrong checksum/duplicate manifest, unavailable release, interrupted download/extraction/activation, and schema-too-new rollback. Verify the previous working application survives each failure. Confirm source and media remain untouched.

Repeat on a Windows 11 x64 VM with no Git, Python, Visual Studio, .NET SDK or separately installed .NET runtime. The package is self-contained, but only this test establishes clean-machine readiness. Windows PowerShell 5.1 must run the script as well as PowerShell 7. Do not convert unrun rows into completed milestones.
