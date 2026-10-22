param(
    [Parameter(Mandatory = $true)][string]$ApiUrl,        # ex.: https://central.exemplo.com  (HTTPS obrigatório)
    [string]$WorkerExe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\CentralContingencia.Worker\bin\Release\net10.0\CentralContingencia.Worker.exe')
)
$ErrorActionPreference = 'Stop'
# Inicia o worker REMOTO neste computador Windows. Lê a credencial protegida por DPAPI (Save-WorkerCredential.ps1),
# entrega-a ao processo filho apenas por variável de ambiente do processo e nunca a imprime.
# Este modo não usa banco de dados: não defina ConnectionStrings__DefaultConnection (o worker recusa iniciar se existir).
$file = Join-Path $env:LOCALAPPDATA 'CentralContingencia\worker-credential.dpapi'
if (-not (Test-Path $file)) { throw "Credencial não encontrada. Execute scripts\Save-WorkerCredential.ps1." }
if ($ApiUrl -notmatch '^https://') { throw 'ApiUrl deve usar HTTPS.' }
if ($env:ConnectionStrings__DefaultConnection) { Remove-Item Env:\ConnectionStrings__DefaultConnection }
$secure = ConvertTo-SecureString (Get-Content $file -Raw)
$env:Worker__Api__Credential = [System.Net.NetworkCredential]::new('', $secure).Password
$env:Worker__Api__BaseUrl = $ApiUrl
$env:DOTNET_ENVIRONMENT = 'Production'
try { & $WorkerExe } finally { Remove-Item Env:\Worker__Api__Credential -ErrorAction SilentlyContinue }
