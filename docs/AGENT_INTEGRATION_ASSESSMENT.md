# Diagnóstico de integração — CentralContingencia + AgenteAdsPower (Etapa 1)

Data: 21/10/2026. Natureza: análise somente leitura. Nenhum código, migration, banco ou configuração foi alterado.
Nenhum segredo foi lido ou transcrito (valores de chaves, hashes e connection strings foram omitidos).

> Limite da análise: o repositório do **AgenteAdsPower não está neste workspace**. Tudo o que se diz dele vem do enunciado
> (skills executadas pelo Claude no VS Code) e do prompt versionado em `docs/warmup/verification-prompt.md`. Ver seção 11.

---

## 1. Arquitetura atual identificada

Monólito modular (Domain → Application → Infrastructure → Api) + SPA React, conforme `CLAUDE.md`.

```
Navegador ──HTTPS/cookie+CSRF──▶ Api (.NET 10, 0.0.0.0:7090)  ──EF/Npgsql──▶ PostgreSQL 17 (Docker, 127.0.0.1:55432)
  (Vite proxy /api → 7090)            │
                                      ├─ HTTP http://127.0.0.1:50325 ──▶ AdsPower Local API (somente leitura: grupos/perfis)
                                      ├─ HTTP(S) localhost/host.docker.internal ──▶ n8n local (webhook de ingestão de TXT)
                                      └─ ◀── n8n local (Bearer, chave de serviço) ── /api/v1/integration/profile-ingestions/*
```

Fatos relevantes:

- **O backend fala com o AdsPower apenas por `AdsPowerLocalApiClient`**, e só aceita `BaseUrl` http com host loopback
  (`ResolveBase`, `AdsPowerLocalApiClient.cs:76-81`; falha `NotConfigured` caso contrário). Isto é uma **trava deliberada**
  e é incompatível com Railway (a API na nuvem não alcança o AdsPower).
- A chave da API local do AdsPower entra por `AdsPower:ApiKey` (variável `AdsPower__ApiKey`), injetada em desenvolvimento por
  `scripts/Set-LocalAdsPower.ps1`, que descriptografa um arquivo DPAPI em `.local/secrets/`.
- O resultado do dashboard é cache em memória singleton (5 min); **nada do AdsPower é persistido**.
- O backend **não** abre nem fecha perfis do AdsPower. Só lê (`/dashboard`) e numera perfis (ingestão).
- **"Aquecimento" hoje é só registro**: o backend guarda o resultado de uma verificação (`profiles.HealthChecks`, ADR 0015); quem
  executa a verificação é o Claude/VS Code com o prompt copiado pela tela `/warmup`. **Não existe fila de tarefas, executor,
  heartbeat nem estado de execução.**
- Autenticação: Identity com cookie `__Host-Central.Session` (Secure/HttpOnly/Strict, 30 min, sem sliding) + antiforgery
  `__Host-Central.Csrf`. Integrações: esquema `IntegrationKey` (Bearer, hash SHA-256 em configuração, comparação em tempo constante, escopos por chave).
- Auditoria: tabelas no schema `audit` (`OperationalEvents`, `ProxyEvents`, `AuthenticationEvents`, `ProxyImport*`).
- Schemas PostgreSQL: `identity`, `audit`, `profiles` (inclui `IngestionBatches`, `HealthChecks`), `proxies`. 12 migrations; a última
  (`20261016175350_AddProfileHealthChecks`) está **gerada e não aplicada** segundo o CLAUDE.md.

## 2. Arquivos e componentes relevantes

