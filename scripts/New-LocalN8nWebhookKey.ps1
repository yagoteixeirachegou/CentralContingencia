$ErrorActionPreference = 'Stop'
# Gera a chave que a Central envia ao webhook do n8n (cabeçalho X-Central-Key) e a guarda protegida pelo Windows (DPAPI)
# em .local/secrets (fora do Git). A chave é mostrada UMA vez: cole-a na credencial Header Auth do n8n.
$projectRoot = Split-Path -Parent $PSScriptRoot
$secrets = Join-Path $projectRoot '.local/secrets'
New-Item -ItemType Directory -Force -Path $secrets | Out-Null
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$secure = ConvertTo-SecureString $key -AsPlainText -Force
[System.IO.File]::WriteAllText((Join-Path $secrets 'n8n-webhook-key.dpapi'), (ConvertFrom-SecureString $secure))
$secure.Dispose()
Write-Host ''
Write-Host 'Chave gerada e guardada em .local/secrets/n8n-webhook-key.dpapi (protegida pelo Windows).'
Write-Host 'No n8n: Credentials > Header Auth > Name: X-Central-Key | Value (copie agora, não será mostrada de novo):'
Write-Host ''
Write-Host $key
Write-Host ''
Write-Host 'Nome da credencial: Central de Contingência - webhook do site'
