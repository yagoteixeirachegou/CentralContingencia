$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$keyFile = Join-Path $projectRoot '.local/secrets/proxy-development-v1.dpapi'
if (-not (Test-Path -LiteralPath $keyFile)) { throw 'Inicialize a chave local antes de configurar a criptografia de proxies.' }
$secureKey = ConvertTo-SecureString ([System.IO.File]::ReadAllText($keyFile).Trim())
$keyPointer = [IntPtr]::Zero
try {
    $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    [Environment]::SetEnvironmentVariable('ProxyEncryption__Keys__development-v1',
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer), 'Process')
    $env:ProxyEncryption__ActiveKeyId = 'development-v1'
} finally {
    if ($keyPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer) }
    $secureKey.Dispose()
}
