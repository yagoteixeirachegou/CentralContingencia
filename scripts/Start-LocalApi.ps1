. (Join-Path $PSScriptRoot 'Set-LocalDatabase.ps1')
Push-Location $projectRoot
try {
    dotnet run --project src/CentralContingencia.Api --launch-profile https
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar a API.' }
} finally { Pop-Location }