| Tema | Caminho |
|---|---|
| Composition root, auth, CSRF, rate limit, HTTPS | `src/CentralContingencia.Api/Program.cs` |
| Leitura AdsPower (cliente HTTP) | `src/CentralContingencia.Infrastructure/AdsPower/AdsPowerLocalApiClient.cs` |
| Dashboard AdsPower (cache, contratos) | `src/CentralContingencia.Application/AdsPower/*`, `Api/AdsPower/AdsPowerEndpoints.cs` |
| Verificação/"aquecimento" | `Application/Warmup/{HealthCheckService,WarmupContracts}.cs`, `Api/Warmup/WarmupEndpoints.cs`, `Domain/Profiles/ProfileHealthCheck.cs`, `Infrastructure/Profiles/EfHealthCheckStore.cs`, `Persistence/Configurations/ProfileHealthCheckConfiguration.cs` |
| Prompt da verificação | `docs/warmup/verification-prompt.md`, `docs/decisions/0015-warmup-health-verification.md` |
| Chave de serviço (Bearer) | `Api/Integration/IntegrationKeyAuthentication.cs` |
| Ingestão para n8n (reserva/confirm/release) | `Api/Integration/IngestionEndpoints.cs`, `Application/Ingestion/*`, `Infrastructure/Allocations/EfIngestionStore.cs`, `Domain/Profiles/ProfileIngestionBatch.cs` |
| Central → n8n (webhook, 1 envio por vez) | `Api/Integration/ProfileUploadEndpoints.cs` (`N8nIngestionOptions`, `UploadGate`, `N8nIngestionClient`) |
| Segredos locais / DPAPI | `scripts/Save-LocalAdsPowerKey.ps1`, `Set-LocalAdsPower.ps1`, `Set-LocalProxyEncryption.ps1`, `Set-LocalIntegration.ps1`, `Set-LocalN8nWebhook.ps1`, `Start-LocalApiFull.ps1`, pasta `.local/secrets/` |
| Criptografia de proxy (AES-256-GCM) | `Infrastructure/Proxies/AesGcmProxyPasswordProtector.cs` |
| Docker | `docker-compose.yml` (só PostgreSQL) |
| n8n | `docs/n8n/*.json`, `docs/n8n/README.md` |
| Frontend | `src/central-contingencia-web/src/features/{warmup,dashboard,ingestion,profiles,proxies}`, `src/lib/api/client.ts`, `components/layout/sheets.ts` |
| Docs de contrato | `docs/api/*.md` (ex.: `profile-ingestion.md`, `adspower-dashboard.md`), `docs/decisions/0013`, `0015` |

## 3. Recursos reutilizáveis

1. **Esquema `IntegrationKey` + política por escopo** (`IntegrationAuth`): base natural da credencial do worker. Basta um novo
   escopo (ex.: `agent-worker`) e política própria; rate limit `integration` e metadado `IntegrationEndpointMetadata` (isenta CSRF) já existem.
2. **Padrão de idempotência** (`Idempotency-Key` UUID, replay seguro) e de leitura manual de corpo com limites e 400/413/415 (`ReadBodyAsync`).
3. **Padrão transação + índice único parcial + auditoria atômica** de `ProxyAllocation`/`EfIngestionStore` — modelo para a reserva de tarefas
   (`SELECT … FOR UPDATE SKIP LOCKED` ou índice único parcial "uma tarefa ativa por perfil").
4. **TTL de reserva com recuperação transacional sob demanda** (ADR 0010): mesmo desenho serve para *lease* de tarefa (expira sem heartbeat).
5. **`HealthCheckService`/`ProfileHealthCheck`**: regras de esteira de 7 dias, 1 por dia UTC, enum de resultados e índices únicos já prontos.
   O worker passaria a **chamar o mesmo caso de uso** em vez de o operador registrar à mão.
6. **Leitura de perfis do AdsPower** (`IAdsPowerProfileSource`) e `GET /api/v1/adspower/dashboard`: continua útil para a UI, mas só enquanto a API rodar na máquina do AdsPower (ver riscos).
7. **Tela `/warmup`** (seleção de até 20 perfis, progresso da esteira): evolui para "enfileirar" em vez de "copiar prompt".
8. **Frontend**: TanStack Query, `lib/api/client.ts` (cookie + CSRF), `sheets.ts` (menu), padrão de testes Vitest/Testing Library.
9. **Testes de integração com PostgreSQL real** (`AuthFixture`, banco descartável por `CENTRAL_TEST_POSTGRES`, apenas localhost).

## 4. Dependências e riscos técnicos

### 4.1 Bloqueios para o Railway (precisam de decisão, não são bugs)

