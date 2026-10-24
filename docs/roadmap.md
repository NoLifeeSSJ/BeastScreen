# Milestones and release gates

Statuses describe evidence, not just source-code presence.

1. **Repository and playback prototype — code cross-builds; Windows verification pending.** WPF HWND host, libmpv lifecycle/events, D3D11 configuration, rendering ADR and smoke runner exist. Native Vulkan dependency preparation is required. Physical audio, input focus, subtitles and GPU/software fallback must be observed on Windows before this milestone is accepted.
2. **Playback controls — implemented, Windows acceptance pending.** Open/seek/queue/tracks/speed/chapters/loop/subtitle/picture/audio/diagnostics are wired. No placeholder buttons are presented as a delivered feature. Audio device selection currently uses an engine device list plus its exact name rather than a friendly device picker.
3. **Library/playlists/preferences — core tested; remaining product work.** SQLite, resume, favourites, history, bookmarks, backup/restore, relocation, search/pagination, selected-folder scans and playlist import/export exist. Embedded metadata, thumbnails/artwork and rich artist/album grouping are outstanding.
4. **Streaming/compatibility — basic direct-source implementation only.** Bounded reconnection, controlled fixtures and codec matrix must pass. yt-dlp, disc title UI and disc backend investigation remain unaccepted.
5. **Windows interface/integration — shell implemented; acceptance pending.** Dark accessible chrome, list/grid, native airspace approach, shortcuts, single instance and media-key messages exist. Windows system media transport controls, high-DPI/multi-monitor, keyboard/screen-reader acceptance remain outstanding.
6. **Installer/release — code and CI provided; not published.** Native redistribution audit, full corresponding source, Windows clean-machine lifecycle, integrity failures, interrupted updates, signing if credentials exist, and real GitHub upload/runner results are release gates. Do not create a release until `docs/release-audit.json` accurately records these checks and linked evidence.

No Windows 10 or native ARM64 release until the entire runtime/native chain is tested. Maintain the playback abstraction; do not add libVLC solely to imply DVD menu support.
