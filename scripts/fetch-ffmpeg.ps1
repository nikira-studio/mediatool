$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'sidecars\ffmpeg.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$destination = Join-Path $repoRoot '.tools\ffmpeg'
$ffmpeg = Join-Path $destination 'ffmpeg.exe'
$ffprobe = Join-Path $destination 'ffprobe.exe'

if ((Test-Path -LiteralPath $ffmpeg) -and (Test-Path -LiteralPath $ffprobe)) {
    exit 0
}

$downloadDirectory = Join-Path $repoRoot '.tools\downloads'
$archive = Join-Path $downloadDirectory "ffmpeg-$($manifest.version).zip"
$extractDirectory = Join-Path $downloadDirectory "ffmpeg-$($manifest.version)"
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri $manifest.downloadUrl -OutFile $archive
}

$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $manifest.sha256.ToLowerInvariant()) {
    throw "FFmpeg archive checksum mismatch. Expected $($manifest.sha256), received $actualHash."
}

if (Test-Path -LiteralPath $extractDirectory) { Remove-Item -LiteralPath $extractDirectory -Recurse -Force }
Expand-Archive -LiteralPath $archive -DestinationPath $extractDirectory
New-Item -ItemType Directory -Path $destination -Force | Out-Null

$ffmpegSource = Get-ChildItem -LiteralPath $extractDirectory -Filter 'ffmpeg.exe' -Recurse | Select-Object -First 1
$ffprobeSource = Get-ChildItem -LiteralPath $extractDirectory -Filter 'ffprobe.exe' -Recurse | Select-Object -First 1
if ($null -eq $ffmpegSource -or $null -eq $ffprobeSource) { throw 'The verified FFmpeg archive did not contain both required executables.' }
Copy-Item -LiteralPath $ffmpegSource.FullName -Destination $ffmpeg
Copy-Item -LiteralPath $ffprobeSource.FullName -Destination $ffprobe

Get-ChildItem -LiteralPath $extractDirectory -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|README)' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name) -Force
}
