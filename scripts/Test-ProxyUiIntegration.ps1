$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $projectRoot '.local/verification-2.2b'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$savedEnvironment = @{}
$names = @('ConnectionStrings__DefaultConnection', 'CENTRAL_UI_BASE_CONNECTION', 'CENTRAL_UI_DATABASE', 'CENTRAL_UI_DATABASE_PASSWORD', 'CENTRAL_UI_PASSWORD', 'CENTRAL_UI_API_ORIGIN', 'ProxyEncryption__ActiveKeyId', 'ProxyEncryption__Keys__browser-test-v1', 'ASPNETCORE_ENVIRONMENT', 'Security__GlobalPermitLimit', 'Security__EndpointPermitLimit')
foreach ($name in $names) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$apiProcess = $null
$created = $false
$failure = $false
Push-Location $projectRoot
try {
    if (Get-NetTCPConnection -LocalPort 7091 -State Listen -ErrorAction SilentlyContinue) { throw 'A porta isolada 7091 já está ocupada.' }
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
    # Somente o processo descartável: evita que a suite de cenários consecutivos limite a si mesma.
    $env:Security__GlobalPermitLimit = '1000'; $env:Security__EndpointPermitLimit = '100'
    dotnet build scripts/browser/BrowserFixture.csproj --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build da fixture falhou.' }
    # Mesmo em falha parcial de create, o finally remove apenas o UUID gerado acima.
    $created = $true
    dotnet run --project scripts/browser/BrowserFixture.csproj --no-build -- create
    if ($LASTEXITCODE -ne 0) { throw 'Criação da fixture falhou.' }
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    # Chamadas explícitas: o adaptador PowerShell deste builder trata atribuição
    # .ConnectionString como palavra-chave e pode preservar a conexão original.
    $connection.set_ConnectionString($env:CENTRAL_UI_BASE_CONNECTION)
    $connection.set_Item('Database', $env:CENTRAL_UI_DATABASE)
    $connection.set_Item('Username', $env:CENTRAL_UI_DATABASE)
    $connection.set_Item('Password', $env:CENTRAL_UI_DATABASE_PASSWORD)
    if ($connection.get_Item('Database') -ne $env:CENTRAL_UI_DATABASE -or $connection.get_Item('Username') -ne $env:CENTRAL_UI_DATABASE) { throw 'Destino descartável não confirmado.' }
    $env:ConnectionStrings__DefaultConnection = $connection.get_ConnectionString()
    $secret = $env:CENTRAL_UI_PASSWORD; $env:CENTRAL_UI_PASSWORD = $null
    $apiProcess = Start-Process dotnet -ArgumentList @('run', '--project', 'src/CentralContingencia.Api', '--no-build', '--no-launch-profile', '--urls', 'https://localhost:7091') -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifacts 'isolated-api-stdout.log') -RedirectStandardError (Join-Path $artifacts 'isolated-api-stderr.log')
    $env:CENTRAL_UI_PASSWORD = $secret; $secret = $null
    $env:CENTRAL_UI_API_ORIGIN = 'https://localhost:7091'
    node scripts/Verify-ProxyUi.cjs --real
    if ($LASTEXITCODE -ne 0) { throw 'Validação em Chrome com API isolada falhou.' }
    dotnet run --project scripts/browser/BrowserFixture.csproj --no-build -- verify
    if ($LASTEXITCODE -ne 0) { throw 'Verificação de persistência/auditoria falhou.' }
    $key = [Environment]::GetEnvironmentVariable('ProxyEncryption__Keys__browser-test-v1')
    foreach ($log in @('isolated-api-stdout.log', 'isolated-api-stderr.log')) {
        $stream = [IO.File]::Open((Join-Path $artifacts $log), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = [IO.StreamReader]::new($stream)
        try { $content = $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
        if ($content.Contains($key) -or $content.Contains($env:CENTRAL_UI_PASSWORD) -or $content.Contains('SyntheticProxy!123')) { throw 'Material sensível encontrado nos logs isolados.' }
    }
    Write-Output 'Logs isolados verificados: sem chave ou senhas de teste.'
} catch { $failure = $true; Write-Output 'Integração local falhou; consulte o resultado sanitizado da verificação.' }
finally {
    if ($apiProcess -and !$apiProcess.HasExited) {
        # Encerrar apenas o dotnet run que este script iniciou e seu filho da API isolada.
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($apiProcess.Id)"
        foreach ($child in $children) { Stop-Process -Id $child.ProcessId -ErrorAction SilentlyContinue }
        Stop-Process -Id $apiProcess.Id -ErrorAction SilentlyContinue
    }
    if ($created) {
        dotnet run --project scripts/browser/BrowserFixture.csproj --no-build -- drop
        if ($LASTEXITCODE -ne 0) { $failure = $true; Write-Output 'Limpeza pendente do banco descartável criado nesta execução.' }
    }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Pop-Location
}
if ($failure) { throw 'Teste de integração UI/API não concluído integralmente.' }
