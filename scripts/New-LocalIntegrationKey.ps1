param([string]$Name = 'n8n-ingestion')
$ErrorActionPreference = 'Stop'
# Gera uma chave de serviço para uma integração (n8n). A chave é mostrada UMA vez; só o hash SHA-256 é guardado
# em .local/secrets/integration-keys.json (fora do Git). Quem perder a chave gera outra e atualiza o n8n.
if ($Name -notmatch '^[A-Za-z0-9._-]{3,64}$') { throw 'Use um nome de 3 a 64 caracteres (letras, números, ponto, hífen ou sublinhado).' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$secrets = Join-Path $projectRoot '.local/secrets'
New-Item -ItemType Directory -Force -Path $secrets | Out-Null
$file = Join-Path $secrets 'integration-keys.json'
$entries = @()
if (Test-Path -LiteralPath $file) { $entries = @(Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json) }
if (@($entries | Where-Object { $_.Name -eq $Name }).Count -gt 0) { throw "Já existe uma chave chamada '$Name'. Escolha outro nome ou remova a entrada do arquivo." }
$bytes = New-Object byte[] 32
$generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$generator.GetBytes($bytes); $generator.Dispose()
$key = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = ([BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($key))) -replace '-', '').ToLowerInvariant()
$sha.Dispose()
$entries += [pscustomobject]@{ Id = [guid]::NewGuid().ToString(); Name = $Name; KeyHash = $hash; Scopes = @('profile-ingestion') }
[System.IO.File]::WriteAllText($file, (ConvertTo-Json -InputObject @($entries) -Depth 4), (New-Object System.Text.UTF8Encoding $false))
Write-Host ''
Write-Host "Chave '$Name' criada. Copie AGORA; ela não será mostrada de novo:" -ForegroundColor Yellow
Write-Host ''
Write-Host "    $key"
Write-Host ''
Write-Host 'No n8n: Credentials > Header Auth > Name = Authorization, Value = Bearer <a chave acima>.'
Write-Host 'Depois reinicie a API carregando as chaves (scripts/Set-LocalIntegration.ps1).'
$key = $null
