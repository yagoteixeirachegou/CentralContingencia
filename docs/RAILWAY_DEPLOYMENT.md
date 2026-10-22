# Implantação no Railway — preparação (Etapa 2A)

Estado: **preparado, não implantado.** Nenhum deploy, migration em banco existente, credencial real ou serviço externo foi usado.
Decisões: ADR 0016. Contexto e riscos de origem: `docs/AGENT_INTEGRATION_ASSESSMENT.md`.

## 1. Arquitetura da primeira implantação

```
Internet ──TLS──▶ proxy da Railway ──HTTP──▶ contêiner único (API ASP.NET Core + build do React em wwwroot)
                                                   │  ConnectionStrings__DefaultConnection
                                                   ▼
                                          PostgreSQL gerenciado (serviço Railway)
```

- Mesma origem: o React é servido pela API (`wwwroot`), então cookies `__Host-` + `SameSite=Strict` e CSRF continuam como estão.
- AdsPower e n8n **continuam locais** e não fazem parte deste serviço (seção 6). O futuro worker falará com a API por HTTPS de saída.

## 2. Imagem

`Dockerfile` (raiz) em três estágios: `node:22-alpine` (npm ci + build) → `dotnet/sdk:10.0` (restore, publish) → `dotnet/aspnet:10.0` (execução).

- Usuário não-root (`$APP_UID`), `ASPNETCORE_ENVIRONMENT=Production`, escuta `http://0.0.0.0:${PORT:-8080}` (a Railway injeta `PORT`).
- `global.json` não é copiado: a imagem traz o SDK 10.0.x mais novo (local: 10.0.300; a imagem usou 10.0.402). Fixar tag do SDK é uma decisão pendente.
- `.dockerignore` exclui `.env*`, `.local`, `*.pfx/*.key/*.dpapi`, `.git`, `tests`, `docs`, `scripts`, `node_modules`, `bin/obj`, `dist`. Nada local entra na imagem.
- Contexto de build: raiz do repositório. Build local: `docker build -t central-contingencia .`

## 3. Variáveis de ambiente (nomes; **nenhum valor neste repositório**)

Defina no painel da Railway (ou secret manager). `.env.example` traz o modelo comentado.

| Variável | Obrigatória | Observação |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | sim | Formato Npgsql. Na Railway, componha com as referências do serviço Postgres (`Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}`). Prefira a rede privada. Npgsql não lê `DATABASE_URL` em formato URI. Sem esta variável a API **recusa iniciar** |
| `AllowedHosts` | sim | Domínio público (ex.: `meuapp.up.railway.app`). `*` é recusado |
| `ReverseProxy__Enabled` | sim | `true` |
| `ReverseProxy__KnownNetworks__0` ou `ReverseProxy__KnownProxies__0` | sim | **A confirmar**: faixa/IP de onde a Railway entrega as requisições ao contêiner. Não foi verificada nesta etapa e não há valor padrão (ver seção 4) |
| `DataProtection__Store` | sim | `Database` |
| `DataProtection__CertificateBase64` / `DataProtection__CertificatePassword` | sim | PFX (RSA 2048+) em Base64 e senha. Cifra o anel de chaves em repouso (seção 5) |
| `DataProtection__PreviousCertificateBase64` / `…PreviousCertificatePassword` | rotação | Mantenha só durante a troca de certificado |
| `ProxyEncryption__ActiveKeyId`, `ProxyEncryption__Keys__<id>` | para usar proxies | Chave AES-256 (32 bytes, Base64) **nova de produção**; nunca reutilize a de desenvolvimento. Sem ela, operações com senha de proxy são recusadas (autenticação continua) |
| `LocalIntegrations__Enabled` | não | Padrão: `false` fora de Development. Mantenha `false` |
| `PORT` | automática | Injetada pela Railway |
| `Security__*` | não | Limites de rate limiting (padrões em `Program.cs`) |

Não defina `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`: confia em qualquer origem e anula a lista explícita.

