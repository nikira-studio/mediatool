param(
    [string]$Version = '0.1.18'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$output = Join-Path $repoRoot "releases\MediaTool-$Version-portable-win-x64"
$archive = "$output.zip"

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw 'The local .NET SDK is missing. Run scripts/bootstrap.ps1 first.'
}

if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $archive)) { throw 'The requested release already exists. Choose a new version.' }

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

$noticeDirectory = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $noticeDirectory 'MediaTool-UNLICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'sidecars\THIRD-PARTY-NOTICES.md') -Destination $noticeDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'sidecars\whisper-LICENSE.txt') -Destination $noticeDirectory

$assets = Get-Content -LiteralPath (Join-Path $repoRoot 'src\MediaTool.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
function Find-PackageFile([string]$package, [string]$version, [string]$file) {
    foreach ($root in $packageRoots) {
        $candidate = Join-Path $root "$package\$version\$file"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw "Required license file not found for $package $version : $file"
}
$wpfVersion = (($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^WPF-UI/' }) -split '/')[1]
Copy-Item -LiteralPath (Find-PackageFile 'wpf-ui' $wpfVersion 'LICENSE.md') -Destination (Join-Path $noticeDirectory 'WPF-UI-LICENSE.md')
Copy-Item -LiteralPath (Find-PackageFile 'wpf-ui' $wpfVersion 'ThirdPartyNotices.txt') -Destination (Join-Path $noticeDirectory 'WPF-UI-THIRD-PARTY-NOTICES.txt')
$downloads = $assets.project.frameworks.'net10.0-windows'.downloadDependencies
$runtimeVersion = (($downloads | Where-Object { $_.name -eq 'Microsoft.NETCore.App.Runtime.win-x64' }).version -replace '[\[\] ]', '' -split ',')[0]
$desktopVersion = (($downloads | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App.Runtime.win-x64' }).version -replace '[\[\] ]', '' -split ',')[0]
if (-not $wpfVersion -or -not $runtimeVersion -or -not $desktopVersion) { throw 'Could not identify the packaged dependency versions.' }
Copy-Item -LiteralPath (Find-PackageFile 'microsoft.netcore.app.runtime.win-x64' $runtimeVersion 'LICENSE.TXT') -Destination (Join-Path $noticeDirectory 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Find-PackageFile 'microsoft.netcore.app.runtime.win-x64' $runtimeVersion 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $noticeDirectory 'dotnet-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Find-PackageFile 'microsoft.windowsdesktop.app.runtime.win-x64' $desktopVersion 'LICENSE') -Destination (Join-Path $noticeDirectory 'windowsdesktop-LICENSE.txt')

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal

$files = Get-ChildItem -LiteralPath $output -File -Recurse
$sizeMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Portable build: $output"
Write-Host "Files: $($files.Count)"
Write-Host "Unpacked size: $sizeMb MB"
Write-Host "ZIP: $archive"
