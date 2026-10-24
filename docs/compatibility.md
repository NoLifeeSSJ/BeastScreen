# Compatibility evidence

The synthetic combination **Matroska + H.264 (8-bit, 640×360, 30 fps) + two AAC tracks** passes actual libmpv playback/seek/screenshot smoke on Windows Server 2025 x64 with WARP/software rendering and null audio ([CI evidence](https://github.com/NoLifeeSSJ/BeastScreen/actions/runs/40123983733)). This is not Windows 11, audible-output, high-resolution or hardware-decode qualification. SRT loading is tested; subtitle appearance still needs inspection. Cross-compilation and file extensions do not establish other codec support. The selected native build is pinned in `native-dependencies.json`; hardware, drivers and build options affect compatibility.

| Category | Targets | Current evidence |
| --- | --- | --- |
| Containers | MP4, MKV, AVI, MOV, WebM, MPEG/MPG, TS/M2TS/MTS, WMV, FLV, OGV, VOB, 3GP | MKV combination above tested; other combinations expected and untested |
| Video | H.264, HEVC, AV1, VP8/9, MPEG-2, MPEG-4 Part 2, VC-1, MJPEG, ProRes | H.264 combination above tested; other codecs expected where compiled in; matrix required |
| Audio | MP3, AAC/M4A, FLAC, WAV, Vorbis, Opus, ALAC, WMA, AIFF, AC-3, E-AC-3, DTS | AAC with null output tested; other playback combinations untested. FFmpeg FLAC tags/frame helper tests pass. Passthrough needs compatible hardware |
| Subtitles | SRT, ASS/SSA, WebVTT, embedded, PGS, VobSub | SRT track loading tested; visual text rendering and other formats unverified |
| Streams | Direct HTTP(S), unencrypted HLS/DASH, radio, IPTV playlists, RTSP, RTMP, RTP, UDP, SRT | UI accepts selected protocols; controlled Windows/network tests outstanding |
| Files | Unicode, spaces, UNC, mapped/removable drives, extensionless files, long paths | Parser/storage tests cover Unicode; filesystem/platform checks outstanding |
| Picture | VFR, high bit depth, HDR, scaling, hardware decode | Hardware/build dependent; not qualified |
| Audio CD | Engine/build dependent `cdda` support | Not enabled or tested in UI |
| DVD/VIDEO_TS | Unencrypted titles, chapters | Backend investigation pending; **mpv has no DVD menu support** |
| Blu-ray/ISO | Unencrypted structures, engine libbluray support | Not enabled or tested; no menu claim |
| Websites | Optional yt-dlp | Not packaged or enabled; direct media URLs only |
| Protected media | Subscription DRM, encrypted commercial discs | Unsupported; no circumvention or cookie scraping |

`scripts/Generate-Fixtures.ps1` creates redistributable synthetic media with two audio tracks and SRT, and additional codec samples when an explicit ffmpeg executable is supplied. `--smoke-test` checks real decoding, duration, pause, seek, track selection and a screenshot on Windows; it does not establish physical audio output or hardware decoding. See testing.md before promoting any row to tested.
