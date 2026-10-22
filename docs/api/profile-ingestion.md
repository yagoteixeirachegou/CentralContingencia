# Ingestão de perfis pelo n8n

API própria para o fluxo "ingestão de perfil": reservar proxies livres, criar os perfis `Perfil N` e, depois que o perfil
existir no AdsPower, confirmar o vínculo. Substitui a leitura e a escrita da planilha HTTP - FACEBOOK.
Decisão e riscos: [ADR 0013](../decisions/0013-profile-ingestion-integration.md). Fluxo do n8n: [docs/n8n/README.md](../n8n/README.md).

## Autenticação

Chave de serviço, não conta de pessoa: `Authorization: Bearer <chave>`. Sem cookie e sem CSRF. A chave só abre estes quatro
endpoints (escopo `profile-ingestion`); não abre nenhuma API usada pelas pessoas. A configuração guarda apenas o hash SHA-256
(`Integration:Keys:N:{Id,Name,KeyHash,Scopes}`). Gerar: `scripts/New-LocalIntegrationKey.ps1`; carregar na API:
`scripts/Set-LocalIntegration.ps1`. Chave ausente, inválida ou curta: 401. Chave sem o escopo: 403. HTTPS obrigatório.
Limite: `Security:IntegrationPermitLimit` requisições por minuto por IP (padrão 120). Respostas `no-store`.

Cada ação grava auditoria com o `Id` da chave como ator. A chave só enxerga e altera os lotes que ela mesma criou (outros: 404).

## Endpoints (`/api/v1/integration/profile-ingestions`)

## Grupo de ingestão e numeração (decisão de 13/10/2026)

A numeração e a criação acontecem **em um único grupo do AdsPower**, escolhido por modo (`ProfileIngestion:Mode`, padrão `Test`):

| Modo | Grupo (nome exato) | Uso |
|---|---|---|
| `Test` | `FB Teste | YAGO` | testes; nunca toca a esteira |
| `Production` | `03 ESTEIRA DE PERFIL` | produção; exige configuração explícita |

**Próximo número = (maior `Perfil N` DESSE grupo no AdsPower, lido na hora) + 1.** A Central também considera o que ela mesma reservou ou
confirmou neste grupo (o intervalo até o perfil aparecer no AdsPower), e **nunca** olha perfis de outros grupos, perfis de teste ou os
recuperados da planilha. Nomes podem se repetir entre grupos diferentes (aceito: ex.: `Perfil 1250` na esteira e em `GG | …`). O nome é lido de forma
tolerante (`Perfil 1250 (revisar)` conta como 1250); errar para mais é seguro. O grupo de cada lote fica gravado (`IngestionBatches.AdsPowerGroupId`), então
lotes de teste não contaminam a produção. Sem o AdsPower, sem a chave ou com o grupo ausente ou duplicado, **nada é reservado** (503/502 com `code`:
`GroupNotFound`, `Unreachable`, `NotConfigured`, `Unauthorized`, `Failed`). Em 12/10/2026: esteira = 22 perfis, 1202–1243, próximo 1244; YAGO: próximo 1251.

### `GET /availability`
`{ availableProxies, nextProfileNumber, nextProfileName, group: { id, name, mode } }`. Sem credenciais. Lê o grupo no AdsPower.

### `POST /reserve`
Cabeçalho `Idempotency-Key: <UUID>` (obrigatório). Corpo: `{ "count": 1..50, "platform1": "ESTEIRA DE PERFIL" }` (`platform1` opcional,
do catálogo existente). Em uma transação: escolhe até `count` proxies livres, cria os perfis e reserva cada proxy por 15 minutos
(`ProxyAllocations:ReservationMinutes`).

- **Proxy livre:** `Active`, dentro da validade e sem alocação ativa (reserva vencida não conta).
- **Ordem:** a da planilha original (linha do CSV importado); proxies sem linha vêm depois, por data de cadastro.
- **Entrega parcial:** se faltar proxy, atende as possíveis. Perfis pendentes não consomem número.

Resposta 200 (o n8n deve criar os perfis em `group.id`, nunca em um grupo fixo):

```json
{ "batchId": "…", "replayed": false, "requested": 5, "reserved": 3, "status": "Partial",
  "group": { "id": "10906211", "name": "FB Teste | YAGO", "mode": "Test" },
  "items": [ { "allocationId": "…", "profileId": "…", "profileName": "Perfil 1598", "profileNumber": 1598,
               "status": "Reserved", "reservationExpiresAt": "…",
               "proxy": { "host": "…", "port": 8080, "username": "…", "password": "…", "region": "…", "expiresAt": "…" } } ],
  "pending": [ { "position": 4, "reason": "NoProxyAvailable", "message": "Não há proxy disponível para este perfil." },
               { "position": 5, "reason": "NoProxyAvailable", "message": "…" } ] }
```

