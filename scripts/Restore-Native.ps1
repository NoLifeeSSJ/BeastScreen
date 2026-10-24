#requires -Version 5.1
[CmdletBinding()]
param([string]$SevenZip = 'C:\Program Files\7-Zip\7z.exe')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'BeastScreen-Install.ps1')
$lock = Get-Content (Join-Path $repo 'native-dependencies.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repo '.native\cache'; $native = Join-Path $repo '.native\win-x64'; $tools = Join-Path $repo '.native\tools'
New-Item -ItemType Directory -Force $cache,$native,$tools | Out-Null
if (-not (Test-Path -LiteralPath $SevenZip)) { throw 'Install 7-Zip or pass -SevenZip with its trusted executable path.' }
function Receive-Pinned($Dependency, [string]$Name) {
    $path = Join-Path $cache $Name
    if (Test-Path -LiteralPath $path) { if ((Get-FileHash $path -Algorithm SHA256).Hash -ieq $Dependency.sha256) { return $path }; Remove-Item -LiteralPath $path }
    $partial = $path + '.partial'
    try { Invoke-WebRequest -UseBasicParsing -Uri $Dependency.url -OutFile $partial -TimeoutSec 300; if ((Get-FileHash $partial -Algorithm SHA256).Hash -ine $Dependency.sha256) { throw ('Checksum mismatch: ' + $Name) }; Move-Item -LiteralPath $partial -Destination $path }
    finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
    return $path
}
$mpv = Receive-Pinned $lock.mpv 'mpv.7z'; & $SevenZip x $mpv ('-o' + $native) 'libmpv-2.dll' '-y' | Out-Host
if ($LASTEXITCODE -ne 0 -or (Get-FileHash (Join-Path $native 'libmpv-2.dll') -Algorithm SHA256).Hash -ine $lock.mpv.dllSha256) { throw 'libmpv extraction or DLL verification failed.' }
$ffmpeg = Receive-Pinned $lock.ffmpeg 'ffmpeg.7z'; & $SevenZip x $ffmpeg ('-o' + $tools) 'ffmpeg.exe' '-y' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg extraction failed.' }
$loaderZip = Receive-Pinned $lock.vulkanLoader 'vulkan-loader.zip'; $headersZip = Receive-Pinned $lock.vulkanHeaders 'vulkan-headers.zip'
$sources = Join-Path $cache 'sources'
if (-not (Test-Path -LiteralPath $sources)) {
    $null = Expand-SafePackage $loaderZip (Join-Path $sources 'loader'); $null = Expand-SafePackage $headersZip (Join-Path $sources 'headers')
}
$loader = Join-Path (Join-Path $sources 'loader') ('Vulkan-Loader-' + $lock.vulkanLoader.revision)
$headers = Join-Path (Join-Path $sources 'headers') ('Vulkan-Headers-' + $lock.vulkanHeaders.revision)
$headersBuild = Join-Path $cache 'headers-build'; $headersInstall = Join-Path $cache 'headers-install'; $loaderBuild = Join-Path $cache 'loader-build'
& cmake -S $headers -B $headersBuild -A x64 ('-DCMAKE_INSTALL_PREFIX=' + $headersInstall) '-DVULKAN_HEADERS_ENABLE_TESTS=OFF'
if ($LASTEXITCODE -ne 0) { throw 'Vulkan-Headers configuration failed. Use Visual Studio C++ build tools and CMake.' }
& cmake --build $headersBuild --config Release --target install --parallel 2; if ($LASTEXITCODE -ne 0) { throw 'Vulkan-Headers installation failed.' }
& cmake -S $loader -B $loaderBuild -A x64 ('-DCMAKE_PREFIX_PATH=' + $headersInstall) '-DBUILD_TESTS=OFF' '-DDYNAMIC_LINK_MSVC_RUNTIME_LIBRARY=OFF' '-DUPDATE_DEPS=OFF'
if ($LASTEXITCODE -ne 0) { throw 'Vulkan loader configuration failed.' }
& cmake --build $loaderBuild --config Release --parallel 2; if ($LASTEXITCODE -ne 0) { throw 'Vulkan loader build failed.' }
$dll = @(Get-ChildItem -LiteralPath $loaderBuild -Filter 'vulkan-1.dll' -Recurse | Where-Object { $_.FullName -match '[\\/]Release[\\/]' })
if ($dll.Count -ne 1) { throw 'Expected exactly one Release Vulkan loader DLL.' }
Copy-Item -LiteralPath $dll[0].FullName -Destination (Join-Path $native 'vulkan-1.dll') -Force
Copy-Item -LiteralPath $dll[0].FullName -Destination (Join-Path $tools 'vulkan-1.dll') -Force
Write-Output 'Pinned native development dependencies prepared. Redistribution remains gated by docs/release-audit.json.'