| # | Achado | Evidência | Impacto |
|---|---|---|---|
| R1 | **Sem Dockerfile/`railway.json`/Nixpacks** e docker-compose só tem PostgreSQL | raiz do repo | Deploy exige empacotar API e web |
| R2 | **Exigência de HTTPS por `Request.IsHttps` sem `UseForwardedHeaders`** | `Program.cs` (middleware 400 "Esta API exige HTTPS") | Atrás do proxy do Railway (TLS terminado no edge) toda chamada `/api/v1` receberia **400**. Cookies `__Host-`/Secure também dependem disso |
| R3 | **Data Protection sem persistência de chaves** (`AddDataProtection().SetApplicationName(...)`) | `Program.cs:23` | Em contêiner efêmero, cada deploy/restart invalida cookies de sessão, tokens antiforgery e provas de importação CSV (15 min). Precisa de key ring no PostgreSQL ou volume |
| R4 | **API não serve o SPA** (sem `UseStaticFiles`/fallback); web usa proxy do Vite para `/api` | `vite.config.ts`, `Program.cs` | São dois serviços (ou hospedagem estática). Cookies `SameSite=Strict` + `__Host-` exigem **mesma origem**: ou domínio único com roteamento, ou servir o SPA pela API |
| R5 | **`AdsPower:BaseUrl` limitado a loopback e n8n `WebhookUrl` limitado a localhost/`host.docker.internal`** | `AdsPowerLocalApiClient.ResolveBase`, `N8nIngestionOptions.Validate` | Na nuvem, `/adspower/dashboard`, a numeração da ingestão (lê o grupo no AdsPower) e o envio de TXT ao n8n **deixam de funcionar** — por desenho |
| R6 | **Ingestão depende do AdsPower local para numerar** (`ReadGroupAsync` falha → 503) | `IngestionEndpoints.Execute` | Mesma causa; precisa passar a vir do worker (snapshot) ou ser tarefa do worker |
| R7 | **Kestrel `MaxRequestBodySize = 16 KB`** e URLs fixas (`0.0.0.0:7090`) em scripts | `Program.cs`, `Start-LocalApiFull.ps1` | Railway define `PORT`; é preciso `ASPNETCORE_URLS=http://0.0.0.0:$PORT` |
| R8 | **Migrations manuais** (`scripts/Migrate-LocalDatabase.ps1`, SQL em `docs/security/`); nada aplica no deploy | scripts | Definir estratégia (job de release com backup), respeitando "migração destrutiva só com autorização" |
| R9 | **OpenAPI só em Development** (`MapOpenApi`) | `Program.cs` | Ok para produção; documentar contrato do worker em `docs/api/` |
| R10 | `/health` não verifica PostgreSQL | `Program.cs` | Healthcheck do Railway passaria com banco fora; criar `/health/ready` |

### 4.2 Segredos, arquivos locais, DPAPI

- **DPAPI é só ferramenta de desenvolvimento** (scripts PowerShell); o **código da API não usa DPAPI** — consome variáveis de ambiente
  (`ProxyEncryption__Keys__*`, `AdsPower__ApiKey`, `Integration__Keys__*`, `N8nIngestion__WebhookKey`). Boa notícia: no Railway basta
  usar *variables*. Os arquivos `.local/secrets/*.dpapi` **não são portáveis** (atrelados ao usuário/máquina Windows) — serão
  re-provisionados, não migrados. **A chave de criptografia de proxy de desenvolvimento (`development-v1`) não deve ser reutilizada em produção**; produção precisa de `KeyId` próprio, e a rotação precisa ser exercitada antes.
- **Chave do AdsPower foi exposta em conversas** (CLAUDE.md, Etapa 2.8) e o usuário planeja trocá-la. A troca deve ocorrer **antes** de
  o worker usá-la. O worker deve ter a sua própria chave local (DPAPI/Credential Manager do Windows), nunca vinda da Central.
- **`.local/`, `.env`, `.data-protection/` estão no `.gitignore`**, mas o repositório **não é git** neste workspace (`git: false`),
  logo não há proteção prática contra commit acidental nem histórico. `.local/` contém backups do banco, SQL e snapshots — risco de
  vazamento se a pasta for copiada/zipada para deploy. `.npm-cache` e `dist/` também ficam na raiz.
- **Chaves de integração ficam em configuração (hash estático)**: sem cadastro, rotação, revogação por executor ou "last used". Para um
  worker isso é insuficiente (ver seção 6).
