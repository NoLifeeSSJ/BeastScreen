#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo=Split-Path $PSScriptRoot -Parent
$manifest=Get-Content (Join-Path $repo 'docs/native-source-manifest.json') -Raw | ConvertFrom-Json
if(-not $manifest.completeAudit -or $manifest.components.Count -eq 0){throw 'Matching complete native source inventory has not been audited. Do not redistribute the prebuilt native artifacts.'}
$output=Join-Path $repo 'artifacts/audited-corresponding-source'; New-Item -ItemType Directory -Path $output -Force | Out-Null
$names=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$lines=@()
foreach($component in $manifest.components){
    if($component.file -notmatch '^[0-9A-Za-z][0-9A-Za-z_.-]+$' -or $component.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or ([Uri]$component.url).Scheme -ne 'https' -or -not $names.Add($component.file)){throw 'Unsafe or duplicate corresponding-source manifest entry'}
    $path=Join-Path $output $component.file; $partial=$path + '.partial'
    try { Invoke-WebRequest -UseBasicParsing -Uri $component.url -OutFile $partial -TimeoutSec 300; if((Get-FileHash $partial -Algorithm SHA256).Hash -ine $component.sha256){throw 'Corresponding-source checksum mismatch'}; Move-Item -LiteralPath $partial -Destination $path -Force }
    finally{if(Test-Path $partial){Remove-Item -LiteralPath $partial -Force}}
    $lines+=$component.sha256.ToLowerInvariant() + '  ' + $component.file
}
Copy-Item (Join-Path $repo 'docs/native-source-manifest.json') $output -Force
Copy-Item (Join-Path $repo 'THIRD_PARTY_NOTICES.md') $output -Force
Copy-Item (Join-Path $repo 'LICENSES') $output -Recurse -Force
[IO.File]::WriteAllLines((Join-Path $output 'SOURCE-MANIFEST.sha256'),$lines,(New-Object Text.UTF8Encoding($false)))
