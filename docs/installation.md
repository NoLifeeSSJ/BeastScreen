# Installation and maintenance

**No release has been published or qualified yet.** There is currently no working public release installation command. The script must report an unavailable release rather than fall back to building source or executing unverified downloaded text.

After a real release exists, download its `BeastScreen-Install.ps1` to disk over HTTPS, inspect it, and run it with Windows PowerShell 5.1 or PowerShell 7. The repository is `NoLifeeSSJ/BeastScreen`; asset names are `BeastScreen-v<version>-win-x64.zip` and `SHA256SUMS.txt` from the same resolved release. Native ARM64 and Windows 10 packages are not offered.

The following are maintenance syntax examples, **not evidence that an unpublished version can be installed**:

```powershell
# Run an inspected local installer, selecting a real version once published:
.\BeastScreen-Install.ps1 -Action Install -Version v0.1.0
.\BeastScreen-Install.ps1 -Action Install -InstallDirectory "$env:LOCALAPPDATA\Programs\BeastScreen" -DesktopShortcut -RegisterFileAssociations

# Once installed, use the maintenance copy (supply your custom directory if used):
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Launch
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Update
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Repair
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Rollback
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Uninstall
# Optional, explicit removal of %LOCALAPPDATA%\BeastScreen:
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Uninstall -RemoveAppData
```

Start Menu shortcuts launch the current version. Shortcut and optional Open With registrations are refreshed during upgrades. Default file associations remain the user's Windows choice. The application runs without administrator privileges; elevate maintenance only if the chosen install location requires it. The installer never changes execution policy or uses Invoke-Expression.

Default layout:

- `C:\BeastScreen\repo`: source, never an uninstall target.
- `C:\BeastScreen\app\versions\<version-id>`: isolated self-contained app versions; previous versions remain available.
- `C:\BeastScreen\app\BeastScreen-Install.ps1`: retained maintenance copy.
- `C:\BeastScreen\downloads`: isolated temporary download staging.
- `%LOCALAPPDATA%\BeastScreen`: SQLite, preferences, cache and future redacted logs; preserved by default.

The installer resolves one release before fetching assets, verifies its package checksum, bounds and confines extraction, runs a startup check and read-only database compatibility check, coordinates owned-player shutdown and atomically activates state. It takes an exclusive installation lock and refuses to claim unrelated nonempty directories. Removal follows recorded file ownership and leaves extra files intact. A checksum is not a publisher signature. A third-party source/licence audit and actual installer lifecycle checks are mandatory before publication.
