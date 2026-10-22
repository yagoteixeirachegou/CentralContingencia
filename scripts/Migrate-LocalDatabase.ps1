. (Join-Path $PSScriptRoot 'Set-LocalDatabase.ps1')
Push-Location $projectRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Falha na restauração da ferramenta EF.' }
    dotnet ef database update --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure
    if ($LASTEXITCODE -ne 0) { throw 'Falha na migration local.' }
} finally { Pop-Location }
