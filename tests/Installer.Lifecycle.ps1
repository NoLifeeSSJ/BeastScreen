#requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$PublishedApplication)
# Real packaged executable + native health checks; controlled release transport.
# Run in a disposable Windows test user: registration helpers are redirected to a fixture.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'BeastScreen-Install.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('BeastScreen-lifecycle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $fixtureRoot | Out-Null
$InstallDirectory = Join-Path $fixtureRoot 'app'; $DownloadDirectory = Join-Path $fixtureRoot 'downloads'
$script:FixtureAssets = @{}; $script:FixtureLatest = 'v0.1.0'; $script:CorruptFixture = $false; $script:HealthFailure = $false
function New-FixtureRelease([string]$Tag) {
    $content=Join-Path $fixtureRoot ('content-' + $Tag); Copy-Item -LiteralPath $PublishedApplication -Destination $content -Recurse
    $info=Get-Content (Join-Path $content 'release-manifest.json') -Raw | ConvertFrom-Json; $info.version=$Tag; [IO.File]::WriteAllText((Join-Path $content 'release-manifest.json'), ($info | ConvertTo-Json))
    $assets=Join-Path $fixtureRoot $Tag; New-Item -ItemType Directory $assets | Out-Null
    $name='BeastScreen-' + $Tag + '-win-x64.zip'; $zip=Join-Path $assets $name; Compress-Archive -Path (Join-Path $content '*') -DestinationPath $zip
    [IO.File]::WriteAllText((Join-Path $assets 'SHA256SUMS.txt'), ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name))
    $script:FixtureAssets[$Tag]=$assets
}
function Get-Release([string]$RequestedVersion) {
    $tag=if($RequestedVersion -eq 'latest'){$script:FixtureLatest}else{$RequestedVersion}
    if(-not $script:FixtureAssets.ContainsKey($tag)){throw 'Unavailable fixture release'}
    $assets=@(Get-ChildItem $script:FixtureAssets[$tag] -File | ForEach-Object { [pscustomobject]@{name=$_.Name; size=$_.Length; browser_download_url=('https://github.com/' + $script:Repository + '/releases/download/' + $tag + '/' + $_.Name)} })
    return [pscustomobject]@{tag_name=$tag; draft=$false; assets=$assets}
}
function Receive-Asset($Asset,[string]$Path) {
    $uri=[Uri]$Asset.browser_download_url; $parts=$uri.AbsolutePath.Split('/'); $tag=$parts[$parts.Length-2]
    Copy-Item -LiteralPath (Join-Path $script:FixtureAssets[$tag] $Asset.name) -Destination $Path
    if($script:CorruptFixture -and $Asset.name.EndsWith('.zip')){[IO.File]::AppendAllText($Path,'corrupt')}
}
function Set-Registrations([string]$Root,$State,[bool]$Desktop,[bool]$Associations) { [IO.File]::WriteAllText((Join-Path $fixtureRoot 'registration-observation.txt'),$State.current) }
function Remove-Registrations([string]$Root) { }
$script:Assertions=0
function Assert-Lifecycle([bool]$Condition,[string]$Message) { if(-not $Condition){throw $Message}; $script:Assertions++ }
try {
    New-FixtureRelease 'v0.1.0'; New-FixtureRelease 'v0.2.0'
    $Action='Install'; Invoke-BeastScreenInstaller; $first=(Read-InstallState $InstallDirectory).current
    Assert-Lifecycle (Test-Path (Join-Path (Join-Path $InstallDirectory $first) 'BeastScreen.exe')) 'Fresh install failed'
    Assert-Lifecycle (Test-Path (Join-Path (Join-Path $InstallDirectory $first) 'native/libmpv-2.dll')) 'Upstream libmpv was not installed'
    Assert-Lifecycle (Test-Path (Join-Path (Join-Path $InstallDirectory $first) 'tools/ffmpeg.exe')) 'Upstream FFmpeg was not installed'
    Invoke-BeastScreenInstaller; Assert-Lifecycle ((Read-InstallState $InstallDirectory).versions.Count -eq 1) 'Repeated install created another version'
    $script:FixtureLatest='v0.2.0'; $Action='Update'; Invoke-BeastScreenInstaller; $updated=Read-InstallState $InstallDirectory
    Assert-Lifecycle ($updated.previous -eq $first -and $updated.current -ne $first) 'Update did not preserve previous version'
    $Action='Rollback'; Invoke-BeastScreenInstaller; Assert-Lifecycle ((Read-InstallState $InstallDirectory).current -eq $first) 'Rollback failed'
    $Action='Repair'; Invoke-BeastScreenInstaller; $repaired=(Read-InstallState $InstallDirectory).current; Assert-Lifecycle ($repaired -ne $first) 'Repair did not replace files'
    $script:CorruptFixture=$true; $Action='Update'; $failed=$false; try { Invoke-BeastScreenInstaller } catch {$failed=$true}; Assert-Lifecycle $failed 'Corrupt update was accepted'; Assert-Lifecycle ((Read-InstallState $InstallDirectory).current -eq $repaired) 'Corrupt update changed active version'; $script:CorruptFixture=$false
    $current=Join-Path $InstallDirectory $repaired; [IO.File]::WriteAllText((Join-Path $current 'unowned.txt'),'preserve')
    $Action='Uninstall'; Invoke-BeastScreenInstaller; Assert-Lifecycle (Test-Path (Join-Path $current 'unowned.txt')) 'Uninstall deleted unowned files'; Assert-Lifecycle (-not (Test-Path (Join-Path $current 'BeastScreen.exe'))) 'Uninstall left owned executable'
    Assert-Lifecycle (-not (Test-Path (Join-Path $current 'native/libmpv-2.dll'))) 'Uninstall left the downloaded owned runtime'
    Assert-Lifecycle (-not (Test-Path (Join-Path $current 'tools/ffmpeg.exe'))) 'Uninstall left the downloaded owned helper'
    $reports=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/test-results'; New-Item -ItemType Directory -Path $reports -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $reports 'installer-lifecycle.json'), (@{ passed=$script:Assertions; failed=0; environment=[Environment]::OSVersion.VersionString; limitations='Controlled release transport and registration fixture. Real self-contained executable/native startup. Not clean Windows 11 or shortcut/association qualification.' } | ConvertTo-Json))
    Write-Output ('Passed ' + $script:Assertions + ' controlled Windows installer lifecycle assertions. Shortcut/association and pristine-OS acceptance remain separate.')
} finally { if(Test-Path $fixtureRoot){Remove-Item -LiteralPath $fixtureRoot -Recurse -Force} }
