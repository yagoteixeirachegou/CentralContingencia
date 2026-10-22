# Etapa 2B — Fila de tarefas e worker (relatório)

Data: 21/10/2026. Escopo: implementação local com execução simulada. Sem deploy, sem migration aplicada em banco existente,
sem AdsPower/agentes/navegador, sem credenciais reais. Decisões: ADR 0017.

## 1. Auditoria inicial (resumo)
Padrões reaproveitados: stores EF que criam um `DbContext` por operação (`EfHealthCheckStore`), `TimeProvider` injetado, políticas
`Management`/`Operations`, CSRF e rate limit `sensitive`, `Idempotency-Key` UUID, leitura manual de corpo com limites (`IngestionEndpoints.ReadBodyAsync`,
tornado `internal`), exceções de aplicação mapeadas para ProblemDetails, auditoria em `audit.*`. `OperationalAuditEvent` tem CHECK de ações próprias,
então a fila ganhou tabela de auditoria própria em vez de reutilizá-lo. Nada existente foi duplicado.

## 2. Arquivos
**Novos** — Domain: `WorkQueue/{WorkTask,WorkTaskEvent,WorkTaskEnums,WorkErrorSanitizer}.cs`. Application: `WorkQueue/{WorkQueueOptions,WorkTaskPayloads,WorkQueueContracts,WorkTaskService}.cs`.
Infrastructure: `WorkQueue/EfWorkTaskStore.cs`, `Persistence/Configurations/WorkTaskConfiguration.cs`, migration `20261021191536_AddWorkQueue`. Api: `WorkQueue/WorkTaskEndpoints.cs`.
Worker (projeto novo `src/CentralContingencia.Worker`): `Program.cs`, `WorkerService.cs`, `TaskRunner.cs`, `Executors.cs`, `WorkerOptions.cs`, `appsettings.json`.
Testes: `WorkTaskTests`, `WorkTaskPayloadTests`, `WorkerTests` (unit); `WorkQueueStoreTests`, `WorkTaskApiTests`, `WorkerEndToEndTests` (integração). Docs: ADR 0017, este relatório, `docs/security/etapa-2b-work-queue-migration.sql`.
**Alterados** — `CentralContingenciaDbContext.cs` (DbSets/configurações), `DependencyInjection.cs` (`AddWorkQueue`, `AddWorkerInfrastructure`, `AddDatabase`), `Program.cs` da API (mapeia `/api/v1/tasks`; OpenAPI), `IngestionEndpoints.cs` (`ReadBodyAsync` → `internal`),
`CentralContingencia.slnx`, `CentralContingencia.UnitTests.csproj` e `…IntegrationTests.csproj` (referência ao Worker), `CLAUDE.md`, `README.md`.

## 3. Modelo de dados
`work."Tasks"` — `Id` uuid PK · `Type` (texto, CHECK no catálogo) · `Status` (CHECK nos 6 estados) · `PayloadVersion` int · `Payload` **jsonb** · `Result` jsonb · `CreatedAt`, `StartedAt`, `CompletedAt`, `AvailableAt` (backoff) timestamptz ·
`AttemptCount`, `MaxAttempts` (1..10) · `LastErrorCode` (≤64), `LastError` (≤300, sanitizado) · `CorrelationId` (≤64) · `IdempotencyKey` uuid, `RequestFingerprint` (SHA-256) · `CreatedBy` uuid ·
`WorkerId` (≤100), `LeaseUntil`, `HeartbeatAt` · `CancelRequestedAt`, `CancelRequestedBy`.
Constraints: `CK_Tasks_Lease` (**Running ⇔ worker + lease + heartbeat**; qualquer outro estado não retém nenhum), `CK_Tasks_Completed` (terminal ⇔ `CompletedAt`), `CK_Tasks_Attempts` (`0 ≤ tentativas ≤ máximo ≤ 10`), `CK_Tasks_Status/Type/Ids/Cancel/Fingerprint/Version`.
Índices: único `(CreatedBy, IdempotencyKey)`; parcial `IX_Tasks_Pending (AvailableAt, CreatedAt) WHERE Status='Pending'`; parcial `IX_Tasks_Running (LeaseUntil) WHERE Status='Running'`; `(Status, CreatedAt)`; `CreatedAt`.
`audit."WorkTaskEvents"` — `Sequence` (identidade, ordem total) · `Id` · `TaskId` · `Action` (Created, Claimed, Succeeded, Failed, RetryScheduled, DeadLettered, CancelRequested, Cancelled, LeaseExpired, Released) · `ActorId` · `WorkerId` · `Attempt` · `Code` · `CorrelationId` · `OccurredAt`.
Só ids, ação e código sanitizado (nunca payload, resultado ou mensagem). Heartbeats não geram evento.

