$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$names = @('ConnectionStrings__DefaultConnection','CENTRAL_UI_BASE_CONNECTION','CENTRAL_UI_DATABASE','CENTRAL_UI_DATABASE_PASSWORD','CENTRAL_UI_PASSWORD','CENTRAL_UI_API_ORIGIN','ProxyEncryption__ActiveKeyId','ProxyEncryption__Keys__browser-test-v1','ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS','Security__GlobalPermitLimit','Security__EndpointPermitLimit')
$savedEnvironment = @{}
foreach ($name in $names) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$apiProcess = $null
$created = $false
Push-Location $projectRoot
try {
    if (Get-NetTCPConnection -LocalPort 7091 -State Listen -ErrorAction SilentlyContinue) { throw 'Porta isolada 7091 ocupada.' }
    . (Join-Path $PSScriptRoot 'Set-LocalDatabase.ps1')
    $env:CENTRAL_UI_BASE_CONNECTION = $env:ConnectionStrings__DefaultConnection
    $env:CENTRAL_UI_DATABASE = 'p6_ui_test_' + [Guid]::NewGuid().ToString('N')
    $env:CENTRAL_UI_PASSWORD = 'Synthetic!a9' + [Guid]::NewGuid().ToString('N')
    $env:CENTRAL_UI_DATABASE_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $env:ProxyEncryption__ActiveKeyId = 'browser-test-v1'
    $randomKey = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomKey) } finally { $generator.Dispose() }
    [Environment]::SetEnvironmentVariable('ProxyEncryption__Keys__browser-test-v1', [Convert]::ToBase64String($randomKey))
    [Array]::Clear($randomKey, 0, $randomKey.Length)
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'https://localhost:7091'
    $env:Security__GlobalPermitLimit = '10000'; $env:Security__EndpointPermitLimit = '1000'
    $output = Join-Path $projectRoot '.local/build-24c-ui/'
    dotnet build scripts/browser/BrowserFixture.csproj --no-restore "-p:BaseOutputPath=$output"
    if ($LASTEXITCODE -ne 0) { throw 'Build da fixture falhou.' }
    $fixture = Join-Path $output 'Debug/net10.0/BrowserFixture.dll'
    $created = $true
    dotnet $fixture create
    if ($LASTEXITCODE -ne 0) { throw 'Criação da fixture descartável falhou.' }
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection.set_ConnectionString($env:CENTRAL_UI_BASE_CONNECTION)
    $connection.set_Item('Database', $env:CENTRAL_UI_DATABASE)
    $connection.set_Item('Username', $env:CENTRAL_UI_DATABASE)
    $connection.set_Item('Password', $env:CENTRAL_UI_DATABASE_PASSWORD)
    if ($connection.get_Item('Database') -ne $env:CENTRAL_UI_DATABASE -or $connection.get_Item('Username') -ne $env:CENTRAL_UI_DATABASE) { throw 'Destino descartável divergente.' }
    $env:ConnectionStrings__DefaultConnection = $connection.get_ConnectionString()
    $env:CENTRAL_UI_API_ORIGIN = 'https://localhost:7091'
    $artifacts = Join-Path $projectRoot '.local/verification-2.4c'
    $null = New-Item -ItemType Directory -Force -Path $artifacts
    $password = $env:CENTRAL_UI_PASSWORD; $env:CENTRAL_UI_PASSWORD = $null
    # API already validated, unchanged backend; never rebuild its live assemblies.
    $apiProcess = Start-Process -FilePath (Join-Path $env:ProgramFiles 'dotnet/dotnet.exe') -ArgumentList (Join-Path $projectRoot '.local/build-2.4b3/Debug/net10.0/CentralContingencia.Api.dll') -WorkingDirectory (Join-Path $projectRoot 'src/CentralContingencia.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifacts 'isolated-api-stdout.log') -RedirectStandardError (Join-Path $artifacts 'isolated-api-stderr.log')
    $env:CENTRAL_UI_PASSWORD = $password; $password = $null
    node scripts/Verify-ProfileUi.cjs --real
    if ($LASTEXITCODE -ne 0) { throw 'Playwright com API descartável falhou.' }
    foreach ($log in @('isolated-api-stdout.log','isolated-api-stderr.log')) {
        $stream = [IO.File]::Open((Join-Path $artifacts $log), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = [IO.StreamReader]::new($stream)
        try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($secret in @($env:CENTRAL_UI_PASSWORD,$env:CENTRAL_UI_DATABASE_PASSWORD,[Environment]::GetEnvironmentVariable('ProxyEncryption__Keys__browser-test-v1'),'SyntheticProxy!123','synthetic-sensitive-profile-user')) {
            if ($content.Contains($secret)) { throw 'Segredo de teste encontrado em logs.' }
        }
    }
    Write-Output 'Logs isolados verificados: sem credenciais ou chave de teste.'
} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
    if ($created) { dotnet $fixture drop; if ($LASTEXITCODE -ne 0) { throw 'Descarte da fixture pendente.' } }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Pop-Location
}
