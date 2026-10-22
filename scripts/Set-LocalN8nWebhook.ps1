param([string]$Url = 'http://localhost:5678/webhook/p6-ingestao-perfil')
$ErrorActionPreference = 'Stop'
# Carrega o endereço e a chave do webhook do n8n nas variáveis do processo atual, antes de iniciar a API.
# Endereço padrão: fluxo ativo no n8n local. Use -Url para outro caminho (por exemplo, o de teste /webhook-test/...).
$projectRoot = Split-Path -Parent $PSScriptRoot
$keyFile = Join-Path $projectRoot '.local/secrets/n8n-webhook-key.dpapi'
if (-not (Test-Path -LiteralPath $keyFile)) { throw 'Gere a chave antes: scripts/New-LocalN8nWebhookKey.ps1' }
$secureKey = ConvertTo-SecureString ([System.IO.File]::ReadAllText($keyFile).Trim())
$pointer = [IntPtr]::Zero
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    [Environment]::SetEnvironmentVariable('N8nIngestion__WebhookKey', [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer), 'Process')
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $secureKey.Dispose()
}
[Environment]::SetEnvironmentVariable('N8nIngestion__WebhookUrl', $Url, 'Process')
