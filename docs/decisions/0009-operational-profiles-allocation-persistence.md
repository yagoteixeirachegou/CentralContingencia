# ADR 0009 — Persistência de perfis e alocações exclusivas

## Escopo

Etapa 2.4B.1: contratos de persistência Application, adapters Infrastructure, EF,
migration gerada/revisada e testes em PostgreSQL descartável. Sem endpoints,
frontend, autorização HTTP nova, aplicação de migration homologada ou integrações.
Reutilizar o domínio da [ADR 0008](0008-operational-profiles-exclusive-allocations.md).

## Mapeamento

profiles.OperationalProfiles mantém UUID, ProfileName convertido para varchar(200),
Platform varchar(100), enum textual Active/Closed e timestamps UTC. O nome não é
único. allocations.ProxyAllocations mantém IDs, ator/chave, estado textual, datas
e motivo de finalização. IsActive é derivado, não coluna. xmin nativo é concurrency
token nas duas entidades, sem criação de coluna física extra no SQL PostgreSQL.

Domain recebeu apenas construtores privados sem parâmetros e setters privados
para materialização. Não referencia EF nem admite mutação pública dos IDs/metadados.
Proxy e seu mapeamento criptográfico permanecem intactos.

FKs ProfileId e ProxyId usam DELETE RESTRICT: nem remoção de perfil nem de proxy
apaga histórico. ActorId é UUID obrigatório sem FK para Identity, conservando a
decisão de retenção de responsáveis independente do ciclo de vida da conta.

Dois índices únicos parciais, filtrados por Status IN ('Reserved','Confirmed'),
garantem uma alocação ativa por proxy e uma por perfil. Prazo ultrapassado não remove
uma reserva do filtro: a transição Expired precisa ser explícita, conforme domínio.
Outro índice único protege ActorId/IdempotencyKey, inclusive em registros encerrados.

Checks protegem IDs não vazios, tamanho/texto, enum/coerência de estados, finitude e
ordenação de datas, prazo de reserva futuro em relação à criação, confirmação anterior
ao prazo e motivos de término. Regras entre linhas/tabelas, como perfil ativo e validade
da proxy, são revalidadas pelo domínio dentro da transação, não por CHECK cross-table.

## Contratos e transações

IOperationalProfileStore oferece criação e leitura por UUID. IAllocationStore oferece
leitura, histórico, reserva, confirmação, liberação, expiração e fechamento do perfil,
como operações internas de persistência, sem implementação de casos de uso HTTP.
Não há repositório genérico, IQueryable público ou detalhe EF na Application.

Cada operação usa DbContext dedicado, construído com as opções registradas, para não
persistir alterações pendentes de Identity/proxies de outro fluxo. Nas mutações:

1. Abrir transação READ COMMITTED com lock_timeout local de cinco segundos.
2. Bloquear linha do perfil FOR UPDATE; quando necessário, bloquear linha da proxy,
   sempre nessa ordem. SQL usa parâmetros, não texto de entrada interpolado.
3. Carregar estado atual e histórico relevante, incluindo UUID e ator/chave da nova
   reserva. Obter horário pelo TimeProvider após os locks e leituras.
4. Invocar AllocationDomainService, sem duplicar as regras de negócio.
5. Salvar e confirmar a transação. Falhas/cancelamento desfazem a transação ao descartá-la.

Reserva e disponibilidade são avaliadas na mesma transação. Locks da proxy impedem
alteração concorrente por seu CRUD durante a checagem; os índices continuam protegendo
contra inserções concorrentes que não seguem o repositório. Fechamento bloqueia o
perfil e grava seu estado junto ao encerramento do vínculo; reserva/confirmacão do
mesmo perfil precisam aguardar. Fechamento não altera a proxy.

Erros de unicidade, xmin, deadlock, serialização e timeout de lock são Conflict;
violações de CHECK/FK/tamanho/nulabilidade são Validation; recursos ausentes são
NotFound; falhas restantes do PostgreSQL são Unavailable. AllocationPersistenceException
retém somente categoria e mensagem estática, sem inner exception, SQL ou detalhes.
Nenhum código HTTP foi implementado nesta etapa.

UUID/chave duplicado retorna conflito, não replay automático. Replay/autorização,
auditoria específica de operações, limites de histórico e serviços Application ficam
para etapa autorizada posterior. A idempotência/auditoria do CSV existente não muda.

## Migration e riscos

20261011223416_AddOperationalProfilesAndAllocations tem Up aditivo: dois schemas,
duas tabelas, seis índices secundários, duas PKs, duas FKs RESTRICT e oito checks.
SQL revisado em ../security/etapa-2.4b1-migration.sql. Nenhum UPDATE/DELETE, ALTER de
tabela existente, recifragem ou mudança de chaves. INSERT de histórico EF apenas
quando a migration for aplicada. Não foi aplicada na homologação.

A futura aplicação pode esperar locks na tabela de proxies ao criar FK; exige
aprovação explícita, revisão do alvo, backup novo e janela adequada. Down remove as
duas tabelas e perderia novo histórico: não executar como recuperação rotineira.
Rollback transacional em testes foi comprovado; recuperação de banco real depende
de procedimento/backup aprovado.

Histórico é mantido pelos repositórios, sem métodos de exclusão. FKs protegem os
recursos, mas não impedem um administrador do banco de apagar/alterar diretamente
linhas de histórico. Permissões operacionais e manutenção devem respeitar retenção.

O histórico carregado é completo para as regras temporais atuais e pode crescer;
otimização/paginação de leitura pública não foi antecipada. Não há job de expiração.
Não atualizar GET /available nesta etapa: nenhuma alocação está exposta ao produto
ou persistida na homologação. A integração futura deve excluir Reserved/Confirmed
da consulta e projeção antes de disponibilizar funcionalidades de alocação.

AES-256-GCM, DPAPI, keyring, Identity, cookies, CSRF, auditorias atuais e contratos
HTTP foram preservados. Não foram introduzidas credenciais nem chamadas externas.
