# Troubleshooting

- **Missing libmpv / native loader error:** rerun `scripts/Restore-Native.ps1` for development. Installed users should use Repair after a release exists. Both `libmpv-2.dll` and `vulkan-1.dll` must be under `native`; do not copy random DLLs into System32 or disable verification.
- **Video black, no compatible GPU:** set hardware decoding to `no`; D3D11 rendering still requires a supported driver. Virtual machines may need the explicit WARP smoke-test configuration. Inspect codec/decoder/dropped-frame diagnostics. Do not assume 4K/8K performance from successful decoding alone.
- **Stream failed:** confirm it is a direct media URL rather than a website page, check network access, and retry. DRM and encrypted discs are unsupported. Diagnostic exports redact URLs; avoid sharing playlists containing credentials.
- **Missing files:** reconnect the drive/share or use Relocate folder, then rescan. Old records are preserved. The scanner does not delete or repair media.
- **Preferences invalid:** malformed JSON is preserved as `settings.json.invalid-<timestamp>` and defaults load. Save valid settings from the app.
- **Library from newer app:** do not reset schema versions manually. Update the player or restore a compatible backup. Uninstall preserves the database unless `-RemoveAppData` is explicit.
- **Installer permission error:** choose a writable dedicated directory; only elevate the maintenance command when that location requires it. The player always runs as the user. No machine-wide execution policy changes are needed.
- **No public release:** the installer cannot download a build that has not been published. Build locally, finish the documented Windows/licensing gates, and only then publish.
