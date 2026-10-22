$ErrorActionPreference = 'Stop'
# Carrega a chave protegida do AdsPower na variável do processo atual (AdsPower__ApiKey) antes de iniciar a API.
$projectRoot = Split-Path -Parent $PSScriptRoot
$keyFile = Join-Path $projectRoot '.local/secrets/adspower-api-key.dpapi'
if (-not (Test-Path -LiteralPath $keyFile)) { throw 'Salve a chave antes: scripts/Save-LocalAdsPowerKey.ps1' }
$secureKey = ConvertTo-SecureString ([System.IO.File]::ReadAllText($keyFile).Trim())
$pointer = [IntPtr]::Zero
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    [Environment]::SetEnvironmentVariable('AdsPower__ApiKey', [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer), 'Process')
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $secureKey.Dispose()
}
