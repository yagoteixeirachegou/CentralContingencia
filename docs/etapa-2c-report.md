# Etapa 2C — Canal HTTPS entre a Central e o worker Windows (relatório)

Data: 21/10/2026. Escopo: somente `SimulatedProcessing`. Sem deploy, sem migration aplicada em banco existente, sem credenciais reais, sem AdsPower/Claude/navegador/redes sociais.
Decisões: ADR 0018 (complementa a 0017). Leitura prévia: relatório 2B, `AGENT_INTEGRATION_ASSESSMENT.md`, migration 2B, CLAUDE.md, AGENTS.md, ADRs 0013/0015/0016/0017.

## 1. Arquitetura implementada
```
Windows (worker)                                   Central (API ASP.NET Core)                             PostgreSQL
 WorkerService ── HttpWorkQueueClient ──HTTPS──▶  /api/v1/agent/*  (esquema ExecutorKey, Bearer)  ──▶  work.Tasks / work.Executors
  TaskRunner      (HttpClientFactory,             ExecutorKeyHandler → IExecutorGateway                 audit.WorkTaskEvents / ExecutorEvents
  executor        timeout, jitter, Retry-After)   IWorkQueueService (mesma máquina de estados WorkTask)
  simulado                                        WorkQueueRecoveryService (leases vencidos)
Navegador (Admin/Manager/Operator) ── cookie+CSRF ──▶ /api/v1/executors, /api/v1/tasks      (telas Tarefas e Executores)
```
- **Isolamento:** o worker remoto não registra `DbContext`/stores (testado) e **recusa iniciar** se existir `ConnectionStrings:DefaultConnection`. `Worker:Mode=LocalDatabase` (acesso direto, Etapa 2B) só é aceito com `Development`.
- **Identidade:** `WorkExecutor` (UUID próprio, nome único sem distinguir caixa, tipos permitidos, hash da credencial, estado/versão/última comunicação, revogação). Tarefa guarda `LastWorkerId` (executor que a reservou), separado do `CreatedBy` (usuário).
- **Credencial:** `ce1.<ExecutorId N>.<segredo base64url 32 bytes>`; só o SHA-256 do segredo é persistido; comparação em tempo constante; conferida a cada requisição (rotação/revogação valem na hora); uma única exibição.
- **Regras reutilizadas, não duplicadas:** claim/heartbeat/complete/fail/cancel/release chamam `IWorkQueueService` → `EfWorkTaskStore` → `WorkTask`. `executorId` vem da credencial; o cliente não envia worker, status, lease nem tentativas (campos desconhecidos → 400).
- **Recuperação:** `WorkQueueRecoveryService` na API (a cada 15 s; `WorkQueue:RecoveryIntervalSeconds`, 0 desliga). No reinício, o worker devolve (`release`) o que seu processo anterior segurava, sem consumir tentativa.

## 2. Endpoints e contratos
**Gestão (cookie + CSRF; rate limit `sensitive` nas mutações)**
| Rota | Papéis | Contrato |
|---|---|---|
| `POST /api/v1/executors` | Admin | `{ name, allowedTypes? }` → 201 `{ executor, credential }` (`no-store`; credencial só aqui). Nome inválido/tipo desconhecido/campo extra → 400; nome repetido → 409 |
| `GET /api/v1/executors`, `GET /api/v1/executors/{id}` | Admin, Manager, Operator | `ExecutorView`: `id, name, status, connection (Online\|Offline\|Revoked), state, lastSeenAt, workerVersion, allowedTypes, createdAt, credentialRotatedAt, revokedAt, runningTasks`. Nunca credencial/hash |
| `POST /api/v1/executors/{id}/rotate` | Admin | 200 `{ executor, credential }`; antiga invalidada na hora; revogado → 409 |
| `POST /api/v1/executors/{id}/revoke` | Admin | 200 `ExecutorView`; definitivo e idempotente |
| `/api/v1/tasks` (2B) | — | resposta ganhou `executorId`, `executorName`, `lastActivityAt` |

