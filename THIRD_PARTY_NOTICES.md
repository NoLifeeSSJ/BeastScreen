# Third-party notices

BeastScreen source: GPL-3.0-or-later; full licence in LICENSE. Copyright BeastScreen contributors. The release includes a matching application source archive and build scripts.

The application ZIP redistributes these components:

- .NET / WPF 10.0.13: Microsoft and contributors, MIT and third-party licences. Complete package-provided licence/notice files are copied to LICENSES/package-notices during packaging. Upstreams: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf.
- Microsoft.Data.Sqlite / Core 10.0.13: .NET Foundation and contributors, MIT; LICENSES/Microsoft-Data-Sqlite-MIT.txt; https://github.com/dotnet/efcore.
- CommunityToolkit.Mvvm 8.4.2: .NET Foundation and contributors, MIT; LICENSES/CommunityToolkit-MIT.md and CommunityToolkit-ThirdPartyNotices.txt, plus package-provided notices; https://github.com/CommunityToolkit/dotnet.
- SQLite: public domain; https://sqlite.org/copyright.html. SQLitePCLRaw 2.1.13: Copyright 2014-2024 SourceGear, LLC, Apache-2.0 according to the locked NuGet metadata; LICENSES/SQLitePCLRaw-Apache-2.0.txt and SQLitePCLRaw-NOTICE.txt. These upstream licence/notice texts were retrieved from v2.1.12; no exact 2.1.13 source tag was found. SQLite's public-domain notice and the wrapper's additional notices are preserved in that NOTICE file. https://github.com/ericsink/SQLitePCL.raw.
- Vulkan Loader / Headers 1.4.366: Khronos Group and contributors, Apache-2.0, MIT and permissive per-file exceptions. LICENSES/Vulkan-Loader.txt and the complete pinned source ZIPs in the corresponding-source release asset preserve their notices. Exact revisions and SHA256 values: docs/native-source-manifest.json.

The application ZIP does **not** contain libmpv-2.dll or ffmpeg.exe. During installation, the installer downloads the exact checksum-pinned archives in native-dependencies.json directly from https://github.com/shinchiro/mpv-winbuild-cmake/releases, then verifies both the archive and extracted-file hashes. These upstream projects retain their own licensing and source obligations:

- mpv: mpv/MPlayer/mplayer2 contributors, GPL-2.0-or-later for the selected default build; primary licence in LICENSES/mpv-GPL-2.0-or-later.txt; https://github.com/mpv-player/mpv.
- FFmpeg: FFmpeg developers and contributors; selected build reports --enable-gpl --enable-version3 and no --enable-nonfree. https://ffmpeg.org/legal.html and https://github.com/FFmpeg/FFmpeg.

The complete matching transitive source/licence inventory for those upstream prebuilt codec archives has not been established here. They must not be added to BeastScreen release assets without completing that work. The supplied corresponding-source archive covers the redistributed Vulkan sources; the separate application source archive covers BeastScreen. Neither claims to contain all sources used by the external codec distributor.
