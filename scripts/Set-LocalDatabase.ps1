$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$settings = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $projectRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
}
foreach ($key in @('POSTGRES_PORT','POSTGRES_DB','POSTGRES_USER','POSTGRES_PASSWORD')) {
    if ([string]::IsNullOrWhiteSpace($settings[$key])) { throw "Configure $key no .env local." }
}
$connectionBuilder = New-Object System.Data.Common.DbConnectionStringBuilder
$connectionBuilder['Host'] = '127.0.0.1'
$connectionBuilder['Port'] = $settings['POSTGRES_PORT']
$connectionBuilder['Database'] = $settings['POSTGRES_DB']
$connectionBuilder['Username'] = $settings['POSTGRES_USER']
$connectionBuilder['Password'] = $settings['POSTGRES_PASSWORD']
$env:ConnectionStrings__DefaultConnection = $connectionBuilder.ConnectionString
