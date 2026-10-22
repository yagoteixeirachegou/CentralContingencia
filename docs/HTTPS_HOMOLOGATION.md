# Procedimento de homologação HTTPS — API e worker (Windows e Railway)

Objetivo: provar, antes de qualquer deploy, que o worker (processo separado, possivelmente em outra máquina) fala com a API somente por HTTPS **com validação TLS completa**,
executa tarefas, sobrevive a falhas e respeita revogação. **Nunca desligue a validação de certificado** para fazer o teste passar. Use dados e credenciais fictícios e um banco descartável.
Evidência de que este roteiro foi executado: `docs/etapa-2c1-report.md`.

## 0. Pré-requisitos
- .NET SDK 10, Docker (para o PostgreSQL descartável) e `openssl` (Git Bash). Build: `dotnet build CentralContingencia.slnx`.
- PostgreSQL descartável (porta própria, senha aleatória, **banco novo**): `docker run -d --name p6-homolog-pg -e POSTGRES_PASSWORD=<aleatória> -p 127.0.0.1:55433:5432 postgres:17-alpine`.
- Esquema: `dotnet ef migrations script --idempotent --project src/CentralContingencia.Infrastructure -o all.sql` e aplique em um **banco novo** com `psql` (`ON_ERROR_STOP=1`). Confira `select count(*) from "__EFMigrationsHistory"` = 16.
- Primeiro Admin (sintético): `CENTRAL_ADMIN_EMAIL`/`CENTRAL_ADMIN_PASSWORD` + `dotnet …Api.dll --provision-admin` (as variáveis são limpas do processo).
- Para a API subir rápido em homologação, reduza os prazos: `WorkQueue__LeaseSeconds=8`, `WorkQueue__ExecutorHeartbeatSeconds=2`, `WorkQueue__ExecutorOnlineSeconds=5`, `WorkQueue__RecoveryIntervalSeconds=2`.

## 1. Confiança do certificado no Windows (verificação somente leitura)
1. `dotnet dev-certs https --check --trust` → informa se existe certificado de desenvolvimento **confiável**. Confirme em `Cert:\CurrentUser\Root` (PowerShell: `Get-ChildItem Cert:\CurrentUser\Root | ? Subject -eq 'CN=localhost'`).
2. Se estiver confiável (como nesta máquina, thumbprint `0E4C8EAC…`), o worker aceita `https://localhost:<porta>` sem configuração extra.
3. **Se não estiver, peça autorização antes de executar `dotnet dev-certs https --trust`** (altera o repositório de certificados do usuário). A alternativa que não altera o sistema é a CA de teste do passo 2.
4. Teste rápido: `curl https://localhost:7191/health` (sem `-k`) deve responder `Healthy`.

## 2. CA privada de teste, sem tocar na confiança do sistema
```
openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.pem -days 2 -subj "/CN=homolog-ca" -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,keyCertSign,cRLSign"
openssl req -newkey rsa:2048 -nodes -keyout srv.key -out srv.csr -subj "/CN=localhost"
printf "subjectAltName=DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n" > srv.ext
openssl x509 -req -in srv.csr -CA ca.pem -CAkey ca.key -CAcreateserial -out srv.pem -days 2 -extfile srv.ext
openssl pkcs12 -export -inkey srv.key -in srv.pem -certfile ca.pem -passout pass:<senha-descartável> -out srv.pfx
```
- API: `ASPNETCORE_URLS=https://localhost:7192`, `Kestrel__Certificates__Default__Path=<caminho Windows do srv.pfx>`, `Kestrel__Certificates__Default__Password=<senha>` (use caminhos no formato `C:/…`, não `/c/…`).
- Worker: `Worker__Api__BaseUrl=https://localhost:7192` e **`Worker__Api__TrustedCaFile=<caminho do ca.pem>`**. A CA vale só para o processo do worker; a validação continua completa (cadeia até a CA informada, validade e **nome do host**).
- Teste negativo obrigatório: o mesmo worker **sem** `TrustedCaFile` deve falhar no handshake com `Unavailable/HttpRequestException>AuthenticationException` e não reservar nada.
- Apague `ca.key`, `srv.key`, `srv.pfx` ao terminar. Nunca versione esses arquivos.