**Canal do executor (`Authorization: Bearer ce1…`, sem cookie/CSRF; cota 600/min por executor)**
| Rota | Corpo → resposta |
|---|---|
| `GET /api/v1/agent/config` | → `{ executorId, name, allowedTypes, heartbeatSeconds, leaseSeconds, maxClaim, serverTime }` |
| `POST /api/v1/agent/heartbeat` | `{ state: Idle\|Busy\|Draining, workerVersion? }` → `{ serverTime }` (atualiza presença) |
| `POST /api/v1/agent/tasks/claim` | `{ types?, max? }` (≤ `maxClaim`) → `{ tasks: [{ id, type, payloadVersion, payload, attempt, maxAttempts, correlationId, leaseUntil }] }`; interseção com os tipos permitidos |
| `GET /api/v1/agent/tasks/held` | → tarefas com lease vigente deste executor |
| `POST /api/v1/agent/tasks/{id}/heartbeat` | `{ attempt }` → `{ cancelRequested }` (renova o lease) |
| `GET /api/v1/agent/tasks/{id}/cancellation?attempt=` | → `{ cancelRequested }` (só leitura) |
| `POST /api/v1/agent/tasks/{id}/complete` | `{ attempt, result? }` (≤ 4 KB) → 204; repetição idêntica → 204 |
| `POST /api/v1/agent/tasks/{id}/fail` | `{ attempt, code, message?, transient }` → `{ status }`; código/mensagem sanitizados; repetição idêntica → mesma resposta |
| `POST /api/v1/agent/tasks/{id}/cancelled` · `…/release` | `{ attempt }` → 204 |
| `POST /api/v1/agent/shutdown` | → 204; devolve tudo o que segura e marca Offline |
Erros em ProblemDetails com `code`: `401` (credencial ausente/inválida/revogada — resposta idêntica, sem dizer o motivo), `400 Validation`, `404 NotFound`, `409 LeaseLost|Conflict`, `413`, `415`, `429` (+`Retry-After`), `503 Unavailable` (+`Retry-After: 5`; indisponibilidade do banco nunca vira 401). OpenAPI documenta o esquema `ExecutorKey` e omite o cabeçalho CSRF nessas rotas.

## 3. Arquivos
**Novos** — Domain: `WorkQueue/WorkExecutor.cs` (executor, estados, `ExecutorEvent`). Application: `WorkQueue/ExecutorContracts.cs`, `ExecutorServices.cs` (credenciais, administração, gateway). Infrastructure: `WorkQueue/EfExecutorStore.cs`, migration `20261021205430_AddExecutors`. Api: `WorkQueue/{ExecutorAuthentication,AgentEndpoints,ExecutorAdminEndpoints,WorkQueueRecoveryService}.cs`.
Worker: `HttpWorkQueueClient.cs`, `WorkerComposition.cs`. Frontend: `features/tasks/{api.ts,TasksPage.tsx,ExecutorsPage.tsx}` + testes. Scripts: `Save-WorkerCredential.ps1`, `Start-Worker.ps1`. Testes: `ExecutorTests.cs` (unit), `AgentChannelTests.cs`, `RemoteWorkerEndToEndTests.cs` (integração). Docs: este relatório, ADR 0018, `docs/security/etapa-2c-executors-migration.sql`.
**Alterados** — `WorkTask` (`LastWorkerId` + métodos de repetição idempotente), `WorkQueueOptions` (presença do executor), `WorkQueueContracts` (`Unauthorized`, `RetryAfter`, `HeldAsync`, `IsCancelRequestedAsync`), `WorkTaskService` (resposta com executor), `EfWorkTaskStore`, `DbContext`/`DependencyInjection`, `Program.cs` da API (esquema, política, cota, rotas, serviço de recuperação, OpenAPI), `IngestionEndpoints.ReadBodyAsync` (limite parametrizável), `WorkerService`/`TaskRunner`/`WorkerOptions`/`Program` do worker, `appsettings.json` do worker, frontend (`App.tsx`, `AuthenticatedLayout.tsx`, `trail.ts`, `test/setup.ts`), testes da 2B ajustados à nova assinatura, `CLAUDE.md`, `README.md`.
Mudança de comportamento a anotar: **o worker agora é Remote por padrão**; para o modo da 2B use `Worker__Mode=LocalDatabase` e `DOTNET_ENVIRONMENT=Development`.

## 4. Provisionamento de executores
1. Admin abre **Tarefas → Executores → Novo executor** (ou `POST /api/v1/executors`) e informa um nome (ex.: `win-adspower-01`). Tipos padrão: `SimulatedProcessing`.
2. A tela mostra a credencial **uma vez** (memória da tela; nada em storage/URL/log). Copiar → guardar no cofre do worker. Se perder: **Rotacionar** (a anterior morre na hora).
3. No Windows do worker: `scripts\Save-WorkerCredential.ps1` (entrada oculta; DPAPI do usuário; arquivo em `%LOCALAPPDATA%\CentralContingencia`, fora do repositório) e `scripts\Start-Worker.ps1 -ApiUrl https://<central>`.
4. Substituição/decomissionamento: **Revogar** (definitivo). Para voltar, provisiona-se outro executor. Toda ação gera evento em `audit.ExecutorEvents` (sem segredo).
Não há registro público: só Admin autenticado (cookie + CSRF) emite credenciais.

