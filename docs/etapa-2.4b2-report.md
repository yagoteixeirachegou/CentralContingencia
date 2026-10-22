# Etapa 2.4B.2 — APIs de Perfis Operacionais e Alocações

Data: 11/10/2026. Implementação e testes concluídos, aguardando revisão.
Nenhuma migration aplicada na homologação; nenhuma próxima etapa iniciada.
AGENTS.md, CLAUDE.md e relatórios 2.4A/2.4B.1 inspecionados antes de implementar.
Domínio, serviços, contratos, DbContext, repositórios, middleware e fixtures revisados.
SDK observado: 10.0.300; nenhuma dependência nova.

## Endpoints e permissões

| Endpoint | Sucesso | Permissão |
| --- | --- | --- |
| GET /api/v1/profiles | 200 paginado | Admin/Manager/Operator |
| GET /api/v1/profiles/{id} | 200 | Leitura |
| POST /api/v1/profiles | 201 + Location | Admin/Manager |
| PATCH /api/v1/profiles/{id} | 200 | Escrita |
| POST /api/v1/profiles/{id}/close | 204 | Escrita |
| GET /api/v1/profiles/{id}/allocations | 200 histórico paginado | Leitura |
| GET /api/v1/allocations | 200 paginado | Leitura |
| GET /api/v1/allocations/{id} | 200 | Leitura |
| POST /api/v1/allocations/reserve | 201 + Location | Escrita |
| POST /api/v1/allocations/{id}/confirm | 204 | Escrita |
| POST /api/v1/allocations/{id}/release | 204 | Escrita |

Operator não realiza mutações. Papéis desconhecidos/Integration permanecem recusados.
Políticas Operations/Management e autorização também na Application. Reutilizados
cookies Identity, HTTPS, CSRF, no-store, limite JSON e rate limiting sensitive.
Contratos completos em [api/operational-profiles-allocations.md](api/operational-profiles-allocations.md).

## Implementação e decisões

OperationalManagementService na Application implementa os casos de uso, autorização,
DTOs seguros e validação dos parâmetros. Fábricas/Value Objects e AllocationDomainService
continuam protegendo invariantes. Profile.UpdateDetails é a única nova operação de
domínio de perfil: valida antes de mutar, preserva UUID/status e recusa alteração de
Closed. Plataforma permanece texto validado, sem catálogo enum inventado.

PATCH permite name/platform, omitidos preservados, null/objeto vazio/duplicatas/campos
desconhecidos recusados. Encerramento é ação explícita; não há reabertura. Reservas
ocupam os dois recursos; confirmar revalida validade/prazo/estado. Expiração continua
explícita e sem endpoint/job novo. Idempotency-Key UUID obrigatório na reserva;
repetição retorna 409, sem replay. Após resposta incerta, consultar histórico antes
de iniciar outra operação.

Persistência reutiliza transações e locks da 2.4B.1. Auditoria grava junto da operação,
inclusive fechamento do perfil e finalização do vínculo; nenhuma gravação parcial.
Conflitos retornam 409, falhas de persistência 503, validação 400, recursos ausentes 404,
sem SQL ou detalhes internos. Fechamento repetido preserva data e não duplica auditoria.

Listagens server-side com page 1..10000 e pageSize 1..100; filtros exatos e whitelists
de ordenação, desempate UUID. Histórico respeita perfil da rota. Leituras de total e
itens não formam snapshot transacional. Atualizações de metadados são serializadas
e usam os campos atuais; não foi criado ETag para detecção de edição antiga do cliente.

Disponibilidade agora exclui Reserved/Confirmed em listagem, detalhe, resposta PATCH
e /available de proxies. Formato dos DTOs/rotas preservado; consultar disponíveis não
reserva. A autoridade da reserva permanece a checagem dentro da transação e os índices
únicos. CSV continua importando somente proxies, sem criar vínculos/perfis.

Nenhuma alteração AES-256-GCM, DPAPI, keyring ou credencial real. Respostas de alocação
contêm somente IDs/estados/datas, sem host, username, password ou envelope. Eventos
auditáveis contêm ator, perfil/alocação, ação, UTC e correlation ID, sem metadados
textuais, conteúdo de requisição, token ou segredo.

## Migration adicional necessária à auditoria

**20261011230449_AddOperationalAudit**, gerada sem aplicação homologada. SQL incremental
revisado em [security/etapa-2.4b2-migration.sql](security/etapa-2.4b2-migration.sql).
Cria somente audit.OperationalEvents, PK_OperationalEvents, quatro checks
CK_OperationalEvents_Ids/Action/Time/Correlation e dois índices:
IX_OperationalEvents_ProfileId_OccurredAt e IX_OperationalEvents_AllocationId_OccurredAt.
Sem alterações em tabelas ou registros existentes. Down remove a nova tabela e perderia
auditoria; não executado. Não há migrations automáticas no startup.