A API **não inicia** fora de Development se faltar conexão, `DataProtection`, `ReverseProxy` ou `AllowedHosts` seguro (`ProductionConfiguration`). A mensagem de erro cita só os nomes das chaves.

## 4. HTTPS atrás de proxy e rate limiting

- Com `ReverseProxy:Enabled=true`, `UseForwardedHeaders` é o primeiro middleware e aceita `X-Forwarded-For` / `X-Forwarded-Proto` **somente** se a conexão imediata vier de um IP/rede listado. `ForwardLimit=1`. Redes `/0` e `0.0.0.0` são recusadas.
- Com `Enabled=false` (padrão/desenvolvimento) esses cabeçalhos são ignorados.
- O limitador de requisições particiona por `RemoteIpAddress`, que após o middleware é o IP real do cliente **só** quando o proxy é confiável. Origem não confiável não consegue forjar IP nem HTTPS (testado).
- **Bloqueio documentado:** o endereço de origem do proxy da Railway precisa ser confirmado na documentação/suporte da plataforma. Sem isso o deploy falha de forma segura (a API recusa iniciar). Confirme e preencha `ReverseProxy__KnownNetworks__0`. Não use `0.0.0.0/0`.

## 5. Data Protection persistente

- `DataProtection__Store=Database` grava o anel de chaves em `security."DataProtectionKeys"` (PostgreSQL), compartilhável entre réplicas e preservado entre deploys. Chaves ficam cifradas por certificado X.509 (`ProtectKeysWithCertificate`); sem o certificado o banco sozinho não decifra o anel (testado: o XML contém `EncryptedData`, não `masterKey`).
- Efeito: sessão (cookie), antiforgery e provas de importação CSV sobrevivem a restart (testado com duas instâncias da aplicação, mesmo banco e certificado).
- Gerar um certificado só para isso (não versione; guarde nas variáveis): 
  `openssl req -x509 -newkey rsa:3072 -nodes -keyout k.pem -out c.pem -days 1095 -subj "/CN=central-dataprotection"` e
  `openssl pkcs12 -export -inkey k.pem -in c.pem -out c.pfx` → `base64 -w0 c.pfx`. Apague os arquivos locais depois.
