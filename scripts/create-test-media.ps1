$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$ffmpeg = Join-Path $repoRoot '.tools\ffmpeg\ffmpeg.exe'
$destination = Join-Path $repoRoot 'tests\media\video-with-audio.mp4'
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null

& $ffmpeg -hide_banner -nostdin -y -f lavfi -i 'testsrc2=size=640x360:rate=30' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 10 -c:v libx264 -preset ultrafast -b:v 1500k -c:a aac -b:a 128k $destination
if ($LASTEXITCODE -ne 0) { throw 'Sample media generation failed.' }
