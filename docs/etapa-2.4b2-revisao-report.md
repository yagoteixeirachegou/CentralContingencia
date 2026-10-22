# Revisão técnica — Etapa 2.4B.2

Data: 11/10/2026. Revisão de código, contratos, migrations e testes existentes.
Somente este relatório foi criado. Nenhuma implementação foi corrigida, migration
aplicada, banco alterado, servidor reiniciado, frontend iniciado, commit ou deploy.

## Parecer

**Bloquear a homologação funcional definitiva da API até corrigir a classificação
de falhas internas de auditoria e decidir expressamente a política de recuperação/retry.**
As duas migrations são aditivas e não apresentam operação destrutiva no Up. Seu SQL
é tecnicamente elegível para aprovação separada, condicionada a autorização explícita,
backup verificável atualizado, conferência do alvo e validação posterior. Aplicar
somente o schema não equivale a homologar o comportamento da API.

Não foi identificado mecanismo que permita duas alocações ativas pelo fluxo normal.
Há proteção no domínio, transação/locks e dois índices únicos parciais. Não há indicação
de perda de histórico ou alteração/recriptografia dos dados anteriores pelas migrations.

## Fontes e limites da evidência

Lidos AGENTS.md, CLAUDE.md, relatórios 2.4B.1/2.4B.2 e contrato REST. Inspecionados
ProxyAllocation, AllocationDomainService, OperationalManagementService, endpoints,
repositórios, classificador de falhas, auditoria, disponibilidade, mapeamentos e SQLs.

Nesta revisão, executados **32 testes unitários existentes, todos aprovados**, somente
em memória, de AllocationDomainTests e OperationalApplicationTests:

```powershell
dotnet test tests/CentralContingencia.UnitTests --no-build --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b2/ --filter 'FullyQualifiedName~AllocationDomainTests|FullyQualifiedName~OperationalApplicationTests'
```

Os 339 testes da entrega anterior (192 unitários/147 integração) são evidência
histórica; a suíte de integração não foi repetida nesta revisão. Nenhum novo teste
foi escrito, nem schema descartável preparado. Não foram feitas chamadas HTTP
autenticadas na homologação, pois gerariam auditoria persistente.

Consulta somente leitura confirmou quatro migrations antigas registradas e ausência
das três tabelas novas. Esta revisão não executou backup, restauração de ensaio ou
aplicação real; não pode garantir antecipadamente o resultado de uma futura operação.

## Regras verificadas

| Item | Resultado verificado |
| --- | --- |
| Reserva | Exige perfil Active, proxy operacional/válida/desocupada, prazo futuro e não superior à validade da proxy. Cria Reserved e ocupa ambos os recursos. |
| Confirmação | Revalida IDs, estado do perfil, proxy e horário após locks. Só Reserved e antes do prazo pode virar Confirmed. |
| Liberação | Reserved ou Confirmed vira Released. Pode cancelar uma reserva antes ou depois do prazo. Preserva vínculo, datas e motivo. |
| Reutilização | Novo UUID de alocação, mesma proxy somente se ainda Active, não vencida e sem outro vínculo ativo. Liberação não reativa nem prorroga a proxy. |
| Exclusividade de proxy | FOR UPDATE da proxy, consulta de vínculos e UX_ProxyAllocations_ActiveProxy. Reserved e Confirmed estão no filtro. |
| Exclusividade de perfil | FOR UPDATE do perfil, validação e UX_ProxyAllocations_ActiveProfile com o mesmo filtro. |
| Prazo vencido | Impede confirmação, mas não muda o estado automaticamente. Reserved continua ativo até liberação/expiração explícita/fechamento do perfil. |
| Perfil encerrado | Close finaliza vínculo ativo e perfil na mesma transação, sem mudar a proxy. Closed bloqueia nova reserva/PATCH. Histórico permanece consultável. |
| Histórico | IDs dos recursos não são reatribuídos. FKs RESTRICT impedem excluir perfil/proxy com histórico; repositórios não oferecem exclusão de alocações. |
| Permissões | Admin/Manager leem e escrevem; Operator somente lê. API e Application aplicam a matriz. Integration/desconhecidos recusados. |
| Auditoria HTTP | Casos de uso fornecem contexto e persistem evento junto da operação. Eventos só contêm UUIDs, ação, ator, UTC e correlation ID. |

Fluxo esperado: reserva → confirmação → liberação → nova reserva; também é possível
reserva → liberação (cancelamento) e reserva → Expired por chamada interna explícita.
Confirmadas não expiram pelo prazo da antiga reserva. Bloqueio/vencimento posterior
da proxy não encerra automaticamente um vínculo Confirmed; requer ação operacional.

### Concorrência

