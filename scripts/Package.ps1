#requires -Version 5.1
[CmdletBinding()]
param([string]$Version = 'v0.1.0', [switch]$ForRelease)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Use a version tag such as v0.1.0.' }
$audit = Get-Content (Join-Path $repo 'docs/release-audit.json') -Raw | ConvertFrom-Json
if ($ForRelease -and (-not $audit.nativeRedistributionApproved -or -not $audit.windows11PlaybackAccepted -or -not $audit.cleanMachineInstallerAccepted -or @($audit.evidence).Count -lt 3)) { throw 'Release blocked: complete the native source/license audit, Windows playback acceptance and clean-machine installer evidence in docs/release-audit.json.' }
$artifacts = Join-Path $repo 'artifacts'; $publish = Join-Path $artifacts ('publish-' + $Version); $output = Join-Path $artifacts 'release'
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& dotnet publish (Join-Path $repo 'src/BeastScreen.Desktop/BeastScreen.Desktop.csproj') -c Release -r win-x64 --self-contained true --no-restore ('-p:Version=' + $Version.TrimStart('v')) -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Self-contained Windows publish failed.' }
foreach ($file in @('native/libmpv-2.dll','native/vulkan-1.dll','tools/ffmpeg.exe')) { if (-not (Test-Path (Join-Path $publish $file))) { throw ('Missing packaged dependency: ' + $file) } }
[IO.File]::WriteAllText((Join-Path $publish 'release-manifest.json'), (@{ product='BeastScreen'; version=$Version; architecture='x64'; minimumWindowsBuild=26100; databaseSchema=1; maximumReadableSchema=1; development=(-not $ForRelease.IsPresent) } | ConvertTo-Json))
Copy-Item (Join-Path $repo 'LICENSE') $publish
Copy-Item (Join-Path $repo 'LICENSES') $publish -Recurse
Copy-Item (Join-Path $repo 'THIRD_PARTY_NOTICES.md') $publish
Copy-Item (Join-Path $repo 'native-dependencies.json') $publish
# SPDX dependency inventory: NuGet content hashes are SHA512 of package bytes.
$packages = New-Object 'Collections.Generic.List[object]'; $seen = New-Object 'Collections.Generic.HashSet[string]'
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
$packages.Add(@{ SPDXID='SPDXRef-ffmpeg'; name='ffmpeg.exe'; downloadLocation='NOASSERTION'; filesAnalyzed=$false; licenseConcluded='NOASSERTION'; checksums=@(@{ algorithm='SHA256'; checksumValue=(Get-FileHash (Join-Path $publish 'tools/ffmpeg.exe') -Algorithm SHA256).Hash.ToLowerInvariant() }) })
$inventory=@{ spdxVersion='SPDX-2.3'; dataLicense='CC0-1.0'; SPDXID='SPDXRef-DOCUMENT'; name=('BeastScreen-' + $Version); documentNamespace=('https://github.com/NoLifeeSSJ/BeastScreen/sbom/' + [Guid]::NewGuid()); creationInfo=@{ created=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); creators=@('Tool: BeastScreen-Package.ps1') }; packages=@($packages.ToArray()) }
$sbom=Join-Path $output 'BeastScreen-sbom.spdx.json'; [IO.File]::WriteAllText($sbom, ($inventory | ConvertTo-Json -Depth 12)); Copy-Item $sbom $publish
if ($ForRelease) {
    $sources = Join-Path $artifacts 'audited-corresponding-source'
    if (-not (Test-Path (Join-Path $sources 'SOURCE-MANIFEST.sha256'))) { throw 'Matching, audited complete corresponding source and SOURCE-MANIFEST.sha256 are required before distribution.' }
    Compress-Archive -Path (Join-Path $sources '*') -DestinationPath (Join-Path $output ('BeastScreen-' + $Version + '-corresponding-source.zip')) -Force
}
$zip=Join-Path $output ('BeastScreen-' + $Version + '-win-x64.zip'); Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal -Force
Copy-Item (Join-Path $repo 'BeastScreen-Install.ps1') $output -Force
Copy-Item (Join-Path $repo 'docs/release-notes.md') (Join-Path $output 'release-notes.md') -Force
$checksums = @(Get-ChildItem $output -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $checksums, (New-Object Text.UTF8Encoding($false)))
Write-Output ('Package prepared at ' + $zip + $(if ($ForRelease) { '' } else { ' (local development only; do not redistribute before the native audit)' }))
