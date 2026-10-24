# Installation and maintenance

The first public preview is **v0.1.0-preview.1**, for Windows 11 x64 24H2/build 26100 or newer. It is unsigned and has not completed full pristine-machine/hardware qualification. Confirm the release and assets at https://github.com/NoLifeeSSJ/BeastScreen/releases/tag/v0.1.0-preview.1.

Run this in PowerShell. It downloads the versioned installer to disk, then runs it; ExecutionPolicy Bypass applies only to that child process.

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $installer = Join-Path $env:TEMP ('BeastScreen-Install-' + [guid]::NewGuid().ToString('N') + '.ps1')
    Invoke-WebRequest -UseBasicParsing 'https://github.com/NoLifeeSSJ/BeastScreen/releases/download/v0.1.0-preview.1/BeastScreen-Install.ps1' -OutFile $installer
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Action Install -Version v0.1.0-preview.1
}
```

The application includes .NET. Installation needs internet access to GitHub and downloads checksum-pinned mpv/FFmpeg archives directly from their upstream distributor; they are not bundled in our app ZIP. Windows built-in tar.exe extracts them. No SDK, Visual Studio, Git or separately installed 7-Zip is needed. Local playback works offline after installation. Preview installation must name the explicit version because GitHub's latest endpoint excludes prereleases. Native ARM64 and Windows 10 packages are not offered.

Maintenance commands (supply your custom install directory if used):

```powershell
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Launch
& C:\BeastScreen\app\BeastScreen-Install.ps1 -Action Update -Version v0.1.0-preview.1
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

The installer resolves one release before fetching assets, verifies its package checksum, bounds and confines extraction, runs a startup check and read-only database compatibility check, coordinates owned-player shutdown and atomically activates state. It takes an exclusive installation lock and refuses to claim unrelated nonempty directories. Removal follows recorded file ownership and leaves extra files intact. A checksum is not a publisher signature. The ZIP includes the notices for its redistributed dependencies; matching application and Vulkan source archives accompany the release. Full Windows 11 clean-machine acceptance remains a stable-release requirement.
