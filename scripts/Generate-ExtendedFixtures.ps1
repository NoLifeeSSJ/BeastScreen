#requires -Version 5.1
param(
    [Parameter(Mandatory=$true)][string]$BaseFixture,
    [string]$FFmpeg = (Join-Path (Split-Path $PSScriptRoot -Parent) '.native\tools\ffmpeg.exe'),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\fixtures\extended')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$marker=Join-Path $OutputDirectory '.beastscreen-test-fixtures'
if((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0 -and -not (Test-Path -LiteralPath $marker)){throw 'Choose an empty or previously generated BeastScreen fixture directory.'}
if(-not (Test-Path -LiteralPath $BaseFixture -PathType Leaf)){throw 'Generate the base playback fixture first.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
[IO.File]::WriteAllText($marker,'Synthetic BeastScreen test media. No original user media belongs here.')
function Invoke-Fixture([string]$Name,[string[]]$Codec,[switch]$Audio){
    $inputSource=if($Audio){'sine=frequency=440:duration=2'}else{'testsrc2=size=160x96:rate=24:duration=2'}
    $arguments=@('-nostdin','-v','error','-y','-f','lavfi','-i',$inputSource) + $Codec + @((Join-Path $OutputDirectory $Name))
    & $FFmpeg @arguments
    if($LASTEXITCODE -ne 0){throw ('Could not generate the required fixture: ' + $Name)}
}
Invoke-Fixture 'h264.mp4' @('-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p')
Invoke-Fixture 'hevc.mkv' @('-c:v','libx265','-preset','ultrafast','-x265-params','pools=1:frame-threads=1:log-level=error','-pix_fmt','yuv420p')
Invoke-Fixture 'av1.mkv' @('-c:v','libaom-av1','-cpu-used','8','-threads','2','-crf','45','-pix_fmt','yuv420p')
Invoke-Fixture 'vp9.webm' @('-c:v','libvpx-vp9','-deadline','realtime','-cpu-used','8','-threads','2','-pix_fmt','yuv420p')
Invoke-Fixture 'mpeg2.mpg' @('-c:v','mpeg2video','-pix_fmt','yuv420p')
Invoke-Fixture 'aac.m4a' @('-c:a','aac') -Audio
Invoke-Fixture 'flac.flac' @('-c:a','flac') -Audio
Invoke-Fixture 'mp3.mp3' @('-c:a','libmp3lame') -Audio
Invoke-Fixture 'vorbis.ogg' @('-c:a','libvorbis') -Audio
Invoke-Fixture 'opus.opus' @('-c:a','libopus') -Audio
Invoke-Fixture 'radio.mp3' @('-c:a','libmp3lame','-b:a','128k','-write_xing','0','-id3v2_version','0') -Audio
Copy-Item -LiteralPath $BaseFixture -Destination (Join-Path $OutputDirectory 'direct.mkv') -Force
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'corrupt.mkv'),'This deliberately corrupt fixture is not media.')
foreach($kind in @('hls','dash')){New-Item -ItemType Directory -Path (Join-Path $OutputDirectory $kind) -Force | Out-Null}
$hls=Join-Path $OutputDirectory 'hls'
& $FFmpeg -nostdin -v error -y -i $BaseFixture -map '0:v:0' -map '0:a:0' -c:v libx264 -preset ultrafast -g 60 -sc_threshold 0 -c:a copy -f hls -hls_time 2 -hls_playlist_type vod -hls_segment_filename (Join-Path $hls 'segment-%03d.ts') (Join-Path $hls 'index.m3u8')
if($LASTEXITCODE -ne 0){throw 'Could not generate unencrypted HLS fixture.'}
$dash=Join-Path $OutputDirectory 'dash'
# FFmpeg dashenc splits the manifest directory on '/'; native Windows '\' paths
# otherwise put fragments in the process working directory instead of beside MPD.
$dashManifest=(Join-Path $dash 'index.mpd').Replace('\','/')
& $FFmpeg -nostdin -v error -y -i $BaseFixture -map '0:v:0' -map '0:a:0' -c:v libx264 -preset ultrafast -g 60 -sc_threshold 0 -c:a copy -f dash -seg_duration 2 $dashManifest
if($LASTEXITCODE -ne 0){throw 'Could not generate unencrypted DASH fixture.'}
if(@(Get-ChildItem -LiteralPath $dash -Filter 'init-stream*.m4s' -File).Count -lt 2 -or @(Get-ChildItem -LiteralPath $dash -Filter 'chunk-stream*.m4s' -File).Count -lt 2){throw 'DASH fragments were not generated beside the manifest.'}
Write-Output ('Generated controlled codec and stream fixtures in ' + $OutputDirectory)
