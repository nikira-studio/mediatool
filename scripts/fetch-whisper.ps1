$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'sidecars\whisper.json') -Raw | ConvertFrom-Json
$destination = Join-Path $repoRoot '.tools\whisper'
$versionMarker = Join-Path $destination '.version'
if ((Test-Path -LiteralPath (Join-Path $destination 'whisper-cli.exe')) -and (Test-Path -LiteralPath $versionMarker) -and ((Get-Content -LiteralPath $versionMarker -Raw).Trim() -eq $manifest.version)) { exit 0 }
$downloads = Join-Path $repoRoot '.tools\downloads'
$archive = Join-Path $downloads "whisper-$($manifest.version)-x64.zip"
$extract = Join-Path $downloads "whisper-$($manifest.version)-x64"
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $manifest.downloadUrl -OutFile $archive }
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $manifest.sha256.ToLowerInvariant()) { throw "whisper.cpp archive checksum mismatch. Expected $($manifest.sha256), received $actualHash." }
if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
Expand-Archive -LiteralPath $archive -DestinationPath $extract
$source = Get-ChildItem -LiteralPath $extract -Filter 'whisper-cli.exe' -Recurse | Select-Object -First 1
if ($null -eq $source) { throw 'The verified whisper.cpp archive has no whisper-cli.exe.' }
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $source.DirectoryName -File | Copy-Item -Destination $destination -Force
Set-Content -LiteralPath $versionMarker -Value $manifest.version -NoNewline
