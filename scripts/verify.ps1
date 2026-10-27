param(
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw 'The local .NET SDK is missing. Run scripts/bootstrap.ps1 first.'
}

Push-Location $repoRoot
try {
    & $dotnet restore MediaTool.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

    & $dotnet test MediaTool.slnx --no-restore --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    $forbidden = Select-String -Path 'src\MediaTool.Core\*.csproj','src\MediaTool.Infrastructure\*.csproj' -Pattern 'UseWPF|PresentationFramework|WindowsBase' -Quiet
    if ($forbidden) { throw 'A UI-free project acquired a WPF dependency.' }

    & $dotnet build 'src\MediaTool.App\MediaTool.App.csproj' --no-restore --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }

    if (-not $SkipLaunch) {
        $app = Join-Path $repoRoot 'src\MediaTool.App\bin\Release\net10.0-windows\MediaTool.App.exe'
        $process = Start-Process -FilePath $app -PassThru
        Start-Sleep -Milliseconds 1500
        if ($process.HasExited) { throw "The app exited during launch with code $($process.ExitCode)." }
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { $process.Kill($true) }
    }
}
finally {
    Pop-Location
}