## 5. Configuração do worker Windows
Variáveis (ou `appsettings.json`/`Worker__*`): `Worker__Api__BaseUrl` (HTTPS obrigatório; HTTP só para loopback em Development), `Worker__Api__Credential` **ou** `Worker__Api__CredentialFile`, `Worker__Api__RequestTimeoutSeconds` (20), `Worker__PollIntervalMs` (2000), `Worker__MaxConcurrency` (2; limitado por `maxClaim` do servidor), `Worker__HeartbeatSeconds` (20; reduzido a ≤ lease/3 conforme o servidor), `Worker__ErrorBackoffMaxSeconds` (60), `Worker__ShutdownGraceSeconds` (30).
Comportamento: handshake (`/agent/config`) antes de reservar; sem handshake não reserva; falhas → backoff exponencial limitado com jitter ±20% e `Retry-After`; credencial recusada → espera o máximo e continua tentando (não derruba o processo); heartbeat de presença separado; shutdown ordenado (`release` + `/agent/shutdown`).
Logs: só ids, tipo, tentativa e códigos; nunca payload, resultado, credencial, host ou mensagem de exceção (testado).

## 6. Resultados (21/10/2026)
| Verificação | Resultado |
|---|---|
| `dotnet build CentralContingencia.slnx` | 0 erros, 2 avisos NU1510 (conhecidos) |
| Testes unitários | **490/490** (eram 422; +68) |
| Testes de integração (PostgreSQL 17 descartável) | **366/366** (eram 319; +47), incluindo **toda a regressão da 2B** (store, API de tarefas, worker local ponta a ponta) |
| Frontend `tsc`, `eslint`, `npm run build` | sem erros |
| Frontend `vitest run --maxWorkers=2` | **224/224** (eram 213; +11). Com paralelismo total ainda há testes antigos que estouram o tempo sob carga; o timeout assíncrono dos testes foi elevado para 5 s em `test/setup.ts` (ajuda, mas não elimina a intermitência) |
| `docker build` da API | OK (a imagem inclui a API e o frontend; o worker roda no Windows e não faz parte da imagem) |
Cobertura exigida: autenticação válida/inválida (8 variações com resposta idêntica) ✔ · credencial revogada e rotacionada ✔ · isolamento entre executores (heartbeat/complete/fail/release/cancelled/cancellation/held) ✔ · claim concorrente (6 executores × 24 tarefas via HTTP) ✔ · lease vencido (relógio controlado) ✔ · heartbeat e renovação ✔ · resultado duplicado (idempotente e conflitos) ✔ · cancelamento (Pending, Running via heartbeat e consulta) ✔ · perda de conexão (curta: não interrompe; longa: lease perdido, resultado atrasado descartado, reexecução única) ✔ · recuperação após reinício ✔ · worker remoto executando tarefas simuladas (sucesso, retry, dead-letter, falha permanente) ✔ · ausência de conexão direta com o PostgreSQL (nenhum serviço de banco registrado; string de conexão recusada) ✔ · rate limit por executor e 503 em falha de banco ✔ · modo local recusado fora de Development ✔.

## 7. Teste ponta a ponta via HTTP
- **Automatizado (`RemoteWorkerEndToEndTests`)**: `WorkerService` + `HttpWorkQueueClient` + `WorkerComposition` reais, falando com a API real (`WebApplicationFactory`, pipeline HTTP completo: HTTPS, autenticação, rate limit, serialização) e PostgreSQL real; um `DelegatingHandler` liga/desliga a "rede". 8 cenários (todos passam): resultados completos, cancelamento, desligamento ordenado, reinício, executor morto recuperado por outro, perda curta, perda longa, revogação durante o uso.
- **Processos reais separados**: API (`dotnet …Api.dll`, HTTPS em `localhost:7191`, banco descartável com as 15 migrations) e worker (`dotnet …Worker.dll`, modo Remote) subiram e o executor foi provisionado por HTTP. **O worker não completou o handshake porque o certificado de desenvolvimento do ASP.NET não é confiável neste Windows** (limitação já registrada em `etapa-1.1-report.md`); ele recusou a conexão, aplicou backoff (2 s, 5 s, …) e não vazou credencial nos logs (verificado em `worker.log` e `api.log`). Não desliguei a validação de certificado para "passar": a execução com processos reais deve ser repetida com um certificado confiável (Railway/domínio real, ou `dotnet dev-certs https --trust` autorizado por você).

