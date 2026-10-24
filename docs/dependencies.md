# Dependency provenance and distribution

Checked on 2026-10-24 against Microsoft's .NET release metadata, NuGet flat-container version indexes, the mpv project's Windows build documentation, and the selected upstream build release. Official source documentation, release metadata and GitHub asset digests were inspected; archive hashes remain pinned in the repository.

| Component | Pin | Evidence / licensing |
| --- | --- | --- |
| .NET SDK | 10.0.402 | [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json); SDK archive SHA512 verified before use; `global.json` disallows roll-forward |
| Runtime | 10.0.13, self-contained win-x64 | Microsoft's active .NET 10 LTS release; [support/OS table](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) |
| CommunityToolkit.Mvvm | 8.4.2 | NuGet, MIT; content hashes in lockfiles |
| Microsoft.Data.Sqlite / Core | 10.0.13 | NuGet, MIT; content hashes in lockfiles |
| SQLitePCLRaw bundle/core/lib/provider | 2.1.13 | NuGet; SQLite public domain, wrapper Apache-2.0; packaged licence and NOTICE retained |
| libmpv | 20261021, mpv e471496c56 | shinchiro x86_64 baseline build, **not x86-64-v3**; upstream URL, archive SHA256, DLL SHA256 and workflow provenance in native-dependencies.json |
| FFmpeg helper | 20261021, 9a861e968 | Same upstream build pipeline; only ffmpeg.exe (no ffprobe in this asset); pinned archive hash |
| Vulkan loader / headers | 1.4.366, exact full commits | Khronos source ZIPs with pinned SHA256; loader built locally with static MSVC runtime; source retains Apache-2.0/MIT/per-file exceptions |
| Unit test runner | Microsoft.NET.Test.Sdk 18.11.0, xUnit 2.9.3, adapter 4.0.1 | Exact NuGet pins and lockfiles; test tools are not application dependencies |

The selected libmpv DLL and FFmpeg executable both import `vulkan-1.dll`, including with D3D11 rendering or audio-only inspection. `Restore-Native.ps1` builds the loader from pinned official sources and places a copy beside each consumer instead of assuming the GPU driver installed it. Vulkan hardware is not required for D3D11; the dependency is the loader library. PE imports confirm UCRT/system dependencies and no separate Visual C++ runtime import for the selected native builds; the loader uses static MSVC runtime. No system codec pack is used. Confirm all native minimum-OS requirements on a clean machine before qualification.

The selected FFmpeg executable embeds `--enable-gpl --enable-version3` and no `--enable-nonfree` flag, with OpenSSL and the codec libraries listed by its full build configuration. This is useful audit evidence, not a complete rights/source audit. The helper build explicitly disables ffprobe; BeastScreen uses FFmetadata output for local tags.

## Release payload and upstream downloads

The application ZIP excludes mpv and FFmpeg binaries. The installer downloads their exact pinned archives directly from the upstream distributor, checks the archive size and SHA256, extracts only the required root member with Windows built-in tar.exe, then verifies that file's SHA256. Initial installation therefore needs internet access to GitHub; local playback works offline afterward. No SDK, Git, Visual Studio or separately installed 7-Zip is required on the destination Windows 11 machine.

The complete transitive source/licence inventory for those prebuilt codec archives remains unestablished. They must not be repackaged into our release ZIP until matching complete source and notices are obtained. The preview uses upstream delivery rather than claiming that audit is complete.

The redistributed Vulkan loader is built from the exact loader/header commits in docs/native-source-manifest.json. Their complete source ZIPs and notices accompany the release. The application source/build scripts are provided separately. Package-provided .NET runtime and CommunityToolkit notices, Microsoft.Data.Sqlite's MIT licence, and SQLitePCLRaw's Apache licence/NOTICE accompany the app. SQLitePCLRaw 2.1.13's NuGet metadata declares Apache-2.0; its licence/notice text was retrieved from upstream v2.1.12 because no exact 2.1.13 source tag was found.

Preview publication is explicitly authorized by the user and requires the tagged build/tests and installer checks. Stable publication still requires full Windows 11 playback and clean-machine acceptance. The audit record distinguishes those requirements in docs/release-audit.json.

The local SDK archive used here is `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-linux-x64.tar.gz`, SHA512 `1904a98dda2db34ba78ba3c4fb1b224455329af91c341d83547843c1839e112b04ae699ee92fa2449a8f62249196a938723078c0c60b9f65c88539d5687729c2`.

No code signing identity is available or used. Checksums provide integrity relative to their manifest; they do not prove publisher identity. Native vulnerability review is separate from NuGet vulnerability output. Generated packages and helper binaries are ignored by Git.
