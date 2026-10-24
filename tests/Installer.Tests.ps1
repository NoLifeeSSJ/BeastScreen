#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'BeastScreen-Install.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$script:Passed = 0
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; $script:Passed++ }
function Assert-Throws([scriptblock]$Code, [string]$Message) { $threw = $false; try { & $Code | Out-Null } catch { $threw = $true }; Assert-True $threw $Message }
function New-TestZip([string]$Path, [string[]]$Names) {
    $stream = [IO.File]::Create($Path); $zip=$null
    try { $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create); foreach ($name in $Names) { $entry=$zip.CreateEntry($name); $writer=New-Object IO.StreamWriter($entry.Open()); try { $writer.Write('test content') } finally { $writer.Dispose() } } } finally { if($null -ne $zip){$zip.Dispose()}; $stream.Dispose() }
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('BeastScreen-installer-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temp | Out-Null
try {
    Assert-True (Test-Within (Join-Path $temp 'child') $temp) 'Child path should be contained.'
    Assert-True (-not (Test-Within ($temp + '-outside') $temp)) 'Sibling prefix must not count as contained.'
    Assert-True (-not (Test-Within (Join-Path $temp '../escape') $temp)) 'Parent traversal must not count as contained.'
    $package=Join-Path $temp 'package.zip'; $manifest=Join-Path $temp 'SHA256SUMS.txt'
    New-TestZip $package @('BeastScreen.exe','native/libmpv-2.dll')
    $hash=(Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($manifest, ($hash + '  package.zip' + [Environment]::NewLine))
    Assert-PackageChecksum $package $manifest 'package.zip'; $script:Passed++
    [IO.File]::AppendAllText($package, 'corruption'); Assert-Throws { Assert-PackageChecksum $package $manifest 'package.zip' } 'Corrupt checksum was accepted.'
    [IO.File]::WriteAllText($manifest, ($hash + '  package.zip' + [Environment]::NewLine + $hash + '  package.zip'))
    Assert-Throws { Assert-PackageChecksum $package $manifest 'package.zip' } 'Duplicate checksum entries were accepted.'
    $safe=Join-Path $temp 'safe.zip'; New-TestZip $safe @('BeastScreen.exe','native/libmpv-2.dll'); $destination=Join-Path $temp 'safe'; $files=Expand-SafePackage $safe $destination
    Assert-True ($files.Count -eq 2 -and (Test-Path (Join-Path $destination 'native/libmpv-2.dll'))) 'Valid package failed to extract.'
    foreach ($entry in @('../escaped.txt','..\escaped.txt','/absolute.txt','C:\escaped.txt','file:stream')) {
        $bad=Join-Path $temp ('bad-' + [Guid]::NewGuid().ToString('N') + '.zip'); New-TestZip $bad @($entry)
        Assert-Throws { Expand-SafePackage $bad (Join-Path $temp ([Guid]::NewGuid().ToString('N'))) } ('Unsafe ZIP entry accepted: ' + $entry)
    }
    $duplicate=Join-Path $temp 'duplicate.zip'; New-TestZip $duplicate @('app.dll','APP.dll'); Assert-Throws { Expand-SafePackage $duplicate (Join-Path $temp 'duplicate') } 'Case-insensitive duplicate entries were accepted.'
    Assert-True (-not (Test-Path (Join-Path (Split-Path $temp -Parent) 'escaped.txt'))) 'ZIP extraction wrote outside staging.'
    $root=Join-Path $temp 'app'; New-Item -ItemType Directory $root | Out-Null
    $state=[pscustomobject]@{ product='BeastScreen'; installerSchema=1; current='versions/v0.1.0'; previous=''; desktop=$false; associations=$false; versions=@([pscustomobject]@{ directory='versions/v0.1.0'; version='v0.1.0'; files=@('BeastScreen.exe') }) }
    Write-InstallState $root $state; Assert-True ((Read-InstallState $root).current -eq 'versions/v0.1.0') 'State did not persist.'
    $state.previous='versions/v0.0.1'; Write-InstallState $root $state; Assert-True (Test-Path (Join-Path $root '.beastscreen-install.json.backup')) 'Atomic state replacement did not keep a backup.'
    $version=Join-Path $root 'versions/v0.1.0'; New-Item -ItemType Directory $version -Force | Out-Null; [IO.File]::WriteAllText((Join-Path $version 'BeastScreen.exe'),'owned'); [IO.File]::WriteAllText((Join-Path $version 'personal-media.txt'),'not owned')
    Remove-OwnedVersion $root $state.versions[0]; Assert-True (-not (Test-Path (Join-Path $version 'BeastScreen.exe'))) 'Owned application file was not removed.'; Assert-True (Test-Path (Join-Path $version 'personal-media.txt')) 'Uninstall deleted an unowned file.'
    [IO.File]::WriteAllText((Join-Path $version 'BeastScreen.exe'),'partial'); Write-InstallJournal $root $state.versions[0]; Restore-PendingOperation $root $null
    Assert-True (-not (Test-Path (Join-Path $version 'BeastScreen.exe'))) 'Interrupted-install recovery left a partial owned file.'
    Assert-True (Test-Path (Join-Path $version 'personal-media.txt')) 'Interrupted-install recovery removed an unowned file.'
    [IO.File]::WriteAllText((Join-Path $version 'BeastScreen.exe'),'active'); Write-InstallJournal $root $state.versions[0]; Restore-PendingOperation $root $state
    Assert-True (Test-Path (Join-Path $version 'BeastScreen.exe')) 'Recovery deleted a version whose activation was already committed.'
    $state.versions[0].directory='../repo'; Write-InstallState $root $state; Assert-Throws { Read-InstallState $root } 'Escaping installed-version state was accepted.'
    Write-Output ('Passed ' + $script:Passed + ' installer helper assertions. These do not substitute for the Windows lifecycle test.')
} catch { Write-Output ('::error::' + $_.Exception.ToString().Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')); throw }
finally { Remove-Item -LiteralPath $temp -Recurse -Force }