`status`: `Complete` (todos), `Partial` (alguns), `Empty` (nenhum). Os itens vêm em ordem de número; as posições pendentes são
as últimas do pedido (`reserved + 1 … requested`).

**Credenciais:** `proxy.password` e `proxy.username` são devolvidos **somente aqui, somente para as proxies reservadas neste lote**
e só enquanto a reserva está válida. Reserva confirmada, liberada ou vencida volta com `proxy: null`. Nenhum outro endpoint
devolve senha de proxy.

**Idempotência:** repetir a chamada com a mesma chave e o mesmo corpo devolve o resultado original (`replayed: true`), sem nova
proxy, perfil ou auditoria; o que foi decidido não muda mesmo que apareçam proxies novas. Mesma chave com `count` ou
`platform1` ou grupo diferentes: 409.

### `POST /{allocationId}/confirm`
Corpo: `{ "adsPowerId": "k1abc12", "serialNumber": "1234" }` (`serialNumber` opcional). A proxy passa a "em uso" e o id e o
número de série do AdsPower ficam gravados no perfil. 204. Repetir os mesmos valores é aceito; outro id para o mesmo perfil,
ou um id do AdsPower já usado por outro perfil: 409. Reserva vencida: 409 (a recuperação é gravada e a proxy volta a ficar livre).
`adsPowerId`: 3 a 64 caracteres `A-Z a-z 0-9 _ -`; `serialNumber`: só dígitos (até 20).

### `POST /{allocationId}/release`
Cancela uma reserva **ainda não confirmada** (por exemplo, quando o AdsPower recusou a criação): a proxy volta a ficar livre,
o perfil é encerrado e o número volta a ser usado. 204. Repetir, ou liberar o que já venceu, também retorna 204. Reserva já
confirmada: 409.

### `POST /batches/{batchId}/release`
Desfaz **todas** as reservas ainda não confirmadas do lote de uma vez (use quando o fluxo falha antes de criar os perfis: lote
inválido, AdsPower fora do ar, nome duplicado). Proxies voltam a ficar livres, os perfis são encerrados e os números são
reaproveitados, sem buracos na numeração. Perfis já confirmados não são tocados. Resposta 200:
`{ "batchId", "released", "confirmed", "alreadyEnded" }` (`alreadyEnded` = reservas já liberadas ou vencidas). Repetir é seguro
(`released` = 0, sem novos eventos de auditoria). Lote de outra credencial ou inexistente: 404.

## Reservas vencidas

Sem confirmação em 15 minutos a reserva expira (com auditoria), a proxy volta a ficar livre e o perfil criado pela ingestão é
encerrado, liberando o número. A recuperação roda no próximo pedido de ingestão, sem processo em segundo plano.

## Erros (ProblemDetails)

| HTTP | Significado |
|---|---|
| 400 | Corpo, `Idempotency-Key`, quantidade, plataforma ou id do AdsPower inválidos |
| 401 / 403 | Chave ausente ou inválida / sem o escopo |
| 404 | Reserva inexistente ou de outra credencial |
| 409 | Estado mudou, reserva vencida, id do AdsPower em conflito ou chave de idempotência com outro pedido |
| 413 / 415 | Corpo acima de 4 KB / não é JSON |
| 429 | Limite por minuto |
| 500 / 503 | Falha ao registrar auditoria (rollback) / serviço, chave de criptografia, AdsPower ou grupo de ingestão indisponíveis |
| 502 | O AdsPower recusou a chave ou respondeu algo inesperado ao numerar |

## Garantias

Exclusividade por índice único parcial e por transação serializada com as demais alocações; auditoria na mesma transação
(`ProfileCreated`, `AllocationReserved`, `AllocationConfirmed`, `ProfileUpdated`, `ProfileClosed`, `AllocationExpired`);
nenhuma credencial em logs, auditoria ou mensagens de erro.

## Migrations

`20261013105059_AddIngestionBatchGroup` (aditiva: `AdsPowerGroupId`/`AdsPowerGroupName` em `IngestionBatches`, SQL em
[etapa-2.5-ingestion-group-migration.sql](../security/etapa-2.5-ingestion-group-migration.sql)); `Down` recusa apagar o grupo dos lotes.

`20261012230807_AddProfileIngestion` (aditiva): colunas `AdsPowerId`, `AdsPowerSerialNumber`, `IngestionBatchId` em
`profiles.OperationalProfiles`, tabela `profiles.IngestionBatches`, índice único parcial em `AdsPowerId`. SQL revisado em
[docs/security/etapa-2.5-profile-ingestion-migration.sql](../security/etapa-2.5-profile-ingestion-migration.sql).
