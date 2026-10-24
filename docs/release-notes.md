# BeastScreen v0.1.0-preview.1

First public Windows 11 x64 preview (24H2/build 26100 or newer). Native WPF/libmpv playback, a local SQLite library, playlists, resume/bookmarks, track/subtitle controls, background scanning, metadata/thumbnails, direct HTTP/radio/HLS/DASH playback and the maintenance installer are included. User reports basic Windows 11 operation; automated validation runs on Windows Server.

Install the release's BeastScreen-Install.ps1 with `-Action Install -Version v0.1.0-preview.1`. The app and .NET runtime are self-contained. No developer tools are required. Initial installation downloads checksum-pinned mpv/FFmpeg archives directly from their upstream distributor using Windows built-in tar; those codec binaries are absent from our ZIP. Local playback then works offline. The default installation is C:\BeastScreen\app, with a Start Menu shortcut; user data stays in %LOCALAPPDATA%\BeastScreen.

Assets include the Windows app ZIP, PowerShell installer, SHA256 manifest, SPDX dependency inventory, upstream runtime pins, application source and matching Vulkan source/notices. See THIRD_PARTY_NOTICES.md in the app/source archives for distribution scope.

This preview is unsigned. Detailed Windows 11 hardware/audio/HDR/focus and pristine-machine lifecycle qualification remain pending. It is not a qualified stable release. Windows 10, native ARM64, DRM playback, disc menus, system media transport controls and website extraction are not delivered. Preview installation must name this explicit version; GitHub's latest endpoint excludes prereleases.
