$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$settings = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $projectRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
$builder = New-Object System.Data.Common.DbConnectionStringBuilder
$builder['Host'] = '127.0.0.1'
$builder['Port'] = $settings['POSTGRES_PORT']
$builder['Database'] = $settings['POSTGRES_DB']
$builder['Username'] = $settings['POSTGRES_USER']
$builder['Password'] = $settings['POSTGRES_PASSWORD']
$previousConnection = $env:CENTRAL_TEST_POSTGRES
try {
    $env:CENTRAL_TEST_POSTGRES = $builder.ConnectionString
    dotnet test (Join-Path $projectRoot 'CentralContingencia.slnx') --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
} finally {
    $env:CENTRAL_TEST_POSTGRES = $previousConnection
}