Foi mantida separada da migration 2.4B.1 já revisada. Ambas estão pendentes na
homologação. Sua aplicação requer autorização própria e backup/revisão do alvo.

## Builds e testes

| Verificação final | Resultado |
| --- | --- |
| Build completo .NET | Aprovado, zero erros/avisos |
| Unitários | 192 aprovados |
| Integração PostgreSQL descartável | 147 aprovados |
| Total | **339 aprovados, zero falhas/ignorados** |
| EF has-pending-model-changes | Sem mudanças após a migration |
| SQL adicional | Inspecionado integralmente; Up aditivo |
| Snapshot homologado antes/depois | Contagens/fingerprints idênticos |
| Bancos descartáveis remanescentes | Zero |

26 casos novos: 12 unitários de autorização antes de persistir, criação/reuso das
invariantes, metadados inválidos, paginação, perfil terminal e auditoria; 14 integração
de cadastro/consulta/PATCH/filtros/ordenação/paginação, matriz completa de papéis,
alocação válida e duplicada, concorrência pelos dois recursos, fechamento/reutilização,
proxies vencidas/Suspended/Disabled, recursos ausentes, CSRF ausente/adulterado,
JSON inválido, falta de chave, auditoria sanitizada e rollback em falha de auditoria.
OpenAPI verifica sessão, CSRF, Idempotency-Key, limites e DTO sem senha. Disponibilidade
verificada nos contratos existentes de proxies, inclusive listagem/detalhe/PATCH.

Regressão completa dos testes existentes: Identity, autenticação, lockout, logout,
autorização, criptografia, auditoria, CRUD de proxies, CSV, prova/idempotência,
transações, persistência e arquitetura. Nenhuma escrita de teste na homologação.
Hosts HTTP novos usam contador de rate limit isolado e limites funcionais ampliados
apenas nas fixtures; limites de produção preservados e testes específicos existentes
continuam executados. Não foi necessário frontend/lint/typecheck/Playwright porque
nenhum arquivo frontend mudou.

Comandos principais:

```powershell
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b2/
. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES=$env:ConnectionStrings__DefaultConnection
try {
  dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b2/
} finally { Remove-Item Env:CENTRAL_TEST_POSTGRES }
# Geração histórica; não repetir migrations add para iniciar a aplicação.
$env:BaseOutputPath='C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b2/'
try {
  dotnet ef migrations add AddOperationalAudit --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build --output-dir Persistence/Migrations
  # Depois de compilar a migration gerada:
  dotnet ef migrations script 20261011223416_AddOperationalProfilesAndAllocations --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build --output docs/security/etapa-2.4b2-migration.sql
  dotnet ef migrations has-pending-model-changes --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
} finally { Remove-Item Env:BaseOutputPath }
```

Ocorrências corrigidas: fake unitário atualizado para novo contrato de disponibilidade;
assert de timestamp ajustado à precisão PostgreSQL de microssegundos; contador HTTP
dos novos testes isolado após um teste antigo receber 429; SQL inicialmente vazio por
assembly anterior à compilação da migration, regenerado/revisado após build. Rodada
final completa aprovada. Nenhum teste removido ou ignorado.

## Preservação, arquivos e limitações

Snapshot .local/24b2-before.txt comparado no encerramento: todas as tabelas monitoradas
preservadas, quatro migrations anteriores, nenhuma nova aplicada. Zero bancos de teste
remanescentes. Apenas leitura foi realizada na homologação; nenhuma conta, proxy,
alocação, auditoria ou chave homologada foi alterada por esta execução.

API local anterior não foi reiniciada: sua versão mantém funcionamento com o schema
atual. **Não iniciar esta compilação contra a homologação antes de aplicar as duas
migrations mediante autorização.** Os endpoints novos foram validados somente em
TestServer com PostgreSQL descartável; não houve homologação manual ou navegador.

Arquivos principais: Application/Allocations (contratos/serviço), Domain/Profile
(UpdateDetails) e Audit/OperationalAuditEvent, Infrastructure/Allocations, DbContext/DI,
Proxies/EfProxyStore e respectivos contratos/serviço, Api/Operations e Program/OpenAPI,
migration/Designer/snapshot, testes unitários/HTTP, SQL, guia API, AGENTS/CLAUDE e relatório.

Pendências: revisão do usuário; homologação das migrations; teste local autenticado
após autorização; catálogo de plataforma; integração futura de frontend; expiração
agendada e eventual replay/ETag. Nenhuma dessas funcionalidades foi iniciada.
Sem frontend, integrações n8n/AdsPower, dados reais, commits ou deploys.
**Etapa 2.4B.2 implementada e validada automaticamente, aguardando revisão.**
