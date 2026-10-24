# Compatibility evidence

The tested combinations below pass on Windows Server 2025 x64 using the exact selected native build ([CI evidence](https://github.com/NoLifeeSSJ/BeastScreen/actions/runs/40128220184)). Native tests decode actual frames or PCM; the WPF smoke uses WARP/software rendering and null audio. This is not Windows 11, audible-output, high-resolution or hardware-decode qualification. SRT loading is tested; subtitle appearance still needs inspection. Cross-compilation and file extensions do not establish other codec support. Native pins are in `native-dependencies.json`.

Tested synthetic combinations: MP4/H.264, MKV/HEVC, MKV/AV1, WebM/VP9, MPEG-PS/MPEG-2, M4A/AAC, FLAC, MP3, Ogg/Vorbis and Opus. Video matrix clips are 160×96, 24 fps, two seconds, 8-bit. The WPF fixture additionally tests MKV/H.264 at 640×360, 30 fps with two AAC tracks. Direct ranged HTTP, continuous MP3 radio, and static unencrypted HLS/MPEG-TS and DASH/fragmented MP4 carry generated H.264/AAC media through controlled loopback endpoints.

| Category | Targets | Current evidence |
| --- | --- | --- |
| Containers | MP4, MKV, AVI, MOV, WebM, MPEG/MPG, TS/M2TS/MTS, WMV, FLV, OGV, VOB, 3GP | Specific MP4/MKV/WebM/MPEG-PS and HLS/MPEG-TS combinations above tested; other combinations expected and untested |
| Video | H.264, HEVC, AV1, VP8/9, MPEG-2, MPEG-4 Part 2, VC-1, MJPEG, ProRes | H.264, HEVC, AV1, VP9 and MPEG-2 combinations above tested; other codecs expected where compiled in |
| Audio | MP3, AAC/M4A, FLAC, WAV, Vorbis, Opus, ALAC, WMA, AIFF, AC-3, E-AC-3, DTS | AAC, FLAC, MP3, Vorbis and Opus decoded with null output; other combinations untested. Passthrough needs compatible hardware |
| Subtitles | SRT, ASS/SSA, WebVTT, embedded, PGS, VobSub | SRT track loading tested; visual text rendering and other formats unverified |
| Streams | Direct HTTP(S), unencrypted HLS/DASH, radio, IPTV playlists, RTSP, RTMP, RTP, UDP, SRT | Controlled ranged HTTP, continuous radio, static HLS/DASH, retry limits and cancellation tested. HTTPS/authentication, live adaptive manifests and remaining protocols unverified |
| Files | Unicode, spaces, UNC, mapped/removable drives, extensionless files, long paths | Parser/storage tests cover Unicode; filesystem/platform checks outstanding |
| Picture | VFR, high bit depth, HDR, scaling, hardware decode | Hardware/build dependent; not qualified |
| Audio CD | Engine/build dependent `cdda` support | Not enabled or tested in UI |
| DVD/VIDEO_TS | Unencrypted titles, chapters | Backend investigation pending; **mpv has no DVD menu support** |
| Blu-ray/ISO | Unencrypted structures, engine libbluray support | Not enabled or tested; no menu claim |
| Websites | Optional yt-dlp | Not packaged or enabled; direct media URLs only |
| Protected media | Subscription DRM, encrypted commercial discs | Unsupported; no circumvention or cookie scraping |

`scripts/Generate-Fixtures.ps1` creates the base two-track/SRT sample; `scripts/Generate-ExtendedFixtures.ps1` creates the codec and adaptive-stream fixtures with the pinned FFmpeg helper. `--smoke-test` checks the native WPF surface, and the 15-case native suite includes `--network-smoke-test` against its own loopback server. Neither establishes physical output or hardware decoding. See testing.md before promoting additional combinations to tested.