- **A resposta da reserva de ingestão devolve senha de proxy** (decisão do usuário, ADR 0013). Num worker remoto isto passa a trafegar fora da máquina local → TLS obrigatório, retenção zero nos logs do worker e da plataforma.
- Segurança correta no envio de TXT: a Central repassa **em memória** logins/2FA ao n8n; **a Central nunca deve armazenar esse conteúdo** e o worker não deve recebê-lo por tarefa persistida em banco.
- `AllowedHosts: "*"` e CORS não configurado (mesma origem hoje) — ajustar em produção.
- Rate limit global por IP (`RemoteIpAddress`): atrás de proxy reverso todos os clientes teriam o IP do proxy até configurar `ForwardedHeaders`.

### 4.3 Riscos de domínio/operacionais

- **O backend não valida `adsPowerProfileId` de uma verificação contra o AdsPower** (ADR 0015): quem tem Management registra qualquer id. Com worker, o
  registro deve vir **vinculado a uma tarefa existente e a um executor autenticado** (impede resultado forjado sem execução).
- **Resultado do worker ≠ ação do operador**: `ProfileHealthCheck.ActorId` espera um usuário Identity. Executor com chave de serviço tem `NameIdentifier` = Id da chave. Decidir se `ActorId` aceita o id do executor (coluna sem FK ao Identity?) ou ganha `ExecutorId` próprio.
- **Escopo de conduta**: o prompt versionado (`verification-prompt.md`) ainda contém a "Etapa 2 — Sessão de navegação" (rolagem, vídeos, Reels, curtidas),
  que **contradiz a ADR 0015/CLAUDE.md** (verificação de uma página, sem interação, sem sessão cronometrada). Isso precisa ser reconciliado **antes** de o worker materializar o fluxo: o worker só deve implementar o escopo da ADR 0015 (abrir → carregar uma página → classificar → registrar; deixar aberto se impedimento; fechar se Ok). Esta avaliação **não propõe** nenhuma automação para contornar CAPTCHA, checkpoint ou verificação — o contrato proposto exige parar e reportar.
- Concorrência: `UploadGate` é um semáforo em memória (um envio por vez); com mais de uma instância da API deixa de valer. Tarefas devem usar exclusão no banco, não em memória.
- Cache do dashboard e `UploadGate` são **estado em processo**: escala horizontal na Railway quebra ambos.

## 5. Arquitetura de integração proposta (mínima)

Princípio: **o worker faz *pull* (conexões de saída apenas)**. Nenhuma porta é aberta na máquina do AdsPower; a Central nunca chama `localhost` do operador. Isso resolve a restrição de Railway e a futura VM Windows (só muda onde o processo roda).

```
Operador (SPA) ──▶ Central (Railway) ──▶ tabela agent.Tasks (fila durável)
                                              ▲   │
   Worker Windows (serviço, sem VS Code)  ────┘   │  HTTPS de saída + Bearer (chave do executor)
        1 registro  2 heartbeat  3 claim (lease)  4 progresso  5 resultado
        │
        └──▶ AdsPower Local API (127.0.0.1:50325, chave local do worker)  →  abre perfil / carrega 1 página / classifica / fecha
```

Componentes mínimos:

1. **Domínio/Application (novo contexto `Agents`)**: `AgentExecutor`, `AgentTask`, `AgentTaskEvent`; casos de uso `RegisterExecutor`, `Heartbeat`, `ClaimTask`, `ReportProgress`, `CompleteTask`, `FailTask`, `CancelTask`, `RequeueExpired`.
2. **Persistência** (schema novo `agents`, migration aditiva): `Executors` (Id, Name, KeyHash, Status, LastSeenAt, Version, Capabilities), `Tasks` (Id, Type, ProfileRef, State, ExecutorId?, LeaseExpiresAt, Attempt, Result, CreatedBy, CreatedAt…), `TaskEvents`. Índice único parcial "no máximo uma tarefa ativa por perfil e tipo"; `UNIQUE (ProfileRef, DataUtc)` para verificação diária.
3. **Autenticação do worker**: nova política `AgentWorker` com escopo `agent-worker`, chave **por executor** guardada como hash em tabela (não em configuração), revogável.
4. **Tipos de tarefa (inicial)**: `HealthCheck` (ADR 0015) e, depois, `ProfileProvision` (ingestão). Sem tipo genérico "executar script".
5. **Worker (.NET Worker Service como serviço do Windows)**, repositório próprio: loop `heartbeat` + `claim` com *backoff*; chave do AdsPower local protegida por DPAPI/Credential Manager; **lista fechada de operações** no AdsPower (start/stop/leitura) — nunca executa instruções arbitrárias vindas da Central.
6. **UI**: submenu Aquecimento passa a enfileirar e acompanhar; nova visão de executores.