## 4. Diagrama de estados
```
              criar                         claim (SKIP LOCKED, AvailableAt ≤ agora, tentativa+1)
   ───────────────▶ Pending ─────────────────────────────────────────────────▶ Running
                     ▲  │ cancelar (imediato)                                   │ │ │ │
   falha transitória │  ▼                                                       │ │ │ └─ sucesso ───────────▶ Succeeded
   (tentativa<máx,   │ Cancelled ◀── cancelamento solicitado: falha/liberação/ ─┘ │ └─ falha permanente ────▶ Failed
    backoff) /       │               recuperação/confirmação do worker           └─── falha transitória com
   lease vencido /   └──────────── liberação (desligamento; não conta a tentativa)       tentativas esgotadas ─▶ DeadLetter
   (tentativa<máx)                  lease vencido com tentativas esgotadas ───────────────────────────────────▶ DeadLetter
```
Invariantes: terminais (`Succeeded, Failed, Cancelled, DeadLetter`) não mudam; `Running` exige lease vigente para toda operação do worker; cancelamento pedido prevalece sobre retry/liberação/recuperação;
cancelar tarefa já concluída é 409; repetir cancelamento é seguro. Transições vivem em `WorkTask` (ver `WorkTaskTests`, 25 casos).

## 5. Contratos da API (`/api/v1/tasks`; cookie + CSRF; OpenAPI documentado)
| Método e rota | Papéis | Descrição |
|---|---|---|
| `POST /api/v1/tasks` | Admin, Manager (limite `sensitive`) | Cabeçalho obrigatório `Idempotency-Key` (UUID). Corpo `{ "type": "SimulatedProcessing", "payload": {…}, "maxAttempts": 1..10? }`, **nada mais** (campo desconhecido → 400). 201 + `Location`; replay idêntico 200 com a mesma tarefa; mesmo key com outro conteúdo 409. Corpo > 4 KB → 413; não-JSON → 415. |
| `GET /api/v1/tasks/{id}` | Admin, Manager, Operator | Visão pública. 404 se não existe. |
| `GET /api/v1/tasks?status=&type=&page=&pageSize=` | idem | Mais recentes primeiro; `page` 1..10000, `pageSize` 1..100 (padrão 20); filtros exatos validados. |
| `POST /api/v1/tasks/{id}/cancel` | Admin, Manager | Sem corpo. Pending → `Cancelled`; Running → 200 com `cancelRequested=true`; concluída → 409. |
Resposta: `id, type, status, payloadVersion, payload, result, createdAt, startedAt, completedAt, nextAttemptAt (só Pending), attemptCount, maxAttempts, lastErrorCode, correlationId, cancelRequested`.
**Não** expõe worker, lease, heartbeat, fingerprint, chave de idempotência, criador nem texto de erro. O cliente não consegue definir status, lease, tentativas nem worker (não há rota nem campo; testado). Erros: 400/404/409/503 em ProblemDetails com mensagem fixa em português.
Payload `SimulatedProcessing` v1: `{ version:1, steps:1..50, stepDelayMs:0..5000, behavior: Succeed|FailTransient|FailPermanent|FailUntilAttempt, succeedOnAttempt:2..10 (só com FailUntilAttempt) }`, validação estrita, JSON canônico ≤ 2 KB. `type` aceita só o nome exato do catálogo.
Autorização: leitura Operations (Admin/Manager/Operator), escrita Management (Admin/Manager). Não há escopo por dono: qualquer operador autorizado vê todas as tarefas (decisão a rever quando houver dados por cliente).

## 6. Estratégia de concorrência
- **Claim:** transação `SELECT "Id" … WHERE Status='Pending' AND AvailableAt<=agora AND Type=ANY(tipos) ORDER BY AvailableAt, CreatedAt LIMIT n FOR UPDATE SKIP LOCKED` → carrega as linhas → `Claim()` do domínio → auditoria → commit. Workers concorrentes nunca esperam nem recebem a mesma linha (teste: 8 workers × 40 tarefas, cada tarefa entregue exatamente uma vez).
- **Demais operações:** `SELECT … FOR UPDATE` da linha, domínio decide, auditoria, commit. Complete × cancelar simultâneos têm resultado determinístico (teste de 10 rodadas).
- **Posse:** worker + tentativa + `LeaseUntil > agora`. Worker atrasado/"ressuscitado" recebe `LeaseLost` em heartbeat, complete, fail e release; o worker descarta o resultado (ao-menos-uma-vez).
- **Recuperação:** qualquer worker roda `RecoverExpired` periodicamente (`SKIP LOCKED`, lote de 100): lease vencido → Pending com backoff, DeadLetter ao esgotar tentativas, ou Cancelled se havia pedido.
- **Idempotência:** índice único + captura de `23505` + fingerprint; 12 criações paralelas com a mesma chave geram uma só tarefa.
- **Retry:** backoff exponencial determinístico `base·2^(tentativa-1)` limitado (`WorkQueue:RetryBaseDelaySeconds` 5, máx 300), aplicado em `AvailableAt` (o claim respeita). Sem jitter (anotado como limitação).
- **Worker:** `MaxConcurrency` (semáforo), heartbeat a cada `HeartbeatSeconds` (≤ lease/3, validado), desligamento ordenado (cancela execuções, devolve cada tarefa à fila sem consumir tentativa, aguarda `ShutdownGraceSeconds`), laço resiliente a falhas de banco (backoff exponencial limitado, logs só com o tipo da exceção).

