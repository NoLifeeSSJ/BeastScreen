# Third-party notices and release status

BeastScreen source: GPL-3.0-or-later; full licence in LICENSE. Copyright BeastScreen contributors.

Development dependencies and primary upstreams:

- .NET / WPF / Microsoft.Data.Sqlite: Microsoft and contributors, MIT; https://github.com/dotnet/runtime, https://github.com/dotnet/wpf, https://github.com/dotnet/efcore.
- CommunityToolkit.Mvvm: .NET Foundation and contributors, MIT; https://github.com/CommunityToolkit/dotnet.
- SQLite: public domain; https://sqlite.org/copyright.html. SQLitePCLRaw: Eric Sink and contributors, Apache-2.0; https://github.com/ericsink/SQLitePCL.raw.
- mpv: mpv/MPlayer/mplayer2 contributors, GPL-2.0-or-later for the selected default build; full primary licence in LICENSES/mpv-GPL-2.0-or-later.txt. https://github.com/mpv-player/mpv. Selected binary provenance: native-dependencies.json.
- FFmpeg: FFmpeg developers and contributors; the exact selected binary's GPL/LGPL/nonfree configuration and all static dependency obligations **remain to be audited**. https://ffmpeg.org/legal.html, https://github.com/FFmpeg/FFmpeg.
- Vulkan Loader and Headers: Khronos Group and contributors, Apache-2.0 and permissive per-file exceptions; complete pinned source ZIPs preserve the notices. Primary loader text is in LICENSES/Vulkan-Loader.txt.

This is an initial development inventory, **not a complete distribution notice set**. In particular libmpv/FFmpeg statically include other projects whose exact revisions, notices and matching source must be established. Neither this document nor a link to a build repository satisfies those source obligations. Public binary publication is blocked in docs/release-audit.json until complete corresponding source and notices have been prepared and reviewed. Do not remove that gate to make a release job pass.
