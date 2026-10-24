#requires -Version 5.1
[CmdletBinding()]
param([string]$Version = 'v0.1.0', [switch]$ForRelease, [switch]$Preview)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Use a version tag such as v0.1.0.' }
$audit = Get-Content (Join-Path $repo 'docs/release-audit.json') -Raw | ConvertFrom-Json
if($Preview -and $Version -notmatch '-preview\.\d+$'){throw 'Preview packaging requires an explicit -preview.N version.'}
if($ForRelease){
    if(-not $audit.nativeRedistributionApproved){throw 'Release blocked: the dependencies included in our ZIP need verified source/licensing.'}
    if($Preview){if(-not $audit.previewPublicationAuthorized){throw 'Preview publication has not been authorized.'}}
    elseif(-not $audit.windows11PlaybackAccepted -or -not $audit.cleanMachineInstallerAccepted){throw 'Stable release blocked: Windows 11 and clean-machine acceptance are required.'}
}
$artifacts = Join-Path $repo 'artifacts'; $publish = Join-Path $artifacts ('publish-' + $Version); $output = Join-Path $artifacts 'release'
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& dotnet publish (Join-Path $repo 'src/BeastScreen.Desktop/BeastScreen.Desktop.csproj') -c Release -r win-x64 --self-contained true --no-restore ('-p:Version=' + $Version.TrimStart('v')) -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Self-contained Windows publish failed.' }
foreach ($file in @('native/libmpv-2.dll','native/vulkan-1.dll','tools/ffmpeg.exe','tools/vulkan-1.dll')) { if (-not (Test-Path (Join-Path $publish $file))) { throw ('Missing packaged dependency: ' + $file) } }
# Codec builds are fetched directly by the installer. We do not redistribute
# prebuilt mpv/FFmpeg without their complete matching transitive source bundle.
Remove-Item -LiteralPath (Join-Path $publish 'native/libmpv-2.dll'),(Join-Path $publish 'tools/ffmpeg.exe') -Force
[IO.File]::WriteAllText((Join-Path $publish 'release-manifest.json'), (@{ product='BeastScreen'; version=$Version; architecture='x64'; minimumWindowsBuild=26100; databaseSchema=1; maximumReadableSchema=1; development=(-not $ForRelease.IsPresent); preview=$Preview.IsPresent; playbackRuntime='download-upstream' } | ConvertTo-Json))
Copy-Item (Join-Path $repo 'LICENSE') $publish
Copy-Item (Join-Path $repo 'LICENSES') $publish -Recurse
Copy-Item (Join-Path $repo 'THIRD_PARTY_NOTICES.md') $publish
Copy-Item (Join-Path $repo 'native-dependencies.json') $publish
$assets=Get-Content (Join-Path $repo 'src/BeastScreen.Desktop/obj/project.assets.json') -Raw|ConvertFrom-Json
$packageRoots=@($assets.packageFolders.PSObject.Properties.Name)
$noticeDirectory=Join-Path $publish 'LICENSES/package-notices'; New-Item -ItemType Directory -Path $noticeDirectory -Force|Out-Null
$noticePackages=@{'microsoft.netcore.app.runtime.win-x64'='10.0.13';'microsoft.windowsdesktop.app.runtime.win-x64'='10.0.13';'communitytoolkit.mvvm'='8.4.2'}
foreach($packageName in $noticePackages.Keys){
    $copied=0
    foreach($packageRoot in $packageRoots){
        $packageDirectory=Join-Path (Join-Path $packageRoot $packageName) $noticePackages[$packageName]
        if(Test-Path $packageDirectory){foreach($notice in Get-ChildItem $packageDirectory -Recurse -File|Where-Object{$_.Name -match '(?i)(license|notice|copying)'}){Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticeDirectory ($packageName + '-' + $notice.Name)) -Force; $copied++}}
    }
    if($copied -eq 0){throw ('Missing distribution notices for ' + $packageName)}
}
if(Test-Path (Join-Path $publish 'Microsoft.Windows.SDK.NET.dll')){throw 'Unused Windows SDK projections must not be included in the WPF-only release.'}
# SPDX dependency inventory: NuGet content hashes are SHA512 of package bytes.
$packages = New-Object 'Collections.Generic.List[object]'; $seen = New-Object 'Collections.Generic.HashSet[string]'
$packages.Add(@{ SPDXID='SPDXRef-BeastScreen'; name='BeastScreen'; versionInfo=$Version; downloadLocation='https://github.com/NoLifeeSSJ/BeastScreen'; filesAnalyzed=$false; licenseDeclared='GPL-3.0-or-later'; licenseConcluded='GPL-3.0-or-later'; checksums=@(@{ algorithm='SHA256'; checksumValue=(Get-FileHash (Join-Path $publish 'BeastScreen.exe') -Algorithm SHA256).Hash.ToLowerInvariant() }) })
$runtimeConfig=Get-Content (Join-Path $publish 'BeastScreen.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach($framework in $runtimeConfig.runtimeOptions.includedFrameworks){
    $packages.Add(@{ SPDXID=('SPDXRef-framework-' + $framework.name); name=$framework.name; versionInfo=$framework.version; downloadLocation='NOASSERTION'; filesAnalyzed=$false; licenseDeclared='MIT'; licenseConcluded='NOASSERTION' })
}
foreach ($lockFile in Get-ChildItem (Join-Path $repo 'src') -Filter 'packages.lock.json' -Recurse) {
    $lock = Get-Content $lockFile.FullName -Raw | ConvertFrom-Json
    foreach ($target in $lock.dependencies.PSObject.Properties) { foreach ($dependency in $target.Value.PSObject.Properties) {
        if ($dependency.Value.type -eq 'Project') { continue }
        $name=$dependency.Name; $versionNumber=$dependency.Value.resolved; $id=$name + '@' + $versionNumber
        if (-not $seen.Add($id)) { continue }
        $hex=[BitConverter]::ToString([Convert]::FromBase64String($dependency.Value.contentHash)).Replace('-','').ToLowerInvariant()
        $packages.Add(@{ SPDXID=('SPDXRef-' + ($id -replace '[^a-zA-Z0-9.-]','-')); name=$name; versionInfo=$versionNumber; downloadLocation=('https://api.nuget.org/v3-flatcontainer/' + $name.ToLowerInvariant() + '/' + $versionNumber + '/' + $name.ToLowerInvariant() + '.' + $versionNumber + '.nupkg'); filesAnalyzed=$false; licenseConcluded='NOASSERTION'; licenseDeclared='NOASSERTION'; checksums=@(@{ algorithm='SHA512'; checksumValue=$hex }) })
    } }
}
foreach ($native in Get-ChildItem (Join-Path $publish 'native') -Filter '*.dll') { $packages.Add(@{ SPDXID=('SPDXRef-native-' + $native.BaseName); name=$native.Name; downloadLocation='NOASSERTION'; filesAnalyzed=$false; licenseConcluded='NOASSERTION'; checksums=@(@{ algorithm='SHA256'; checksumValue=(Get-FileHash $native.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }) }) }
# Upstream codec archives are inventoried separately; they are absent from this ZIP.
Copy-Item (Join-Path $repo 'native-dependencies.json') (Join-Path $output 'upstream-playback-runtime.json') -Force
$relations=@(@{ spdxElementId='SPDXRef-DOCUMENT'; relationshipType='DESCRIBES'; relatedSpdxElement='SPDXRef-BeastScreen' })
foreach($package in $packages){if($package.SPDXID -ne 'SPDXRef-BeastScreen'){$relations+=@{ spdxElementId='SPDXRef-BeastScreen'; relationshipType='DEPENDS_ON'; relatedSpdxElement=$package.SPDXID }}}
$inventory=@{ spdxVersion='SPDX-2.3'; dataLicense='CC0-1.0'; SPDXID='SPDXRef-DOCUMENT'; name=('BeastScreen-' + $Version); documentNamespace=('https://github.com/NoLifeeSSJ/BeastScreen/sbom/' + [Guid]::NewGuid()); creationInfo=@{ created=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); creators=@('Tool: BeastScreen-Package.ps1') }; documentDescribes=@('SPDXRef-BeastScreen'); relationships=$relations; packages=@($packages.ToArray()) }
$sbom=Join-Path $output 'BeastScreen-sbom.spdx.json'; [IO.File]::WriteAllText($sbom, ($inventory | ConvertTo-Json -Depth 12)); Copy-Item $sbom $publish
if ($ForRelease) {
    $sources = Join-Path $artifacts 'audited-corresponding-source'
    if (-not (Test-Path (Join-Path $sources 'SOURCE-MANIFEST.sha256'))) { throw 'Matching, audited complete corresponding source and SOURCE-MANIFEST.sha256 are required before distribution.' }
    Compress-Archive -Path (Join-Path $sources '*') -DestinationPath (Join-Path $output ('BeastScreen-' + $Version + '-corresponding-source.zip')) -Force
    & git -C $repo archive --format=zip ('--output=' + (Join-Path $output ('BeastScreen-' + $Version + '-source.zip'))) HEAD
    if($LASTEXITCODE -ne 0){throw 'Application source archive failed.'}
}
$zip=Join-Path $output ('BeastScreen-' + $Version + '-win-x64.zip'); Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal -Force
Copy-Item (Join-Path $repo 'BeastScreen-Install.ps1') $output -Force
Copy-Item (Join-Path $repo 'docs/release-notes.md') (Join-Path $output 'release-notes.md') -Force
$checksums = @(Get-ChildItem $output -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $checksums, (New-Object Text.UTF8Encoding($false)))
Write-Output ('Package prepared at ' + $zip + $(if ($ForRelease) { '' } else { ' (development package)' }))
