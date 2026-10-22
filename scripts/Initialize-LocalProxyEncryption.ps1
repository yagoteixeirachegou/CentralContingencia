$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$secretDirectory = Join-Path $projectRoot '.local/secrets'
$keyFile = Join-Path $secretDirectory 'proxy-development-v1.dpapi'
if (Test-Path -LiteralPath $keyFile) { throw 'A chave local ja existe. Nao sobrescrever chaves em uso.' }
New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
$keyBytes = New-Object byte[] 32
$generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $generator.GetBytes($keyBytes)
    $secureKey = ConvertTo-SecureString ([Convert]::ToBase64String($keyBytes)) -AsPlainText -Force
    # Sem -Key: DPAPI vincula o arquivo ao usuário Windows atual.
    $protectedKey = ConvertFrom-SecureString $secureKey
    $protectedBytes = [System.Text.Encoding]::UTF8.GetBytes($protectedKey)
    $keyStream = [System.IO.File]::Open($keyFile, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $keyStream.Write($protectedBytes, 0, $protectedBytes.Length) }
    finally { $keyStream.Dispose() }
    Write-Output 'Chave local criada com DPAPI; nenhum valor secreto foi exibido.'
} finally {
    [Array]::Clear($keyBytes, 0, $keyBytes.Length)
    $generator.Dispose()
    if ($secureKey) { $secureKey.Dispose() }
    $protectedKey = $null
}
