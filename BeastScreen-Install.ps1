#requires -Version 5.1
<#!
BeastScreen maintenance installer. Download this file, inspect it, then run with -File.
Checksums establish download integrity, not publisher identity. No execution-policy changes.
!#>
[CmdletBinding()]
param(
    [ValidateSet('Install','Update','Repair','Rollback','Uninstall','Launch')][string]$Action = 'Install',
    [string]$Version = 'latest',
    [string]$InstallDirectory = 'C:\BeastScreen\app',
    [string]$DownloadDirectory = 'C:\BeastScreen\downloads',
    [switch]$DesktopShortcut,
    [switch]$RegisterFileAssociations,
    [switch]$RemoveAppData,
    [switch]$ForceClose
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Repository = 'NoLifeeSSJ/BeastScreen'
$script:Product = 'BeastScreen'
$script:InstallerSourcePath = $PSCommandPath

function Get-NormalizedPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}
function Test-Within([string]$Child, [string]$Parent) {
    $c = Get-NormalizedPath $Child; $p = Get-NormalizedPath $Parent
    return $c.StartsWith($p + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoReparsePoint([string]$Path) {
    $cursor = [IO.DirectoryInfo](Get-NormalizedPath $Path)
    while ($null -ne $cursor) {
        if ($cursor.Exists -and ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation paths may not traverse a junction or symbolic link.' }
        $cursor = $cursor.Parent
    }
}
function Assert-InstallPath([string]$Path) {
    $full = Get-NormalizedPath $Path
    if ($full -eq [IO.Path]::GetPathRoot($full).TrimEnd('\','/') -or $full.Length -lt 5) { throw 'Choose a dedicated application subdirectory, not a drive root.' }
    foreach ($protected in @('C:\BeastScreen\repo', $env:WINDIR, $env:USERPROFILE, $env:LOCALAPPDATA)) {
        if ([string]::IsNullOrEmpty($protected)) { continue }
        if ($full -eq (Get-NormalizedPath $protected) -or (Test-Within $protected $full) -or (($protected -eq 'C:\BeastScreen\repo' -or $protected -eq $env:WINDIR) -and (Test-Within $full $protected))) { throw 'The install directory conflicts with source, system or user-data locations.' }
    }
    Assert-NoReparsePoint $full
    return $full
}
function Read-InstallState([string]$Root) {
    $file = Join-Path $Root '.beastscreen-install.json'
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $null }
    $state = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($state.product -ne $script:Product -or $state.installerSchema -ne 1) { throw 'This directory is not owned by a compatible BeastScreen installer.' }
    foreach ($entry in @($state.versions)) {
        if ($entry.directory -notmatch '^versions[\\/]v[0-9A-Za-z.-]+$' -or -not (Test-Within (Join-Path $Root $entry.directory) (Join-Path $Root 'versions'))) { throw 'Invalid installed-version path in installer state.' }
    }
    return $state
}
function Write-InstallState([string]$Root, $State) {
    $file = Join-Path $Root '.beastscreen-install.json'; $temp = $file + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temp, ($State | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $file) { [IO.File]::Replace($temp, $file, ($file + '.backup'), $true) }
        else { [IO.File]::Move($temp, $file) }
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
}
function Get-Release([string]$RequestedVersion) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $endpoint = 'https://api.github.com/repos/' + $script:Repository + '/releases/'
    if ($RequestedVersion -eq 'latest') { $endpoint += 'latest' }
    else { if ($RequestedVersion -notmatch '^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Specify a release version such as v0.1.0.' }; $endpoint += 'tags/' + $(if ($RequestedVersion.StartsWith('v')) { $RequestedVersion } else { 'v' + $RequestedVersion }) }
    try { $release = Invoke-RestMethod -Uri $endpoint -Headers @{ 'User-Agent'='BeastScreen-Installer'; 'Accept'='application/vnd.github+json' } -TimeoutSec 30 }
    catch { throw 'The requested public GitHub Release is unavailable. Check the version and network connection. No application files were changed.' }
    if ($release.draft -or $release.tag_name -notmatch '^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'The release does not have a supported immutable version tag.' }
    return $release
}
function Get-Asset($Release, [string]$Name) {
    $assets = @($Release.assets | Where-Object { $_.name -ceq $Name })
    if ($assets.Count -ne 1) { throw ('Release ' + $Release.tag_name + ' must contain exactly one ' + $Name + ' asset.') }
    $url = [Uri]$assets[0].browser_download_url
    $prefix = 'https://github.com/' + $script:Repository + '/releases/download/' + $Release.tag_name + '/'
    if ($url.Scheme -ne 'https' -or -not $url.AbsoluteUri.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Unexpected release asset origin.' }
    return $assets[0]
}
function Receive-Asset($Asset, [string]$Path) {
    if ([long]$Asset.size -le 0 -or [long]$Asset.size -gt 2147483648) { throw 'The release asset has an unsafe or empty size.' }
    $partial = $Path + '.partial'
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Asset.browser_download_url -OutFile $partial -TimeoutSec 300 -Headers @{ 'User-Agent'='BeastScreen-Installer' }
        if ((Get-Item -LiteralPath $partial).Length -ne [long]$Asset.size) { throw 'The download was interrupted or its size changed.' }
        Move-Item -LiteralPath $partial -Destination $Path -Force
    } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }
}
function Assert-PackageChecksum([string]$Package, [string]$Manifest, [string]$AssetName) {
    if ((Get-Item -LiteralPath $Manifest).Length -gt 1048576) { throw 'The checksum manifest is too large.' }
    $matchesFound = @()
    foreach ($line in [IO.File]::ReadAllLines($Manifest)) {
        if ($line -match '^([A-Fa-f0-9]{64}) [ *]([^\\/]+)$' -and $Matches[2] -ceq $AssetName) { $matchesFound += $Matches[1].ToLowerInvariant() }
    }
    if ($matchesFound.Count -ne 1) { throw 'The checksum manifest must contain exactly one matching asset entry.' }
    if ((Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant() -cne $matchesFound[0]) { throw 'SHA256 verification failed. Nothing was installed.' }
}
function Expand-SafePackage([string]$Archive, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    Assert-NoReparsePoint $Destination
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    $files = New-Object 'Collections.Generic.List[string]'; $names = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    try {
        if ($zip.Entries.Count -gt 25000) { throw 'The package contains too many entries.' }
        [long]$total = 0
        foreach ($entry in $zip.Entries) {
            $total += $entry.Length
            if ($total -gt 2147483648 -or $entry.FullName.StartsWith('/') -or $entry.FullName.StartsWith('\') -or $entry.FullName.Contains(':') -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'The package exceeds extraction limits or contains an unsafe entry.' }
            foreach ($segment in $entry.FullName.TrimEnd('/','\').Split([char[]]@('/','\'))) {
                if ($segment -eq '..' -or $segment -eq '.' -or $segment -match '[<>:"|?*\x00-\x1F]' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or $segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw 'The package contains an unsafe Windows filename.' }
            }
            $relative = $entry.FullName.Replace('\', [IO.Path]::DirectorySeparatorChar).Replace('/', [IO.Path]::DirectorySeparatorChar)
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
            if (-not (Test-Within $target $Destination) -or -not $names.Add($target)) { throw 'The package contains a duplicate or escaping path.' }
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { [IO.Directory]::CreateDirectory($target) | Out-Null; continue }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false); $files.Add($relative)
        }
    } finally { $zip.Dispose() }
    return ,$files.ToArray()
}
function Invoke-StartupCheck([string]$Executable, [string]$Argument = '--health-check') {
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'The package has no BeastScreen.exe.' }
    $process = Start-Process -FilePath $Executable -ArgumentList $Argument -WorkingDirectory ([IO.Path]::GetDirectoryName($Executable)) -PassThru
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'The application startup check timed out; the previous version is still active.' }
    if ($process.ExitCode -ne 0) { throw ('Application startup check failed (exit ' + $process.ExitCode + '). The previous version is still active. Check native dependencies or library schema compatibility.') }
}
function Stop-OwnedPlayer([string]$Root) {
    foreach ($process in @(Get-Process -Name BeastScreen -ErrorAction SilentlyContinue)) {
        try { $path = $process.Path } catch { continue }
        if ([string]::IsNullOrEmpty($path) -or -not (Test-Within $path (Join-Path $Root 'versions'))) { continue }
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(10000)) { if ($ForceClose) { $process.Kill(); $process.WaitForExit() } else { throw 'BeastScreen is still running. Close this installation and retry, or explicitly use -ForceClose.' } }
    }
}
function Set-Registrations([string]$Root, $State, [bool]$Desktop, [bool]$Associations) {
    $current = @($State.versions | Where-Object { $_.directory -eq $State.current })[0]
    $executable = Join-Path (Join-Path $Root $current.directory) 'BeastScreen.exe'
    $shell = New-Object -ComObject WScript.Shell
    $start = Join-Path ([Environment]::GetFolderPath('Programs')) 'BeastScreen.lnk'
    $shortcut = $shell.CreateShortcut($start); $shortcut.TargetPath = $executable; $shortcut.WorkingDirectory = [IO.Path]::GetDirectoryName($executable); $shortcut.Description = 'BeastScreen media player'; $shortcut.Save()
    if ($Desktop) { $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'BeastScreen.lnk')); $link.TargetPath = $executable; $link.WorkingDirectory = [IO.Path]::GetDirectoryName($executable); $link.Save() }
    if ($Associations) {
        $key = 'HKCU:\Software\Classes\BeastScreen.Media'
        New-Item -Path ($key + '\shell\open\command') -Force | Out-Null
        Set-Item -Path $key -Value 'BeastScreen media'
        Set-Item -Path ($key + '\shell\open\command') -Value ('"' + $executable + '" "%1"')
        foreach ($extension in @('.mp4','.mkv','.avi','.mov','.webm','.mp3','.flac','.wav','.m3u','.m3u8','.pls')) { $openWith = 'HKCU:\Software\Classes\' + $extension + '\OpenWithProgids'; New-Item -Path $openWith -Force | Out-Null; New-ItemProperty -Path $openWith -Name 'BeastScreen.Media' -PropertyType String -Value '' -Force | Out-Null }
    }
}
function Remove-Registrations([string]$Root) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in @((Join-Path ([Environment]::GetFolderPath('Programs')) 'BeastScreen.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'BeastScreen.lnk'))) {
        if ((Test-Path -LiteralPath $path) -and (Test-Within $shell.CreateShortcut($path).TargetPath $Root)) { Remove-Item -LiteralPath $path -Force }
    }
    $key = 'HKCU:\Software\Classes\BeastScreen.Media'; $command = $key + '\shell\open\command'
    if ((Test-Path $command) -and (Get-Item $command).GetValue('').StartsWith(('"' + $Root + '\'), [StringComparison]::OrdinalIgnoreCase)) {
        foreach ($extension in @('.mp4','.mkv','.avi','.mov','.webm','.mp3','.flac','.wav','.m3u','.m3u8','.pls')) { Remove-ItemProperty -Path ('HKCU:\Software\Classes\' + $extension + '\OpenWithProgids') -Name 'BeastScreen.Media' -ErrorAction SilentlyContinue }
        Remove-Item -Path $key -Recurse -Force
    }
}
function Remove-OwnedVersion([string]$Root, $Entry) {
    $directory = Join-Path $Root $Entry.directory
    if (-not (Test-Within $directory (Join-Path $Root 'versions'))) { throw 'Refusing to remove a version outside this installation.' }
    Assert-NoReparsePoint $directory
    foreach ($relative in @($Entry.files)) {
        $target = [IO.Path]::GetFullPath((Join-Path $directory $relative))
        if (-not (Test-Within $target $directory)) { throw 'Unsafe file in installer ownership record.' }
        Assert-NoReparsePoint ([IO.Path]::GetDirectoryName($target))
        if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
    }
    # Remove empty directories only. Any files added by the user are preserved.
    if (Test-Path -LiteralPath $directory) {
        $stack = New-Object 'Collections.Generic.Stack[string]'; $stack.Push($directory); $all = New-Object 'Collections.Generic.List[string]'
        while ($stack.Count -gt 0) { foreach ($child in @(Get-ChildItem -LiteralPath $stack.Pop() -Directory -Force)) { if (-not ($child.Attributes -band [IO.FileAttributes]::ReparsePoint)) { $all.Add($child.FullName); $stack.Push($child.FullName) } } }
        $dirs = @($all.ToArray() | Sort-Object { $_.Length } -Descending)
        foreach ($dir in $dirs) { if (@(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) { [IO.Directory]::Delete($dir) } }
        if (@(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { [IO.Directory]::Delete($directory) }
    }
}
function Invoke-BeastScreenInstaller {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'BeastScreen requires native Windows 11 x64.' }
    $architecture = $env:PROCESSOR_ARCHITEW6432; if ([string]::IsNullOrEmpty($architecture)) { $architecture = $env:PROCESSOR_ARCHITECTURE }
    if ($architecture -ne 'AMD64' -or [Environment]::OSVersion.Version.Build -lt 26100) { throw 'This build targets Windows 11 24H2 or newer, x64. Windows 10 and native ARM64 have not been qualified.' }
    $root = Assert-InstallPath $InstallDirectory
    $parent = [IO.Path]::GetDirectoryName($root); [IO.Directory]::CreateDirectory($parent) | Out-Null
    $hash = [Security.Cryptography.SHA256]::Create(); try { $id = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($root.ToLowerInvariant()))).Replace('-','').Substring(0,20) } finally { $hash.Dispose() }
    $lock = $null; $stage = $null
    try {
        try { $lock = [IO.File]::Open((Join-Path $parent ('.beastscreen-' + $id + '.lock')), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
        catch { throw 'Another operation is using this installation, or this location is not writable. Choose a writable custom directory or elevate only this maintenance operation if needed.' }
        $state = Read-InstallState $root
        if ($null -eq $state -and (Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count -gt 0) { throw 'Refusing to claim a nonempty directory without a BeastScreen ownership record.' }
        if ($Action -eq 'Launch') { if ($null -eq $state) { throw 'BeastScreen is not installed here.' }; Start-Process -FilePath (Join-Path (Join-Path $root $state.current) 'BeastScreen.exe'); return }
        if ($Action -eq 'Uninstall') {
            if ($null -eq $state) { Write-Output 'No owned BeastScreen installation found.'; return }
            Stop-OwnedPlayer $root; Remove-Registrations $root
            foreach ($entry in @($state.versions)) { Remove-OwnedVersion $root $entry }
            foreach ($name in @('.beastscreen-install.json','.beastscreen-install.json.backup','BeastScreen-Install.ps1')) { $file = Join-Path $root $name; if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force } }
            if ($RemoveAppData) { $data = Join-Path $env:LOCALAPPDATA 'BeastScreen'; Assert-NoReparsePoint $data; if (Test-Path -LiteralPath $data) { Get-ChildItem -LiteralPath $data -Recurse -Force | ForEach-Object { if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'App data contains links. Remove these manually before deleting app data.' } }; Remove-Item -LiteralPath $data -Recurse -Force } }
            Write-Output 'BeastScreen uninstalled. Source repositories, media and unowned files were preserved. App data was kept unless -RemoveAppData was supplied.'; return
        }
        if ($Action -eq 'Rollback') {
            if ($null -eq $state -or [string]::IsNullOrEmpty($state.previous)) { throw 'No previous working version is available.' }
            $exe = Join-Path (Join-Path $root $state.previous) 'BeastScreen.exe'; Stop-OwnedPlayer $root; Invoke-StartupCheck $exe '--check-data'; Invoke-StartupCheck $exe
            $old = $state.current; $state.current = $state.previous; $state.previous = $old; Write-InstallState $root $state; Set-Registrations $root $state $state.desktop $state.associations; Write-Output 'Rollback completed. Library schema compatibility was checked.'; return
        }
        $requested = $Version
        if ($Action -eq 'Repair' -and $requested -eq 'latest' -and $null -ne $state) { $requested = @($state.versions | Where-Object { $_.directory -eq $state.current })[0].version }
        $release = Get-Release $requested; $assetName = 'BeastScreen-' + $release.tag_name + '-win-x64.zip'
        if ($Action -ne 'Repair' -and $null -ne $state -and -not [string]::IsNullOrEmpty($state.current)) {
            $installed = @($state.versions | Where-Object { $_.directory -eq $state.current })[0]
            if ($installed.version -eq $release.tag_name) {
                Invoke-StartupCheck (Join-Path (Join-Path $root $state.current) 'BeastScreen.exe')
                $state.desktop = $state.desktop -or $DesktopShortcut.IsPresent; $state.associations = $state.associations -or $RegisterFileAssociations.IsPresent
                Write-InstallState $root $state; Set-Registrations $root $state $state.desktop $state.associations
                Write-Output ('Already installed and healthy: ' + $release.tag_name + '. Use Repair to replace application files.'); return
            }
        }
        $asset = Get-Asset $release $assetName; $checksums = Get-Asset $release 'SHA256SUMS.txt'
        [IO.Directory]::CreateDirectory($DownloadDirectory) | Out-Null; Assert-NoReparsePoint $DownloadDirectory
        $stage = Join-Path $DownloadDirectory ('beastscreen-' + [Guid]::NewGuid().ToString('N')); [IO.Directory]::CreateDirectory($stage) | Out-Null
        $package = Join-Path $stage $assetName; $manifest = Join-Path $stage 'SHA256SUMS.txt'
        Receive-Asset $checksums $manifest; Receive-Asset $asset $package; Assert-PackageChecksum $package $manifest $assetName
        $unpacked = Join-Path $stage 'unpacked'; $files = Expand-SafePackage $package $unpacked
        $releaseManifest = Join-Path $unpacked 'release-manifest.json'
        if (-not (Test-Path -LiteralPath $releaseManifest)) { throw 'The package is missing its application manifest.' }
        $info = Get-Content -LiteralPath $releaseManifest -Raw | ConvertFrom-Json
        if ($info.product -ne $script:Product -or $info.version -ne $release.tag_name -or $info.architecture -ne 'x64' -or $info.databaseSchema -ne 1) { throw 'Package identity, architecture or schema does not match this installer.' }
        [IO.Directory]::CreateDirectory((Join-Path $root 'versions')) | Out-Null
        $relative = 'versions\' + $release.tag_name + '-' + [Guid]::NewGuid().ToString('N')
        $candidate = Join-Path $root $relative
        # Copy to the destination volume before startup checks; staging can be on another drive.
        Copy-Item -LiteralPath $unpacked -Destination $candidate -Recurse
        $entry = [pscustomobject]@{ directory=$relative; version=$release.tag_name; files=@($files) }
        try { Invoke-StartupCheck (Join-Path $candidate 'BeastScreen.exe'); Stop-OwnedPlayer $root; Invoke-StartupCheck (Join-Path $candidate 'BeastScreen.exe') '--check-data' }
        catch { Remove-OwnedVersion $root $entry; throw }
        if ($null -eq $state) { $state = [pscustomobject]@{ product=$script:Product; installerSchema=1; current=''; previous=''; desktop=$false; associations=$false; versions=@() } }
        $state.previous = $state.current; $state.current = $relative; $state.desktop = $state.desktop -or $DesktopShortcut.IsPresent; $state.associations = $state.associations -or $RegisterFileAssociations.IsPresent; $state.versions = @($state.versions) + @($entry)
        Write-InstallState $root $state
        $maintenance = Join-Path $root 'BeastScreen-Install.ps1'
        if ((Get-NormalizedPath $script:InstallerSourcePath) -ne (Get-NormalizedPath $maintenance)) { Copy-Item -LiteralPath $script:InstallerSourcePath -Destination $maintenance -Force }
        Set-Registrations $root $state $state.desktop $state.associations
        Write-Output ('Installed ' + $release.tag_name + '. Launch BeastScreen from the Start Menu. Maintenance script: ' + $maintenance)
    } finally {
        if ($null -ne $stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force }
        if ($null -ne $lock) { $lock.Dispose() }
    }
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-BeastScreenInstaller }
