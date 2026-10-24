#requires -Version 5.1
param([string]$FFmpeg = (Join-Path (Split-Path $PSScriptRoot -Parent) '.native\tools\ffmpeg.exe'), [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\fixtures'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$file = Join-Path $OutputDirectory 'BeastScreen fixture 映画.mkv'
& $FFmpeg -nostdin -v error -f lavfi -i 'testsrc2=size=640x360:rate=30:duration=8' -f lavfi -i 'sine=frequency=440:duration=8' -f lavfi -i 'sine=frequency=660:duration=8' -map 0:v -map 1:a -map 2:a -c:v libx264 -pix_fmt yuv420p -c:a aac -metadata 'title=BeastScreen synthetic fixture' -metadata:s:a:0 'language=eng' -metadata:s:a:1 'language=fra' -y $file
if ($LASTEXITCODE -ne 0) { throw 'Failed to generate video/audio fixture.' }
[IO.File]::WriteAllText(([IO.Path]::ChangeExtension($file, '.srt')), "1`r`n00:00:00,000 --> 00:00:07,000`r`nBeastScreen subtitle fixture — 映画`r`n", (New-Object Text.UTF8Encoding($false)))
& $FFmpeg -nostdin -v error -f lavfi -i 'sine=frequency=440:duration=1' -c:a flac -metadata 'artist=BeastScreen tests' -metadata 'album=Synthetic fixtures' -metadata 'genre=Test' -y (Join-Path $OutputDirectory 'metadata.flac')
if ($LASTEXITCODE -ne 0) { throw 'Failed to generate music fixture.' }
Write-Output $file
