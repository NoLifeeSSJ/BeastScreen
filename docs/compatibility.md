# Compatibility evidence

No Windows playback combination has yet been marked tested. Cross-compilation and unit tests do not establish codec support. The selected native build is pinned in `native-dependencies.json`; hardware, drivers and build options affect compatibility.

| Category | Targets | Current evidence |
| --- | --- | --- |
| Containers | MP4, MKV, AVI, MOV, WebM, MPEG/MPG, TS/M2TS/MTS, WMV, FLV, OGV, VOB, 3GP | Expected from mpv/FFmpeg, untested with the selected Windows build |
| Video | H.264, HEVC, AV1, VP8/9, MPEG-2, MPEG-4 Part 2, VC-1, MJPEG, ProRes | Expected where compiled in; sample matrix required |
| Audio | MP3, AAC/M4A, FLAC, WAV, Vorbis, Opus, ALAC, WMA, AIFF, AC-3, E-AC-3, DTS | Expected where compiled in; not verified; passthrough requires suitable receiver/device |
| Subtitles | SRT, ASS/SSA, WebVTT, embedded, PGS, VobSub | Expected from libass/FFmpeg; font availability and graphical formats require samples |
| Streams | Direct HTTP(S), unencrypted HLS/DASH, radio, IPTV playlists, RTSP, RTMP, RTP, UDP, SRT | UI accepts selected protocols; controlled Windows/network tests outstanding |
| Files | Unicode, spaces, UNC, mapped/removable drives, extensionless files, long paths | Parser/storage tests cover Unicode; filesystem/platform checks outstanding |
| Picture | VFR, high bit depth, HDR, scaling, hardware decode | Hardware/build dependent; not qualified |
| Audio CD | Engine/build dependent `cdda` support | Not enabled or tested in UI |
| DVD/VIDEO_TS | Unencrypted titles, chapters | Backend investigation pending; **mpv has no DVD menu support** |
| Blu-ray/ISO | Unencrypted structures, engine libbluray support | Not enabled or tested; no menu claim |
| Websites | Optional yt-dlp | Not packaged or enabled; direct media URLs only |
| Protected media | Subscription DRM, encrypted commercial discs | Unsupported; no circumvention or cookie scraping |

`scripts/Generate-Fixtures.ps1` creates redistributable synthetic media with two audio tracks and SRT, and additional codec samples when an explicit ffmpeg executable is supplied. `--smoke-test` checks real decoding, duration, pause, seek, track selection and a screenshot on Windows; it does not establish physical audio output or hardware decoding. See testing.md before promoting any row to tested.