O repositório abre READ COMMITTED, define lock_timeout de cinco segundos, bloqueia
perfil antes da proxy, carrega estado/histórico e só então avalia o relógio/domínio.
SaveChanges e commit pertencem à mesma transação. Um segundo perfil disputando a
mesma proxy aguarda o lock e, após o primeiro commit, encontra o vínculo ativo.
Mesmo uma inserção que ignore o repositório enfrenta o índice único parcial.

Timeout, deadlock, serialização, xmin e unicidade são traduzidos em Conflict/409,
sem detalhes PostgreSQL. Testes anteriores cobrem concorrência nos dois lados e
unicidade por ator/chave. GET /available não constitui reserva nem garante que o
recurso continuará livre no momento da escrita.

### Idempotência e abandono

- **Confirmar novamente não retorna sucesso idempotente:** se já Confirmed, o domínio
  recusa e a API responde 409. Não cria segunda alocação nem novo evento; cliente
  precisa consultar GET /allocations/{id} para distinguir resultado já realizado.
- Release repetido também retorna 409. Close repetido retorna 204 sem duplicar auditoria.
- Reserva com mesmo ator/Idempotency-Key retorna 409, sem replay do resultado original.
  A chave impede duplicação, mas não existe consulta pública por chave para recuperar
  precisamente um UUID cuja resposta 201 tenha sido perdida.
- Prazo existe, mas não há job/endpoint público que faça expiração automática. Recuperação
  manual é possível por listagem/histórico e release por Admin/Manager, inclusive de
  reservas vencidas. Portanto, não é um bloqueio irrecuperável, mas exige intervenção.
- ExpireAsync interno chama Change com audit null. A transição Expired existe e funciona,
  porém não produz OperationalEvents. Não deve ser reutilizada por futura automação
  sem corrigir atribuição de responsável/auditoria.

## Riscos e correções

### R1 — Erro interno de auditoria é apresentado como erro de entrada

Classificação: **correção indispensável antes da homologação funcional definitiva**.

AllocationPersistenceErrors.Translate mapeia qualquer CheckViolation para Validation;
OperationalEndpoints converte Validation para HTTP 400. O teste
AuditFailureRollsBackProfileCreation injeta falha de CHECK em audit.OperationalEvents
com dados válidos e explicitamente espera BadRequest. Assim, o teste prova rollback,
mas também confirma que a resposta atribui ao cliente uma falha interna da auditoria.

Proposta: distinguir validações do cliente/invariantes de falhas internas ao gravar
auditoria. Para indisponibilidade de persistência usar 503; para outra falha interna,
status 5xx sanitizado apropriado. Atualizar teste para assegurar categoria correta,
rollback e ausência de conteúdo sensível. Não converter indiscriminadamente todas
as violações de integridade em 5xx; preservar 400/409 onde forem de fato pertinentes.

Evidência: Infrastructure/Allocations/AllocationPersistenceErrors.cs:16,
Api/Operations/OperationalEndpoints.cs:58 e OperationalEndpointTests.cs:181.
**Nenhuma correção foi aplicada.**

### R2 — Retry e reservas abandonadas exigem política operacional explícita

Classificação: decisão indispensável para definir o alcance da homologação;
correções de automação dependem de autorização posterior.

Para teste local supervisionado, é possível aceitar 409 em repetições e recuperação
manual por GET/release. Isso precisa ser aprovado como comportamento esperado.
Para automação sem supervisão, recomendar confirmação/liberação com retry seguro,
recuperação inequívoca por chave e estratégia de expiração. Não trocar o contrato
silenciosamente nem criar job nesta revisão.

Adicionar regressões HTTP dedicadas de confirm/release repetidos e concorrentes,
checando estado, datas e ausência de auditoria duplicada. O comportamento atual
da segunda confirmação foi deduzido do código; não há teste HTTP dedicado desse
cenário nos testes inspecionados. Os testes verdes anteriores não eliminam essa lacuna.

### R3 — Expiração interna sem auditoria

Classificação: **indispensável antes de habilitar recuperação automática**; não impede
o cancelamento manual auditado pelo endpoint release existente.

ExpireAsync não recebe ator/correlation e passa audit null. Eventos de expiração
estão previstos no enum/schema, mas não são emitidos por esse caminho. Antes de
um job/conector utilizá-lo, definir responsável e auditoria na mesma transação.
Os argumentos audit opcionais dos demais repositórios também permitem chamadas
internas sem eventos; a garantia atual é dos caminhos HTTP da Application.

### R4 — Resposta de proxies pode falhar depois de a escrita estar confirmada

Classificação: risco de compatibilidade e recuperação a validar antes de automação.