## 6. Contratos de API sugeridos

Prefixo `/api/v1/agent` (executor, Bearer) e `/api/v1/agent-tasks` (operadores, cookie+CSRF). JSON, ProblemDetails, UTC, `Idempotency-Key` nas mutações do worker. **Rascunho para aprovação; nada existe ainda.**

### 6.1 Worker → Central

**`POST /api/v1/agent/executors/register`** — uma vez, com chave de registro de uso único emitida pelo Admin.
```json
// req
{ "name": "win-adspower-01", "machineFingerprint": "<hash opaco>", "workerVersion": "0.1.0",
  "adsPowerVersion": "x.y.z", "capabilities": ["health-check"] }
// 201
{ "executorId": "uuid", "credential": "<exibida uma única vez>", "heartbeatSeconds": 30, "leaseSeconds": 120 }
```
**`POST /api/v1/agent/heartbeat`**
```json
{ "executorId": "uuid", "status": "Idle|Busy|Degraded", "adsPowerReachable": true,
  "currentTaskId": "uuid|null", "workerVersion": "0.1.0" }
// 200 { "serverTime": "...", "cancelTaskIds": ["uuid"], "minWorkerVersion": "0.1.0" }
```
**`POST /api/v1/agent/tasks/claim`** — *lease* atômico (`FOR UPDATE SKIP LOCKED`).
```json
// req  Idempotency-Key: uuid
{ "executorId": "uuid", "types": ["HealthCheck"], "max": 1 }
// 200 (ou 204 sem tarefa)
{ "task": { "id": "uuid", "type": "HealthCheck", "attempt": 1, "leaseExpiresAt": "...",
            "payload": { "adsPowerProfileId": "k1abc", "profileName": "Perfil 12", "adsPowerGroupId": "g1", "cycleDay": 3 } } }
```
O payload **não contém credenciais**. Perfil e dia vêm da Central; o worker só obtém o que precisa.

**`POST /api/v1/agent/tasks/{id}/progress`** — estende o lease. `{ "executorId", "step": "ProfileOpened|PageLoaded|…", "at": "..." }`

**`POST /api/v1/agent/tasks/{id}/result`** — `Idempotency-Key`; só o executor dono do lease.
```json
{ "executorId": "uuid", "outcome": "Ok|Captcha|VerificationRequested|LoginRequired|IdentityConfirmation|Checkpoint|AccountRestriction|NavigationError",
  "profileLeftOpen": true, "startedAt": "...", "finishedAt": "...", "workerMessage": "texto curto sanitizado" }
// 200 grava ProfileHealthCheck pelo caso de uso existente; 409 se lease expirou/já concluída
```
**`POST /api/v1/agent/tasks/{id}/fail`** — `{ "executorId", "reason": "AdsPowerUnreachable|ProfileNotFound|Timeout|InternalError", "retryable": true }`. `retryable` só vale para falha de infraestrutura; **impedimento de plataforma nunca é "retryable"**.

### 6.2 Central → Operador (SPA)

- `POST /api/v1/agent-tasks` (Admin/Manager) `{ "type":"HealthCheck", "profiles":[{adsPowerProfileId,…}] }` — até 20 por pedido, mesma regra da tela atual; recusa 409 se o perfil já tem verificação no dia ou tarefa ativa.
- `GET /api/v1/agent-tasks?state=&profile=&page=` · `GET /api/v1/agent-tasks/{id}` · `POST /api/v1/agent-tasks/{id}/cancel`
- `GET /api/v1/agent-executors` (Admin) · `POST /api/v1/agent-executors/registration-tokens` · `POST /api/v1/agent-executors/{id}/revoke`

### 6.3 Estados

`Queued → Leased → Running → Succeeded | Failed | Cancelled | Expired`. Lease sem heartbeat/progresso vence e volta a `Queued`
**somente** se `retryable`; tarefa interrompida com perfil possivelmente aberto vai para `NeedsReview` (humano decide), nunca reexecuta sozinha.