## 8. Migrations geradas (não aplicadas em banco existente)
- `20261021205430_AddExecutors` — **depende de** `20261021191536_AddWorkQueue` (aplicar depois dela). Aditiva: `work."Executors"` (+CHECKs, índice único em `NormalizedName`), `audit."ExecutorEvents"` e **uma coluna anulável** `work."Tasks"."LastWorkerId"` (sem reescrita da tabela). Sem `DROP`/`ALTER … TYPE`. SQL de revisão: `docs/security/etapa-2c-executors-migration.sql` (gerado de `AddWorkQueue` → `AddExecutors`).
- Ordem completa pendente para produção: `…AddDataProtectionKeys` (2A) → `…AddWorkQueue` (2B) → `…AddExecutors` (2C). O script idempotente com todas as 15 migrations foi aplicado somente em bancos descartáveis. O banco local de desenvolvimento **não** foi tocado.

## 9. Riscos conhecidos
1. **Credencial roubada** = reserva de tarefas dos tipos permitidos até a revogação; sem mTLS, sem IP fixo; o segredo fica na memória/variável de ambiente do processo do worker (legível por quem tem o mesmo usuário Windows). Mitigação: DPAPI para armazenar, tipos mínimos, revogação imediata, auditoria.
2. **`claim` não é idempotente**: resposta perdida deixa a tarefa com o executor até o lease vencer ou o worker reiniciar (então é devolvida sem consumir tentativa).
3. Tentativas com credencial inválida **não passam pelo limitador** (a autorização responde antes dele; vale também para a chave do n8n). O segredo tem 256 bits, mas cada tentativa consulta o banco. Recomenda-se limitador por IP anterior à autenticação antes de expor publicamente.
4. O `429` do limitador traz `Retry-After: 60` fixo; o worker o respeita e pode ficar até ~1 min parado.
5. Relógios: lease/backoff usam o relógio da API (único no modo remoto — melhor que na 2B); o worker usa o seu apenas para temporizadores.
6. Resultado de tarefa é livre (≤ 4 KB, JSON): a Central não consegue provar que não contém segredo. Cada executor real precisa de resultado tipado e revisado.
7. A revogação não interrompe a execução em curso no worker (ele só perde o acesso); o lease vence e a recuperação reencaminha a tarefa.
8. Recuperação roda dentro da API: se a API cair, leases vencidos aguardam; com várias réplicas é seguro (`SKIP LOCKED`).
9. Operadores (papel Operator) veem nomes, versões e atividade dos executores (sem credenciais).
10. Diretório ainda sem git; teste de processos reais por HTTPS pendente (item 7).

## 10. Pendências para a integração futura com o AgenteAdsPower
- Definir tipos de tarefa reais (ex.: verificação de impedimentos da ADR 0015) com **payload fechado e referências** (id do perfil do AdsPower, nunca credenciais), executor idempotente e resultado tipado; reconciliar `docs/warmup/verification-prompt.md` com a ADR 0015 antes de qualquer automação. **Nenhum executor pode contornar CAPTCHA/checkpoint/verificações**: diante de impedimento, parar, registrar e deixar para análise humana.
- O executor Windows falará com a API **local do AdsPower** (`127.0.0.1`), com a chave do AdsPower guardada só na máquina do worker (DPAPI/Credential Manager), nunca na Central; trocar a chave já exposta antes.
- Mapear o resultado da tarefa para `profiles.HealthChecks` (hoje ainda não aplicada) com ator = executor (a coluna `ActorId` exige decisão: aceitar executor ou criar `ExecutorId`).
- Instalar o worker como serviço do Windows (conta de serviço mínima), com atualização controlada de versão (a Central já recebe `workerVersion` e pode exigir versão mínima).
- Limites por criador/fila, retenção de tarefas/eventos, alertas de executor offline, endpoint/limitador pré-autenticação, TLS confiável e deploy no Railway (`ReverseProxy`, etc., ver `RAILWAY_DEPLOYMENT.md`).
- Repetir o teste com processos reais sobre HTTPS confiável e registrar o resultado.

> **Correção (Etapa 2C.1, 21/10/2026):** o item "Processos reais separados" da seção 7 atribuiu a falha do handshake a um certificado de desenvolvimento "não confiável". **Essa atribuição não foi verificada e estava errada**:
> o certificado de desenvolvimento está confiável neste Windows (`Cert:\CurrentUser\Root`) e o worker autentica normalmente por HTTPS. A causa daquela primeira falha não foi identificada (não se repetiu em 6 execuções
> posteriores); o worker agora registra a causa técnica segura no log (`Unavailable/http_xxx` ou o tipo da exceção). Ver `docs/etapa-2c1-report.md`.
