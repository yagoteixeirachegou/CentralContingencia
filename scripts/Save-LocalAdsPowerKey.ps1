$ErrorActionPreference = 'Stop'
# Guarda a chave da API local do AdsPower protegida pelo Windows (DPAPI), em .local/secrets (fora do Git).
# A chave é digitada com entrada oculta e nunca aparece na tela, em logs ou em arquivos do repositório.
$projectRoot = Split-Path -Parent $PSScriptRoot
$secrets = Join-Path $projectRoot '.local/secrets'
New-Item -ItemType Directory -Force -Path $secrets | Out-Null
$secure = Read-Host 'Cole a chave de API do AdsPower (a entrada fica oculta)' -AsSecureString
if ($secure.Length -lt 16) { throw 'Chave muito curta; nada foi salvo.' }
[System.IO.File]::WriteAllText((Join-Path $secrets 'adspower-api-key.dpapi'), (ConvertFrom-SecureString $secure))
$secure.Dispose()
Write-Host 'Chave protegida salva em .local/secrets/adspower-api-key.dpapi'
