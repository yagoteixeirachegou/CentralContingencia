# Etapa 2.4B.1 — Persistência de Perfis Operacionais e Alocações

Data: 11/10/2026. Infraestrutura implementada e validada em bancos descartáveis.
**Migration homologada não aplicada.** Nenhuma Etapa 2.4B.2 iniciada.

AGENTS.md, CLAUDE.md e relatório 2.4A lidos; domínio, DbContext, repositórios,
registro DI e fixtures inspecionados antes das alterações. Plano apresentado.
EF Core 10/Npgsql e dependências existentes preservados, sem pacotes adicionais.

## Implementação

- Application/Allocations/AllocationPersistenceContracts.cs: IOperationalProfileStore,
  IAllocationStore, comando interno de reserva e erro categorizado de persistência.
  Sem IQueryable, EF ou contratos HTTP.
- Infrastructure/Allocations: EfOperationalProfileStore, EfAllocationStore e
  AllocationPersistenceErrors. Contexto dedicado por operação evita salvar alterações
  pendentes de outros fluxos; DI usa opções EF e TimeProvider existentes.
- Configurações EF específicas para OperationalProfile e ProxyAllocation, DbSets no
  contexto existente, Value Object ProfileName com conversão por valor, enums em
  texto, timestamps UTC e xmin nativo como concurrency token.
- Domain: apenas construtores privados sem parâmetros e setters privados para
  materialização das duas entidades. As fábricas, regras e dependências do domínio
  permanecem intactas; IDs não são mutáveis pela API pública.
- Reserva transacional: lock da linha do perfil, depois da proxy; carregar estado
  atual e histórico relevante; obter horário após a espera pelos locks; reutilizar
  AllocationDomainService; SaveChanges e commit únicos. READ COMMITTED, timeout de
  lock de cinco segundos. Índices únicos são a proteção final contra concorrência.
- Confirmação/liberação/expiração transacionais. Fechamento grava perfil encerrado
  e finalização da alocação juntos. Histórico permanece, e a proxy não é alterada.
- Unicidade/xmin/deadlock/serialização/timeout traduzidos para Conflict; checks/FKs/
  limites para Validation; recurso ausente para NotFound; outras falhas PostgreSQL
  para Unavailable. Exceção sanitizada não retém SQL, parâmetros ou inner exception.

Não foram criados endpoints, casos de uso HTTP, frontend ou integrações.
Autenticação, CSRF, AES-256-GCM, DPAPI, keyring, auditorias e importação existentes
não foram alterados. Não houve recifragem nem alteração de proxies homologadas.
Decisões na [ADR 0009](decisions/0009-operational-profiles-allocation-persistence.md).

## Migration e SQL revisados

Migration: **20261011223416_AddOperationalProfilesAndAllocations**, com designer e
snapshot EF atualizados. SQL incremental desde AddProxyImportBatches em
[security/etapa-2.4b1-migration.sql](security/etapa-2.4b1-migration.sql).
Up e SQL foram inspecionados integralmente. Nenhum UPDATE/DELETE, DROP ou ALTER de
tabela existente no Up. INSERT no histórico EF ocorre somente quando aplicada.

Tabelas novas:

| Tabela | Conteúdo |
| --- | --- |
| profiles.OperationalProfiles | UUID, Name, Platform, Status, CreatedAt, UpdatedAt, ClosedAt |
| allocations.ProxyAllocations | UUID, ProxyId, ProfileId, ActorId, IdempotencyKey, Status, CreatedAt, UpdatedAt, ReservationExpiresAt, ConfirmedAt, EndedAt, EndReason |

Schemas novos: profiles e allocations. xmin é nativo do PostgreSQL: apesar da
metadata rowVersion no C# gerado, o SQL não cria coluna física xmin.

Índices secundários gerados:

| Nome | Regra |
| --- | --- |
| IX_OperationalProfiles_Status_CreatedAt | Consulta de perfis por estado/data |
| UX_ProxyAllocations_ActiveProxy | ProxyId único quando Status IN ('Reserved','Confirmed') |
| UX_ProxyAllocations_ActiveProfile | ProfileId único com o mesmo filtro |
| IX_ProxyAllocations_ActorId_IdempotencyKey | Unicidade de ator/chave, inclusive histórico |
| IX_ProxyAllocations_ProfileId_CreatedAt | Histórico por perfil/data |
| IX_ProxyAllocations_ProxyId_CreatedAt | Histórico por proxy/data |

Constraints geradas:

- PK_OperationalProfiles e PK_ProxyAllocations.
- CK_Profiles_Id, CK_Profiles_Name, CK_Profiles_Platform, CK_Profiles_State e
  CK_Profiles_Times: UUID não vazio, texto/limites, Active/Closed e datas coerentes.
- CK_Allocations_Ids, CK_Allocations_State e CK_Allocations_Times: UUIDs obrigatórios,
  estados/motivos, timestamps finitos/ordenados, confirmação antes do prazo,
  expiração no prazo ou depois e término coerente com UpdatedAt.
- FK_ProxyAllocations_OperationalProfiles_ProfileId e
  FK_ProxyAllocations_Proxies_ProxyId, ambas ON DELETE RESTRICT, protegendo histórico.

Total: duas tabelas, seis índices secundários, duas PKs, oito checks e duas FKs.
Nome do perfil não é único. ActorId é UUID obrigatório, sem nova FK para Identity.

## Testes e resultados

| Verificação final | Resultado |
| --- | --- |
| Build completo da solução .NET | Aprovado, zero erros/avisos |
| Unitários .NET | 180 aprovados, zero falhas/ignorados |
| Integração PostgreSQL | 133 aprovados, zero falhas/ignorados |
| Total .NET | **313 aprovados** |
| EF has-pending-model-changes | Nenhuma alteração de modelo após a migration |
| Snapshot homologado antes/depois | Contagens e fingerprints idênticos |
| Bancos p6_auth_test_* remanescentes | Zero |
| Tabelas novas na homologação | Ausentes |

