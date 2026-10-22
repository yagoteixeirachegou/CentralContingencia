# ADR 0018 — Identidade de executor e canal HTTPS para workers remotos

Data: 21/10/2026. Status: aceita (Etapa 2C autorizada pelo usuário). Relatório: `docs/etapa-2c-report.md`. Complementa a ADR 0017.

## Decisões
- **O worker remoto só fala HTTPS com a API** (`/api/v1/agent/*`) e **nunca abre conexão com o PostgreSQL**. O modo remoto é o padrão do worker e recusa
  qualquer `ConnectionStrings:DefaultConnection`; o modo `LocalDatabase` (Etapa 2B) existe só para desenvolvimento e é recusado fora de `Development`.
- **Identidade própria por executor** (`work.Executors`), independente do Identity dos usuários. `ExecutorId` é um UUID estável; o criador da tarefa (`CreatedBy`, usuário)
  e o executor (`LastWorkerId`) são campos distintos.
- **Credencial individual** `ce1.<ExecutorId>.<segredo de 32 bytes>`: persiste-se apenas o SHA-256 do segredo (alta entropia, sem necessidade de hash lento). Exibida uma única
  vez no provisionamento/rotação por Admin. Rotação invalida a anterior na hora; revogação é definitiva. A credencial é conferida a cada requisição (comparação em tempo
  constante), então revogar/rotacionar vale imediatamente.
- **Esquema de autenticação exclusivo** (`ExecutorKey`, Bearer): cookies de usuário não entram no canal do executor e credenciais de executor não abrem nenhuma rota de usuário.
  O canal dispensa CSRF (sem cookie) e tem cota por executor (`Security:ExecutorPermitLimit`, 600/min), não por IP.
- **Tipos permitidos por executor** (`AllowedTypes`, definidos pelo Admin): o claim é sempre interseção entre o pedido e o permitido.
- **Sem regras duplicadas:** os endpoints traduzem HTTP; estado, lease, tentativas, idempotência e auditoria continuam em `WorkTask`/casos de uso. Executor e worker nunca são
  informados pelo cliente: vêm da credencial. Repetições de `complete`/`fail`/`cancelled` pelo mesmo executor e tentativa são idempotentes (resposta perdida).
- **Recuperação de leases vencidos** passa a ser feita pela API (`WorkQueueRecoveryService`), pois o worker remoto não tem banco. No reinício o worker devolve (`release`) o que a
  execução anterior segurava, sem consumir tentativa.
- **Presença:** o heartbeat do executor registra estado (Idle/Busy/Draining), versão e última comunicação; "Online" = comunicou dentro de `ExecutorOnlineSeconds`.

## Consequências
- Roubo da credencial de um worker permite reservar tarefas dos tipos permitidos a ele até a revogação; o Admin revoga em um clique. Não há IP fixo nem mTLS nesta etapa.
- A API passa a ser ponto único de recuperação e de presença; com várias réplicas a recuperação usa `SKIP LOCKED`.
- A resposta perdida de um `claim` não é idempotente: a tarefa fica com o executor até o lease vencer (ou o executor reiniciar e devolvê-la).
