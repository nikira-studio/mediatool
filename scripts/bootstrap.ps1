$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sdkRoot = Join-Path $repoRoot '.tools\dotnet'
$dotnet = Join-Path $sdkRoot 'dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    $installer = Join-Path ([System.IO.Path]::GetTempPath()) 'MediaTool-dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Version '10.0.401' -InstallDir $sdkRoot -Architecture x64
    }
    finally {
        Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
    }
}

& $dotnet restore (Join-Path $repoRoot 'MediaTool.slnx') --use-lock-file
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\fetch-ffmpeg.ps1')
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg setup failed.' }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\fetch-whisper.ps1')
if ($LASTEXITCODE -ne 0) { throw 'whisper.cpp setup failed.' }
