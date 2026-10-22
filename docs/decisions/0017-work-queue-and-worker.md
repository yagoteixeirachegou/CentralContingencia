# ADR 0017 — Fila de tarefas em PostgreSQL e worker independente

Data: 21/10/2026. Status: aceita (Etapa 2B autorizada pelo usuário). Relatório: `docs/etapa-2b-report.md`.

## Decisões
- **PostgreSQL é a fonte de verdade.** Tabela `work."Tasks"` + auditoria `audit."WorkTaskEvents"`; nada de broker externo.
- **Contexto novo `WorkQueue`** (Domain/Application/Infrastructure/Api) com a entidade `WorkTask`, que concentra a máquina de estados.
  Estados: `Pending, Running, Succeeded, Failed, Cancelled, DeadLetter`. Tipos: catálogo fechado (`SimulatedProcessing`).
- **Posse por lease.** Toda operação do worker após o claim exige worker + número da tentativa + lease vigente; lease vencido, mesmo sem recuperação, é perda de posse (entrega ao-menos-uma-vez; o resultado atrasado é descartado).
- **Concorrência:** `SELECT … FOR UPDATE SKIP LOCKED` no claim e na recuperação; `SELECT … FOR UPDATE` nas demais operações; transição + auditoria no mesmo commit; CHECK constraints impedem estados incoerentes mesmo por SQL direto.
- **Idempotência** por `(criador, Idempotency-Key)` com índice único e fingerprint do conteúdo (mesmo conteúdo → mesma tarefa; outro conteúdo → 409).
- **Payload** versionado e validado por tipo, com lista fechada de campos: não há onde transportar credenciais, cookies ou tokens. Dados sensíveis devem ser referenciados por id de registro protegido.
- **Erros:** código estável + mensagem sanitizada (segredos, hosts, e-mails e tokens longos são removidos; 300 caracteres). A API expõe só o código.
- **Cancelamento cooperativo:** `Pending` cancela na hora; `Running` marca `cancelRequested` e o worker confirma via heartbeat.
- **Worker** é um projeto separado (`CentralContingencia.Worker`) que acessa o PostgreSQL pela Application/Infrastructure (sem API). Executor único e simulado.

## Consequências
- O worker precisa de conexão direta ao banco. Isso serve ao desenvolvimento local; um worker remoto (VM Windows) exigirá um canal HTTPS de saída com credencial própria (Etapa 2C), nunca o banco exposto.
- Relógios: lease, backoff e recuperação usam o relógio de quem opera (API/worker). Exige NTP; o lease de 60 s tolera deriva pequena.
- Nenhum AdsPower, navegador, agente externo ou rede social é tocado.
