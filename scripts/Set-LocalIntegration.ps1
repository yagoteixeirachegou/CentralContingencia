$ErrorActionPreference = 'Stop'
# Carrega as chaves de serviço (somente hashes) como variáveis do processo atual, antes de iniciar a API.
$projectRoot = Split-Path -Parent $PSScriptRoot
$file = Join-Path $projectRoot '.local/secrets/integration-keys.json'
if (-not (Test-Path -LiteralPath $file)) { throw 'Gere uma chave antes: scripts/New-LocalIntegrationKey.ps1' }
$entries = @(Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json)
for ($i = 0; $i -lt $entries.Count; $i++) {
    $entry = $entries[$i]
    [Environment]::SetEnvironmentVariable("Integration__Keys__${i}__Id", [string]$entry.Id, 'Process')
    [Environment]::SetEnvironmentVariable("Integration__Keys__${i}__Name", [string]$entry.Name, 'Process')
    [Environment]::SetEnvironmentVariable("Integration__Keys__${i}__KeyHash", [string]$entry.KeyHash, 'Process')
    $scopes = @($entry.Scopes)
    for ($j = 0; $j -lt $scopes.Count; $j++) { [Environment]::SetEnvironmentVariable("Integration__Keys__${i}__Scopes__${j}", [string]$scopes[$j], 'Process') }
}