- **Rotação:** gere novo PFX → coloque o antigo em `PreviousCertificate*` e o novo em `Certificate*` → faça deploy → após o novo anel estar ativo e as sessões antigas expirarem, remova o anterior. Perder o certificado invalida cookies e provas (usuários precisam entrar de novo); não há dado de negócio cifrado por esse anel.
- Dependências: `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.8. Ela trouxe `System.Security.Cryptography.Xml`/`Pkcs` 10.0.8 com avisos de vulnerabilidade (NU1902/NU1903); foram fixados em 10.0.13 em `Infrastructure.csproj`. O aviso NU1510 restante ("não será podado") é esperado e inofensivo. Em produção, a imagem `aspnet:10.0` já traz o runtime corrigido; localmente o runtime instalado é 10.0.8.

## 6. Funcionalidades que dependem do ambiente local (isoladas)

Fora de Development (ou com `LocalIntegrations__Enabled=false`) estas rotas respondem **503** `LocalIntegrationUnavailable`; em Development (padrão) seguem exatamente como antes:

| Prefixo | Depende de | Telas afetadas |
|---|---|---|
| `/api/v1/adspower/*` | AdsPower local (`127.0.0.1:50325`) | Dashboard |
| `/api/v1/integration/*` | n8n local + AdsPower (numeração) | — (consumida pelo n8n) |
| `/api/v1/profile-ingestions/*` | n8n local (webhook) | Perfis → Novos perfis (TXT) |

Continuam disponíveis: autenticação, proxies, importação CSV, perfis, alocações, grid, aquecimento (registro; usa só o banco). O frontend ainda não tem aviso dedicado: as telas Dashboard e Ingestão mostrarão o erro 503 genérico (pendência de UX).

## 7. Banco e migrations (controle manual)

- A API **nunca** aplica migrations ao iniciar.
- Nova migration aditiva: `20261021183950_AddDataProtectionKeys` (cria `security."DataProtectionKeys"`); revisão em `docs/security/etapa-2a-dataprotection-migration.sql`. **Não foi aplicada em nenhum banco existente.** O `Down` só remove essa tabela.
- Para um banco novo: gere o script idempotente e aplique com `psql` sob controle humano, com backup quando houver dados:
  `dotnet ef migrations script --idempotent --project src/CentralContingencia.Infrastructure -o all.sql`.
  Revise (procure `DROP`/`ALTER ... TYPE`) antes de aplicar. Nesta etapa o script completo (13 migrations) foi aplicado apenas em um PostgreSQL descartável.
- Primeiro Administrador: `dotnet CentralContingencia.Api.dll --provision-admin` no contêiner, com `CENTRAL_ADMIN_EMAIL` e `CENTRAL_ADMIN_PASSWORD` temporários (a senha é limpa do ambiente do processo). Exige a configuração de produção completa.
- Ordem sugerida: banco criado → migrations → deploy → provisionar Admin → conferir `/health/ready`.

## 8. Health checks

- `/health`: processo no ar (não toca o banco). Use para *liveness*.
- `/health/ready`: PostgreSQL acessível **e** nenhuma migration pendente; responde `Ready` (200) ou `NotReady` (503), sem detalhes. Use como healthcheck de implantação **depois** de aplicar as migrations (antes, o deploy ficaria "não pronto" por desenho).

## 9. Validação realizada (21/10/2026, máquina local)

| Verificação | Resultado |
|---|---|
| `dotnet build CentralContingencia.slnx` | 0 erros; 2 avisos NU1510 (ver seção 5) |
| `dotnet test` (PostgreSQL 17 descartável em Docker) | **Unitários 326/326; integração 260/260** (inclui 30 testes novos de implantação e 2 testes existentes ajustados para a configuração de produção) |
| Frontend `tsc --noEmit`, `eslint`, `npm run build` | sem erros |
| Frontend `vitest run` | 213/213 com `--maxWorkers=2`; com paralelismo total 2 testes (ProfilesPage, ProxyImportPage) estouram o timeout de 1 s do `findBy`, passam isolados. Frontend não foi alterado nesta etapa; flakiness sob carga |
| `docker build` | OK (SDK 10.0.402 na imagem) |
| Contêiner em banco descartável | `/health` Healthy; `/health/ready` Ready; `/`, `/proxies`, `/proxies/import` servem o index; `/assets/inexistente.js` 404; `/api/v1/nope` 404; HTTPS só com `X-Forwarded-Proto` de rede confiável (400 sem ele); host fora de `AllowedHosts` 400; rotas locais 503; cookie CSRF `Secure; HttpOnly; SameSite=Strict`; usuário `app` (não-root); `PORT` respeitado; reinício reaproveitou a chave existente (1 linha antes e depois); logs sem `password`/`secret` |
| Mensagem `libgssapi_krb5.so.2` no log | inofensiva (Npgsql tenta Kerberos; a imagem não traz a lib e a autenticação é por senha) |

Não executado: deploy real; teste contra o proxy real da Railway; Playwright (`scripts/Verify-*.cjs`); testes de carga; scan de vulnerabilidades da imagem.

## 10. Riscos e pendências para a próxima etapa

1. Confirmar a origem do proxy da Railway (`ReverseProxy__KnownNetworks__0`). **Bloqueio para o primeiro deploy.**
2. Gerar o certificado do Data Protection e a chave AES de produção; definir quem guarda e rotaciona.
3. Estratégia de migrations em produção (job de release com backup) e política de backup do PostgreSQL gerenciado.
4. Aviso de UX no frontend para rotas 503 de integrações locais (hoje mensagem genérica).
5. Cabeçalhos de segurança do SPA (CSP, `X-Content-Type-Options`, `Referrer-Policy`) não foram adicionados nesta etapa; avaliar com cuidado para não quebrar a UI.
6. Controle de versão: o diretório não é um repositório git; adotar antes do deploy (o `.dockerignore`/`.gitignore` ajudam, mas não substituem).
7. Fixar a tag da imagem do SDK/runtime (hoje `10.0`) e atualizar `global.json` ou documentar a diferença.
8. Etapa 2B (não iniciada): tabelas de tarefas/executores e a autenticação do worker continuam fora do escopo.

## 11. Worker remoto e executores (Etapa 2C)
- Workers Windows falam com a API por HTTPS de saída (`/api/v1/agent/*`); nada é exposto na máquina do worker e o PostgreSQL continua privado.
- Migrations adicionais, em ordem: `AddWorkQueue` (2B) → `AddExecutors` (2C). Não são aplicadas automaticamente.
- Variáveis novas (opcionais): `WorkQueue__RecoveryIntervalSeconds` (padrão 15; 0 desliga), `WorkQueue__ExecutorOnlineSeconds` (60), `WorkQueue__ExecutorHeartbeatSeconds` (20), `WorkQueue__ExecutorMaxClaim` (5), `Security__ExecutorPermitLimit` (600/min por executor).
- Com várias réplicas da API a recuperação de leases é segura (`SKIP LOCKED`). Rate limit de executor é por credencial, mas o limitador global continua por IP: configure `ReverseProxy` corretamente.
- Credenciais de executor são emitidas só por Admin (tela Tarefas → Executores). Ver `docs/etapa-2c-report.md`.

## 12. Segurança do canal de serviço (Etapa 2C.1)
- Credenciais de serviço inválidas (executores e chave do n8n) são freadas por cliente: `Security__AuthFailureLimit` (10) por `Security__AuthFailureWindowSeconds` (60) → `429 + Retry-After`, sem consultar o banco. O cliente é o IP real **somente** se `ReverseProxy` estiver correto; valide com o passo 3 da seção 4 de `docs/HTTPS_HOMOLOGATION.md`.
- O canal `/api/v1/agent` (Bearer) não divide o limite global por IP com as pessoas; tem cota própria por executor.
- Migrations adicionais em ordem: `AddDataProtectionKeys` → `AddWorkQueue` → `AddExecutors` → `AddClaimKey` (todas aditivas; revise os scripts em `docs/security/`).
- `WorkQueue__RecoveryIntervalSeconds` deve ficar entre 1 e 3600 em produção (0 desliga a recuperação e deixa tarefas de executores mortos presas); valores inválidos impedem a API de iniciar.

## 13. Atualizações da Etapa 2D (Fase 1) — leia antes da seção 3 e da seção 4
- **`AllowedHosts` precisa incluir `healthcheck.railway.app`** (host usado pelo health check da plataforma), além do domínio público; sem isso o deploy é reprovado com 400.
- **Porta:** a API lê `PORT` sozinha (`http://*:PORT`); o `ENTRYPOINT` é exec (`dotnet CentralContingencia.Api.dll`). Não defina `ASPNETCORE_URLS` no Railway.
- **Health check:** `railway.json` usa `/health/ready`; `/health*` não é redirecionado para HTTPS.
- **Proxy confiável:** a seção 4 acima ("confirme com a documentação/suporte") foi investigada: a documentação oficial **não** publica faixas de origem; não use faixa presumida. Use a sonda `/diagnostics/network` (token temporário) para medir e siga `docs/etapa-2d-predeploy.md`, seção 5.
- **Migrations:** `dotnet CentralContingencia.Api.dll --migrate [--plan]` (exige `CENTRAL_MIGRATE_CONFIRM=<id da última migration>`; recusa script destrutivo sem `CENTRAL_MIGRATE_ALLOW_DESTRUCTIVE=YES`). Preferir como *Pre-deploy command* configurado na UI.
- Plano completo, variáveis, rollback e bloqueios: `docs/etapa-2d-predeploy.md`.