Um novo teste unitário valida o contrato de mapeamento, os dois índices, FKs RESTRICT,
concurrency token e conversão segura do ProfileName. Quatorze novos casos de integração
cobrem materialização, histórico, confirmação/fechamento/reutilização, concorrência
por proxy e por perfil com exatamente um vencedor, unicidade de ator/chave entre
recursos distintos, índices recusando inserções que ignoram o repositório, tradução
de violação real de unicidade e ausência de detalhes sensíveis na exceção.

Também cobrem expiração/liberação, perfil encerrado, proxy bloqueada/vencida, IDs e
prazos inválidos, recursos ausentes, FKs contra remoção de recursos históricos,
checks contra corrupção de estado, espera pelo lock seguida de revalidação do
estado ou do horário, rollback do fechamento em falha de gravação e upgrade desde
o schema anterior preservando proxy já existente, seu envelope e metadados.

Todos os testes anteriores foram executados novamente, incluindo autenticação,
papéis, lockout, CSRF, criptografia, auditoria, CRUD, parser, prova/idempotência CSV,
concorrência e arquitetura. Escritas usam somente fixtures PostgreSQL descartáveis
e dados sintéticos. Injeção de falha adiciona/remove CHECK apenas nessa fixture.
Não houve login ou escrita de teste no banco homologado.

Comandos executados:

```powershell
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b1/
# Conexão somente em memória, via loader existente; fixtures trocam o banco antes de gravar.
. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES=$env:ConnectionStrings__DefaultConnection
try {
  dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b1/
} finally { Remove-Item Env:CENTRAL_TEST_POSTGRES }

# Geração/revisão apenas; nenhum database update foi executado.
$env:BaseOutputPath='C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b1/'
try {
  dotnet ef migrations add AddOperationalProfilesAndAllocations --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build --output-dir Persistence/Migrations
  dotnet ef migrations script 20261011165123_AddProxyImportBatches --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build --output docs/security/etapa-2.4b1-migration.sql
  dotnet ef migrations has-pending-model-changes --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
} finally { Remove-Item Env:BaseOutputPath }
```

O comando migrations add documentado é histórico desta execução; não repetir para
iniciar a aplicação. Saída de build isolada preserva a API em execução. A primeira
tentativa de passar -p:BaseOutputPath ao CLI EF foi recusada pelo parser, antes de
qualquer operação; corrigida usando propriedade MSBuild de processo. Dois avisos
EF1002 no SQL de injeção de falha foram eliminados usando DDL estático. Rodada final
sem avisos, falhas ou testes ignorados. Não foram necessários testes/builds frontend,
lint/typecheck ou Playwright, pois nenhum arquivo frontend foi alterado.

## Preservação da homologação

Verificações somente leitura pelo script Compare-LocalDatabaseSnapshot e psql
local. Snapshot inicial em .local/24b1-before.txt, apenas contagens/hashes; comparação
final idêntica em todas as tabelas monitoradas. Estado final:

| Item | Quantidade preservada |
| --- | ---: |
| Usuários | 4 |
| Auditorias de autenticação | 36 |
| Proxies | 1 |
| Auditorias de proxies | 1 |
| Lotes/linhas CSV | 0 / 0 |
| Migrations aplicadas | 4 |

O histórico permanece com InitialIdentityAndAuthenticationAudit, AddProxyPersistence,
AddProxyAudit e AddProxyImportBatches. A migration nova não está registrada; ambas as
tabelas novas permanecem ausentes. A contagem atual de autenticação difere de relatórios
históricos anteriores, mas não mudou durante esta execução, conforme snapshot inicial.
Não foi necessário backup novo porque nenhuma alteração homologada foi executada.
Servidores existentes não foram reiniciados; nenhuma chave foi carregada/regenerada.

## Riscos e pendências

Migration Up aditiva, sem alteração de linhas/colunas existentes. FKs novas podem
aguardar locks na tabela de proxies na futura aplicação; aprovação explícita,
backup novo e conferência do alvo continuam obrigatórios. Down remove as duas
tabelas e perderia seu histórico: não foi executado e não deve ser usado como
recuperação automática. Não há aplicação de migrations no startup.

Garantias concorrentes comprovadas em PostgreSQL descartável, sem promessa sobre
escritas administrativas diretas que violem regras cross-table do domínio. Locks
podem causar conflito/timeout de cinco segundos. Histórico completo pode crescer;
otimização/paginação pública, política de retenção e expiração agendada são futuras.

Sem replay novo, endpoints, autorização/auditoria específica de novas operações,
importação operacional ou interface. A idempotência do armazenamento recusa chave
duplicada; casos de uso futuros poderão implementar replay sem repetir a operação.
GET /available e suas projeções atuais precisam considerar as novas alocações antes
de expor o módulo em etapa posterior. Permanecem intactos nesta etapa para preservar
os endpoints homologados e não exigir tabela ainda não aplicada no banco local.

Arquivos adicionais: mapeamentos, repositórios/contratos, ajustes privados de domínio,
DbContext/DI, migration/Designer/snapshot, AllocationPersistenceTests.cs,
AllocationPersistenceModelTests.cs, ADR 0009, SQL, AGENTS.md, CLAUDE.md e este relatório.
Nenhuma alteração de frontend, criptografia, registros homologados, commit ou deploy.
**Etapa 2.4B.1 concluída; aplicação homologada da migration aguarda autorização própria.
Etapa 2.4B.2 não iniciada.**