## 7. Alterações necessárias por camada

| Camada | Alterações |
|---|---|
| **Domain** | `AgentExecutor`, `AgentTask` (máquina de estados, lease, tentativas), regras de transição; enum de tipos. Reusar `HealthCheckOutcome`. |
| **Application** | Casos de uso do §5.1; `ClaimTask` e `CompleteTask` transacionais; `CompleteTask` chama `IHealthCheckService.RecordAsync` (ajustar quem é o ator). Portas `IAgentStore`. |
| **Infrastructure** | Schema `agents` + migration aditiva; `EfAgentStore`; **key ring do Data Protection no PostgreSQL**; rotação de chaves do executor. |
| **Api** | Grupos `/agent` e `/agent-tasks`; política `AgentWorker`; `UseForwardedHeaders`; `/health/ready`; limites de corpo por rota; OpenAPI dos novos contratos. |
| **Frontend** | Ver §8. |
| **Deploy** | Dockerfile da API (e decisão sobre servir o SPA), variáveis Railway, job de migration, domínio único/TLS. |
| **Dashboard/Ingestão (R5/R6)** | Decidir fonte do AdsPower na nuvem: o worker publica *snapshots* sanitizados (grupos/perfis) via API, substituindo a leitura direta. |
| **Docs** | ADR 0016 (worker pull), `docs/api/agent-tasks.md`, atualização do `verification-prompt.md` ao escopo da ADR 0015. |

## 8. Frontend — monitoramento de tarefas

- `warmup/WarmupPage.tsx`: trocar "copiar prompt" por "Enfileirar verificação"; manter a seleção (≤20) e o progresso da esteira; mostrar estado da tarefa por linha.
- Nova listagem de tarefas (TanStack Table, filtros e paginação no servidor): estado, perfil, executor, tentativa, resultado, horário. Atualização por *polling* via TanStack Query (`refetchInterval` curto) — SSE/WebSocket fica fora do MVP.
- Painel de executores (Admin): online/offline por `LastSeenAt`, versão, AdsPower acessível, revogar. Estado "executor offline" visível na tela de Aquecimento para não parecer que a fila anda.
- Estados de loading/erro/vazio; `sheets.ts` ganha o item "Executores" (somente Admin); rotas e cliente HTTP/CSRF atuais permanecem.
- Impedimentos exigem ação humana: destacar `NeedsReview` e "perfil deixado aberto na máquina X" (o operador precisa saber onde olhar).
- Dados do worker mostrados no navegador passam por sanitização no backend (nunca páginas, cookies ou capturas).

## 9. Testes existentes aproveitáveis

- Backend: ~162 casos `[Fact]/[Theory]` (xUnit). `AuthFixture` + `AuthFactory` (`WebApplicationFactory<Program>`, banco descartável por teste, só localhost, requer `CENTRAL_TEST_POSTGRES`) servem de base para testes dos endpoints do worker. Reaproveitar `IngestionEndpointTests` (chave de serviço, escopo, idempotência), `ReservationReliabilityTests` (TTL/concorrência) e `AllocationPersistenceTests` (índice único parcial), `AdsPowerEndpointTests`/`AdsPowerTests` (fake do AdsPower).
- Frontend: Vitest + Testing Library (`WarmupPage.test.tsx`, `DashboardPage.test.tsx`, `IngestionPage.test.tsx`, `ProfilesPage.test.tsx`, `OperationalGridPage.test.tsx`) com mocks; scripts Playwright em `scripts/Verify-*.cjs`.
- Faltará: testes de concorrência de *claim* (N workers disputando), expiração de lease, replay idempotente de resultado, autorização cruzada (executor A não conclui tarefa de B), e contrato do worker contra um **AdsPower fake**. Não executei a suíte nesta etapa (fora do escopo e exigiria banco).

## 10. Plano de implementação incremental

