$ErrorActionPreference = 'Stop'
# Inicia a API local com tudo o que estiver configurado em .local/secrets: banco, criptografia de proxies, AdsPower,
# chaves do n8n (ingestão) e o webhook do site. Escuta em 0.0.0.0:7090 para o n8n (Docker) alcançar a Central.
$projectRoot = Split-Path -Parent $PSScriptRoot
# Encerra instâncias anteriores da API (a porta 7090 precisa estar livre).
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -match 'CentralContingencia.Api' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Confirm:$false; Wait-Process -Id $_.ProcessId -Timeout 20 -ErrorAction SilentlyContinue }
. (Join-Path $PSScriptRoot 'Set-LocalDatabase.ps1')
. (Join-Path $PSScriptRoot 'Set-LocalProxyEncryption.ps1')
$secrets = Join-Path $projectRoot '.local/secrets'
if (Test-Path -LiteralPath (Join-Path $secrets 'adspower-api-key.dpapi')) { . (Join-Path $PSScriptRoot 'Set-LocalAdsPower.ps1') }
if (Test-Path -LiteralPath (Join-Path $secrets 'integration-keys.json')) { . (Join-Path $PSScriptRoot 'Set-LocalIntegration.ps1') }
if (Test-Path -LiteralPath (Join-Path $secrets 'n8n-webhook-key.dpapi')) { . (Join-Path $PSScriptRoot 'Set-LocalN8nWebhook.ps1') }
else { Write-Warning 'Webhook do n8n não configurado: a tela de envio de TXT ficará indisponível. Rode scripts/New-LocalN8nWebhookKey.ps1.' }
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', 'https://0.0.0.0:7090;http://localhost:5041', 'Process')
Push-Location $projectRoot
try {
    dotnet run --project src/CentralContingencia.Api --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar a API.' }
} finally { Pop-Location }
