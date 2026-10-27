param(
    [string]$Version = '0.1.15'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$output = Join-Path $repoRoot "releases\MediaTool-$Version-portable-win-x64"
$archive = "$output.zip"

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw 'The local .NET SDK is missing. Run scripts/bootstrap.ps1 first.'
}

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }

& $dotnet publish (Join-Path $repoRoot 'src\MediaTool.App\MediaTool.App.csproj') `
    --no-restore `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:SatelliteResourceLanguages=en `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal

$files = Get-ChildItem -LiteralPath $output -File -Recurse
$sizeMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Portable build: $output"
Write-Host "Files: $($files.Count)"
Write-Host "Unpacked size: $sizeMb MB"
Write-Host "ZIP: $archive"