| Etapa | Entrega | Critérios de aceitação |
|---|---|---|
| **0 — Pré-requisitos** (sem código de feature) | Decidir §11; alinhar `verification-prompt.md` à ADR 0015; trocar a chave do AdsPower; ADR 0016 | Escopo do worker aprovado por escrito; prompt sem etapas de interação; chave anterior revogada |
| **1 — Domínio de tarefas/executores** | Domain + Application + testes unitários, sem persistência | Máquina de estados e lease testadas; nenhuma dependência de EF/HTTP |
| **2 — Persistência** | Migration aditiva `agents`, `EfAgentStore`, índices únicos parciais | Migration só em banco descartável; claim concorrente com N threads entrega 1 vencedor; backup/aprovação antes de aplicar em homologação |
| **3 — API do worker** | `/agent/*` + política `AgentWorker` + chaves por executor | Chave inválida/revogada = 401; escopo errado = 403; replay idempotente; payload sem credenciais; OpenAPI atualizado |
| **4 — API de operadores + UI** | `/agent-tasks`, `/agent-executors`, telas | Admin/Manager enfileiram, Operator só lê; estados loading/erro/vazio; executor offline sinalizado |
| **5 — Worker mínimo (repo próprio)** | Serviço Windows com heartbeat, claim e `HealthCheck` contra AdsPower local | Roda sem VS Code; reinicia sem perder lease indevido; para ao detectar impedimento e deixa o perfil aberto; fecha em `Ok`; chave local protegida |
| **6 — Endurecimento para Railway** | Dockerfile, `ForwardedHeaders`, key ring no banco, `/health/ready`, variáveis, migration como job | Deploy em ambiente de teste Railway sem 400 de HTTPS; sessão sobrevive a restart; rate limit por IP real; nenhum segredo no repositório |
| **7 — Dashboard/ingestão via worker** | Snapshot de perfis publicado pelo worker; `ProfileProvision` como tarefa | Dashboard funciona sem acesso da Central ao `localhost`; numeração igual à atual no grupo ativo |
| **8 — VM Windows** | Instalação do worker na VM | Mesmo binário, só configuração; AdsPower com licença/instância na VM validado |

Cada etapa só começa após autorização explícita (regra do CLAUDE.md); nenhuma inclui commit, deploy ou dados reais por padrão.

## 11. Dúvidas técnicas não resolvidas

1. **Repositório AgenteAdsPower**: não está disponível aqui. Como as skills abrem o perfil hoje (API local `browser/start`, Playwright/CDP via `ws`, AdsPower IA Agent)? A ADR 0015 deixou isso "a definir". Isso decide se o worker reimplementa ou reaproveita código.
2. **Escopo do worker**: confirmar que o worker implementa **apenas** a verificação de uma página (ADR 0015) e que a "Etapa 2 — Sessão de navegação" do prompt versionado deve ser **removida**. Interações com a rede social (curtir, rolar, Reels) não fazem parte do que estou propondo.
3. **Quem é o "ator" do registro**: `ActorId` aceita executor (sem FK ao Identity) ou criamos `ExecutorId`? Impacta a migration já gerada e **não aplicada** de `HealthChecks` — vale ajustá-la antes de aplicá-la.
4. **Hospedagem do SPA** no Railway: servido pela API (mesma origem, mantém `SameSite=Strict`/`__Host-`) ou serviço separado com domínio comum?
5. **Ingestão e dashboard na nuvem**: ficam via worker (§7) ou permanecem só locais até a V2? O n8n continuará local ou irá ao Railway (muda a regra "WebhookUrl só localhost")?
6. **Um ou vários workers**: uma única VM com um AdsPower; há limite de perfis abertos simultaneamente? Concorrência desejada por executor.
7. **Retenção e privacidade**: o que o worker pode enviar além da categoria (mensagem curta)? Recomendo nenhum conteúdo de página, cookie ou captura de tela.
8. **Política de impedimento**: quem é notificado, em quanto tempo, e como o operador localiza o perfil aberto na máquina/VM (RDP)?
9. **Protocolo de transporte do heartbeat**: HTTPS *polling* (recomendado, mais simples) vs. SignalR; lease de 120 s e heartbeat de 30 s são sugestões, não medidas.
10. **Estratégia de migrations em produção** e de backup no Railway (RPO/RTO).
11. **Repositório não-git**: adotar controle de versão antes de qualquer deploy (hoje não há histórico nem proteção real do `.gitignore`).
12. **Verificação não executada**: não rodei `dotnet build/test` nem o frontend nesta etapa; o estado de build/testes atual não foi reconfirmado.