## 7. Segurança
Payload fechado (sem campo para credenciais/cookies/tokens; testado com `password`, `cookies`, `token`); erro sanitizado antes de gravar (senha, host, e-mail, tokens ≥ 24 caracteres); auditoria só com ids/códigos; respostas 503 sem detalhe de infraestrutura (a exceção interna fica no `InnerException`, que a API nunca serializa nem registra);
worker não registra payload, resultado nem mensagens de exceção; rate limit e CSRF herdados; nada de integração externa.

## 8. Resultados reais (21/10/2026)
| Verificação | Resultado |
|---|---|
| `dotnet build CentralContingencia.slnx` | 0 erros, 2 avisos NU1510 (já documentados na Etapa 2A) |
| Testes unitários | **422/422** (eram 326; +96: domínio, payloads, sanitizador, `TaskRunner`, executor simulado, `WorkerService`, opções) |
| Testes de integração (PostgreSQL 17 descartável em Docker) | **319/319** (eram 260; +59: store/concorrência 22, API 32, worker ponta a ponta 5) |
| Worker como processo real (`dotnet …Worker.dll`) contra banco descartável | executou 1 tarefa: `Succeeded`, resultado tipado, auditoria `Claimed → Succeeded`; senha do banco ausente do log |
| Frontend | não alterado (213/213 com `--maxWorkers=2` na Etapa 2A) |
| Docker | ver nota abaixo |
Cobertura pedida: idempotência ✔ · concorrência entre workers ✔ · claim atômico ✔ · lease expirado ✔ · heartbeat ✔ · worker interrompido (fantasma recuperado por outro; desligamento ordenado) ✔ · retry ✔ · dead-letter ✔ · cancelamento (Pending, Running via heartbeat, abandonada) ✔ · falhas do banco (serviço e laço do worker) ✔ · autorização (anônimo/Operator/Manager/Admin, CSRF) ✔ · validação de payload ✔.
Um bug real foi encontrado e corrigido pelos testes: `TaskRunner` tratava resultado `null` de sucesso como "resultado grande demais".

## 9. Migration
`20261021191536_AddWorkQueue` — **aditiva** (cria schema `work`, 2 tabelas, constraints e índices; sem `DROP`/`ALTER`). **Gerada e não aplicada em nenhum banco existente.** Revisão em `docs/security/etapa-2b-work-queue-migration.sql`.
Aplicada apenas em bancos descartáveis (fixtures de teste e um banco `p6_worker_smoke` em contêiner removido ao final). A API e o worker nunca migram sozinhos.
Observação: a primeira versão da migration (sem `Sequence`) foi removida com `ef migrations remove --force` antes de qualquer uso.

## 10. Como rodar localmente (não executado contra o banco de desenvolvimento)
Depois de aplicar a migration sob controle humano: `dotnet run --project src/CentralContingencia.Worker` com `ConnectionStrings__DefaultConnection` (e `DOTNET_ENVIRONMENT=Development`). Configuração em `appsettings.json` (`Worker:*`, `WorkQueue:*`).

## 11. Limitações e riscos
1. **Worker acessa o banco diretamente.** Adequado ao ambiente local; para VM Windows/Railway exige canal HTTPS de saída com credencial de worker (Etapa 2C). Não exponha o PostgreSQL ao worker remoto.
2. Relógios de API/worker definem lease e backoff (sem `now()` do banco): exige NTP; deriva > lease/3 pode causar recuperação indevida (a tarefa é reexecutada: ao-menos-uma-vez, executores futuros precisam ser idempotentes).
3. Sem jitter no backoff e sem limite global de fila/limite por criador (um Manager pode enfileirar muitas tarefas; o rate limit `sensitive` de 10/min atenua).
4. Não há retenção/limpeza de tarefas e eventos antigos, nem painel/UI; a recuperação roda dentro dos workers (se nenhum worker estiver ativo, leases vencidos ficam parados até um voltar).
5. Heartbeat e cancelamento têm latência de até `HeartbeatSeconds` (1 s nos testes, 20 s por padrão).
6. Sem escopo por dono nas consultas (operadores veem todas as tarefas).
7. Único tipo: `SimulatedProcessing`. Cada tipo real exigirá payload fechado, executor idempotente e revisão de segurança; **nenhum** executor deve contornar verificações de plataformas.
8. Os testes de integração exigem `CENTRAL_TEST_POSTGRES`; sem ele são ignorados.
9. O diretório continua sem git.

## 12. Próxima etapa recomendada (2C — não iniciada)
Canal worker↔API por HTTPS de saída: credencial de serviço por executor (hash em tabela, revogável), endpoints `claim/heartbeat/result` sobre os mesmos casos de uso, registro de executor com heartbeat próprio, e tela de monitoramento de tarefas no frontend.
