. (Join-Path $PSScriptRoot 'Set-LocalDatabase.ps1')
$adminEmail = Read-Host 'E-mail sintético do primeiro administrador local'
$adminSecret = Read-Host 'Senha (12 a 128 caracteres, maiúscula, minúscula, número e símbolo)' -AsSecureString
$secretPointer = [IntPtr]::Zero
Push-Location $projectRoot
try {
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminSecret)
    $env:CENTRAL_ADMIN_EMAIL = $adminEmail
    $env:CENTRAL_ADMIN_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
    dotnet run --project src/CentralContingencia.Api --no-launch-profile -- --provision-admin
    if ($LASTEXITCODE -ne 0) { throw 'Provisionamento recusado. Consulte a documentação de segurança.' }
} finally {
    $env:CENTRAL_ADMIN_EMAIL = $null
    $env:CENTRAL_ADMIN_PASSWORD = $null
    if ($secretPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer) }
    $adminSecret.Dispose()
    Pop-Location
}
