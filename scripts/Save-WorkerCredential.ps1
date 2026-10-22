$ErrorActionPreference = 'Stop'
# Guarda a credencial do executor (worker) protegida pelo Windows (DPAPI, usuário atual) FORA do repositório:
# %LOCALAPPDATA%\CentralContingencia\worker-credential.dpapi. A credencial é digitada com entrada oculta e nunca
# aparece na tela, em logs ou em arquivos do projeto. Só o mesmo usuário Windows, na mesma máquina, consegue lê-la.
# A credencial é emitida uma única vez pelo Admin (POST /api/v1/executors ou tela Tarefas > Executores).
$folder = Join-Path $env:LOCALAPPDATA 'CentralContingencia'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$secure = Read-Host 'Cole a credencial do executor (ce1....; a entrada fica oculta)' -AsSecureString
$plain = [System.Net.NetworkCredential]::new('', $secure).Password
if ($plain -notmatch '^ce1\.[0-9a-f]{32}\.[A-Za-z0-9_-]{43}$') { $secure.Dispose(); throw 'Formato de credencial inválido; nada foi salvo.' }
[System.IO.File]::WriteAllText((Join-Path $folder 'worker-credential.dpapi'), (ConvertFrom-SecureString $secure))
$secure.Dispose(); $plain = $null
Write-Host "Credencial protegida salva em $folder\worker-credential.dpapi"