## 3. Roteiro ponta a ponta (API e worker em processos independentes)
1. Suba a API (passos 0–2) e verifique `GET /health/ready` = `Ready`.
2. Como Admin (cookie + CSRF): `POST /api/v1/executors {"name":"homolog-1"}` → guarde a credencial (exibida uma vez).
3. Suba o worker em **outro processo**: `DOTNET_ENVIRONMENT=Production`, `Worker__Api__BaseUrl`, `Worker__Api__Credential` (e `TrustedCaFile` se for o caso). Sem `ConnectionStrings__*`. Log esperado: `Executor homolog-1 (…) autenticado; lease 8s…`.
4. Crie tarefas (`POST /api/v1/tasks`, `Idempotency-Key` UUID): sucesso, `FailUntilAttempt`, `FailPermanent`, `FailTransient` (dead-letter) e uma longa; cancele a longa. Esperado: `Succeeded`, `Succeeded` (2 tentativas), `Failed`, `DeadLetter`, `Cancelled`.
5. **Morte abrupta**: `Stop-Process -Force` no worker durante uma tarefa longa.
   a. Reiniciar antes do lease vencer → auditoria `Claimed > Released > Claimed > Succeeded` (1 tentativa).
   b. Ficar fora mais que o lease → a **API** recupera (`LeaseExpired`) e o worker novo conclui (`Claimed > LeaseExpired > Claimed > Succeeded`, 2 tentativas).
6. **Revogação** durante o uso: `POST /api/v1/executors/{id}/revoke`. O worker continua vivo, recebe `401`, espera com backoff e não reserva mais nada.
7. **Higiene**: `grep -c "ce1\."` e `grep -c "Bearer "` nos logs da API e do worker devem dar 0. Evite consultar a API em laço rápido com o mesmo IP do worker (o limite global de 120 req/min por IP vale para pessoas; o canal do agente tem cota própria).
8. Remova o contêiner/banco descartável e os arquivos temporários.

## 4. Homologação na Railway (antes de qualquer tráfego real)
Pré: `docs/RAILWAY_DEPLOYMENT.md` (variáveis, certificado do Data Protection, `ReverseProxy`, `AllowedHosts`, migrations aplicadas sob controle humano, Admin provisionado).
1. **Faixa do proxy**: confirme com a documentação/suporte da Railway de onde as requisições chegam ao contêiner e preencha `ReverseProxy__KnownNetworks__0`. Sem isso a API recusa iniciar (comportamento seguro).
2. `GET https://<domínio>/health` → `Healthy`; `/health/ready` → `Ready` (só depois das migrations).
3. **IP real e HTTPS atrás do proxy**: faça 11 chamadas com credencial inválida a `/api/v1/agent/config` a partir do seu computador → a 11ª deve ser `429` com `Retry-After`, e uma chamada válida de **outro IP** (ex.: o celular) deve continuar `200`. Se ambos bloquearem juntos, o IP confiável está errado (todos aparecendo como o IP do proxy): corrija `ReverseProxy` antes de seguir.
4. Provisione um executor pela tela **Tarefas → Executores** e rode o worker no Windows/VM com `Worker__Api__BaseUrl=https://<domínio>` (certificado público da Railway: **não** use `TrustedCaFile`).
5. Repita os passos 4–7 da seção 3 contra o ambiente da Railway com um volume pequeno de tarefas simuladas.
6. Confira nos logs da Railway: nenhuma credencial, nenhum payload; recuperação de leases ocorrendo (`tarefa(s) com lease vencido recuperada(s)`) sem tráfego de usuários.
7. Só então considere o AgenteAdsPower. Revogue o executor de homologação ao final.

## 5. Critérios de aprovação
Todos os cenários da seção 3 passam; o teste negativo de TLS falha como esperado; nenhum segredo nos logs; 11ª tentativa inválida = 429; `/health/ready` = Ready; migrations aplicadas na ordem; nenhuma exceção não tratada nos logs. Qualquer item não executado deve ser registrado com o motivo no relatório.
