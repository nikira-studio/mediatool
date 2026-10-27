param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'The local .NET SDK is missing. Run scripts/bootstrap.ps1 first.' }

Push-Location $repoRoot
try {
    # Exercises queue reuse, a real repeat compression, and conflict-safe version names.
    & $dotnet test MediaTool.slnx --configuration Release --no-restore --filter 'FullyQualifiedName~SequentialJobServiceTests|FullyQualifiedName~CompressionJobHandlerTests|FullyQualifiedName~OutputFileServiceTests'
    if ($LASTEXITCODE -ne 0) { throw 'User-workflow regression checks failed.' }
}
finally { Pop-Location }