ProxyManagementService CreateAsync/UpdateAsync chama SaveAsync e depois consulta
HasActiveAllocationAsync. Se essa consulta falhar, a proxy/auditoria já estará
persistida, embora a resposta possa ser erro. Não alegar rollback de toda escrita
nessa situação nem recomendar retry cego de POST. A exceção dessa leitura não é
traduzida pelo classificador operacional; o handler global pode retornar 500.

Proposta: testar a falha da leitura pós-commit e definir resposta/recuperação coerente.
Iniciar versão nova antes da primeira migration também quebraria leituras existentes
de disponibilidade. Implantação local deve respeitar schema completo antes do restart.
Evidência: Application/Proxies/ProxyManagementService.cs:37 e :62.

### Outros limites

Histórico interno completo pode crescer; páginas/contagens são snapshots separados.
Não há ETag de edição do cliente. FKs não impedem um administrador de banco de
apagar linhas de histórico diretamente. Validade da proxy, estado de perfil e
fechamento coordenado exigem o repositório/domínio: os CHECKs não são triggers
cross-table. Não foram encontrados vazamentos nos DTOs/eventos inspecionados;
isso não garante futuras configurações de logging de transporte.

## Migrations pendentes, ordem e impacto

Leitura atual confirmou que o último registro homologado é
20261011165123_AddProxyImportBatches. As duas migrations seguintes são:

| Ordem | Migration | Impacto do Up |
| --- | --- | --- |
| 1 | 20261011223416_AddOperationalProfilesAndAllocations | Schemas profiles/allocations; OperationalProfiles/ProxyAllocations; 6 índices secundários, 2 PKs, 8 CHECKs, 2 FKs RESTRICT |
| 2 | 20261011230449_AddOperationalAudit | audit.OperationalEvents; 2 índices, 1 PK, 4 CHECKs |

Primeira: depende da tabela proxies.Proxies e sua PK já existentes. Segunda: usa
schema audit já existente; não tem FK para as tabelas da primeira. A dependência
entre as duas é de sequência EF e funcionamento do módulo, não uma FK da auditoria.
A versão 2.4B.2 precisa das duas para funcionar integralmente.

SQLs revisados: security/etapa-2.4b1-migration.sql e security/etapa-2.4b2-migration.sql.
Não há UPDATE/DELETE, conversão de datas/status, ALTER de tabelas antigas, recifragem,
alteração de Identity ou substituição de chaves. xmin é nativo, não nova coluna física.
As FKs novas podem aguardar locks na tabela referenciada de proxies. Os Down removem
tabelas novas e perderiam seu histórico/auditoria; não são recuperação automática segura.

**Preservação esperada dos dados existentes: sim, pelo SQL aditivo inspecionado.**
Garantia integral da aplicação real ainda não pode ser afirmada: depende de alvo,
backup, execução sem divergência e comparação pós-migration. O histórico EF mudará
intencionalmente de quatro para seis entradas; o catálogo receberá novos objetos.
Isso não deve mudar registros anteriores de usuários/proxies/auditorias/CSV.

Antes de eventual aplicação autorizada: conferir conexão local, migration exata,
SQL e ausência de divergências, criar/validar backup atualizado e conservar DPAPI/keyring.
Aplicar na ordem, sem liberar escrita operacional entre schema e validações; comparar
fingerprints/contagens anteriores, validar índices/constraints e só depois reiniciar
API com chaves existentes. Nenhum desses passos de alteração foi executado aqui.

## Integração futura com n8n

REST versionada, JSON, UUIDs, paginação, status HTTP e OpenAPI permitem cliente HTTP
direto, sem automação de navegador. O cliente pode obter CSRF, autenticar via API,
manter cookies e renovar CSRF após mudança de identidade, tecnicamente por HTTP.

**Não está pronta uma integração de serviço em produção:** Integration é recusado,
não existem credenciais/escopos próprios de máquina, e CLAUDE proíbe reutilizar login
humano compartilhado. Também faltam política de retry/replay/recuperação, expiração
auditada e tratamento operacional de sessão/rate limits. Preparação dos contratos
não equivale a autorização para conectar n8n ou conceder Admin a uma automação.

## Recomendação final

1. Manter migrations sem aplicação até a revisão/autorização do usuário.
2. Corrigir R1 antes de homologar definitivamente o comportamento de falhas da API.
3. Aprovar expressamente o modelo manual atual de recuperação/409 ou autorizar ajuste
   específico de R2; testar repetições HTTP antes de aprovação funcional definitiva.
4. Não habilitar automação de expiração/n8n antes de resolver R3 e o modelo de serviço.
5. Tratar os SQLs como aditivos elegíveis, sem confundir essa análise com permissão
   de execução ou garantia antecipada de preservação. Validar R4 antes de retries automáticos.

Aguardar avaliação do usuário. Nenhuma correção, migration, importação, commit,
deploy, mudança visual ou próxima etapa foi realizada.
