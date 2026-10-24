# Dependency provenance and distribution

Checked on 2026-10-24 against Microsoft's .NET release metadata, NuGet flat-container version indexes, the mpv project's Windows build documentation, and the selected upstream build release. Network policy blocks `learn.microsoft.com`, `mpv.io` and `api.github.com` here; official source documentation was read through `raw.githubusercontent.com` instead.

| Component | Pin | Evidence / licensing |
| --- | --- | --- |
| .NET SDK | 10.0.402 | [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json); SDK archive SHA512 verified before use; `global.json` disallows roll-forward |
| Runtime | 10.0.13, self-contained win-x64 | Microsoft's active .NET 10 LTS release; [support/OS table](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) |
| CommunityToolkit.Mvvm | 8.4.2 | NuGet, MIT; content hashes in lockfiles |
| Microsoft.Data.Sqlite / Core | 10.0.13 | NuGet, MIT; content hashes in lockfiles |
| SQLitePCLRaw bundle/core/lib/provider | 2.1.13 | NuGet; SQLite public domain, wrapper Apache-2.0; verify packaged notices before release |
| libmpv | 20261021, mpv e471496c56 | shinchiro x86_64 baseline build, **not x86-64-v3**; upstream URL, archive SHA256, DLL SHA256 and workflow provenance in native-dependencies.json |
| FFmpeg helper | 20261021, 9a861e968 | Same upstream build pipeline; only ffmpeg.exe (no ffprobe in this asset); pinned archive hash |
| Vulkan loader / headers | 1.4.366, exact full commits | Khronos source ZIPs with pinned SHA256; loader built locally with static MSVC runtime; source retains Apache-2.0/MIT/per-file exceptions |
| Unit test runner | Microsoft.NET.Test.Sdk 18.11.0, xUnit 2.9.3, adapter 4.0.1 | Exact NuGet pins and lockfiles; test tools are not application dependencies |

The selected libmpv DLL imports `vulkan-1.dll` even with D3D11 rendering. `Restore-Native.ps1` therefore builds the loader from pinned official sources instead of assuming the GPU driver installed it. Vulkan hardware is not required for D3D11; the dependency is the loader library. Windows provides the UCRT and system DLLs. No system codec pack is used. Confirm all actual imports and native minimum-OS requirements on a clean machine before qualification.

## Release gate

The application code is GPL-3.0-or-later, compatible with choosing GPLv3 terms for GPLv2-or-later mpv code. That choice alone **does not complete distribution obligations**. The supplied prebuilt mpv/FFmpeg archives do not contain an exact complete inventory/source bundle for all static dependencies. A build-script repository link is not a substitute for complete corresponding source.

Before publishing, obtain exact source revisions, configuration/patches, build scripts and licence texts for all included native components (FFmpeg, mpv, libplacebo, libass and each compiled dependency), inspect GPL/nonfree configuration, verify redistribution rights, and stage complete matching materials in `artifacts/audited-corresponding-source` with a verified `SOURCE-MANIFEST.sha256`. Record evidence in docs/release-audit.json. Packaging with `-ForRelease` and the release workflow fail closed until these checks and Windows acceptance are recorded.

The local SDK archive used here is `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-linux-x64.tar.gz`, SHA512 `1904a98dda2db34ba78ba3c4fb1b224455329af91c341d83547843c1839e112b04ae699ee92fa2449a8f62249196a938723078c0c60b9f65c88539d5687729c2`.

No code signing identity is available or used. Checksums provide integrity relative to their manifest; they do not prove publisher identity. Native vulnerability review is separate from NuGet vulnerability output. Generated packages and helper binaries are ignored by Git.
