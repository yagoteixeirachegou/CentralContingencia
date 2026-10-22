# ADR 0019 — Freio de autenticação inválida, reentrega idempotente de claim e recuperação confiável

Data: 21/10/2026. Status: aceita (Etapa 2C.1 autorizada pelo usuário). Complementa as ADRs 0017 e 0018. Relatório: `docs/etapa-2c1-report.md`.

## Decisões
- **Freio de tentativas inválidas (`AuthFailureThrottle`)** nos esquemas `ExecutorKey` e `IntegrationKey` (n8n). A autorização responde 401 antes do middleware de rate limiting, então o limitador comum não via
  essas tentativas. Agora cada credencial inválida é contada por escopo e cliente (IPv6 por /64); ao exceder `Security:AuthFailureLimit` (10) em `Security:AuthFailureWindowSeconds` (60) a API responde
  `429 + Retry-After` **sem consultar o banco**. O cliente é o `RemoteIpAddress` já corrigido pelo `UseForwardedHeaders`: só vale o `X-Forwarded-For` de proxy confiável configurado. Indisponibilidade do banco não conta como falha.
  Trade-off aceito: um cliente bloqueado recebe 429 mesmo com credencial válida (um atacante e um worker legítimo atrás do mesmo NAT se afetam); contagem por processo.
- **O canal do agente (Bearer) não divide o limite global por IP** com as pessoas (`GetNoLimiter`); tem cota por executor (600/min) e o freio acima. Descoberto na homologação: o limite global de 120/min por IP
  fazia o worker receber 429 (heartbeats falhando, lease perdido) quando pessoas ou scripts no mesmo IP consultavam a API. O `Retry-After` do limitador passou a refletir a janela real (1–60 s).
- **Claim idempotente**: `POST /agent/tasks/claim` aceita `Idempotency-Key` (UUID). A chave é gravada em `WorkTask.ClaimKey`. Repetir a MESMA chave devolve apenas as tarefas que ESTE executor ainda segura com lease
  vigente (sem nova tentativa, sem novo evento, sem alterar lease); outro executor nunca recebe tarefa alheia por chave; depois de concluída/expirada a tarefa não é reentregue. O worker reutiliza a chave quando a falha
  é indeterminada (transporte/503/429) e a troca após qualquer resposta definitiva. Sem a chave, o comportamento é o anterior.
- **Recuperação de leases pela API** (`WorkQueueRecoveryService`): intervalo validado na partida (0–3600; inválido impede a inicialização), prazo de 30 s por rodada, falhas apenas registradas (o laço continua),
  independente de tráfego HTTP. Provado por teste: sobe o host sem nenhuma requisição e a tarefa de executor morto volta à fila.
- **Confiança TLS por processo** (`Worker:Api:TrustedCaFile`): para CA privada (homologação/VM interna) o worker adiciona a CA como raiz confiável **somente no seu processo**, mantendo cadeia, validade e nome do host.
  Nome divergente nunca é perdoado. Revogação não é consultada (CA privada sem CRL). Não altera o repositório de certificados do sistema.
- Diagnóstico seguro: `WorkQueueException.Detail` (ex.: `http_503`, `HttpRequestException>AuthenticationException`) aparece nos logs do worker; nunca host, URL ou mensagem de rede.

## Consequências
Migration aditiva `AddClaimKey` (coluna anulável + índice parcial). Contagem do freio é por réplica da API. Nenhuma regra de negócio foi alterada; autenticação, autorização e auditoria não foram enfraquecidas.
