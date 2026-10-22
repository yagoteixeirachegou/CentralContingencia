# Preparação da homologação 2.4C.3

Data: **12/10/2026**. Implementação aprovada tecnicamente pelo usuário.
**Parecer: favorável à aplicação controlada da única migration identificada abaixo,
após autorização explícita e nova conferência imediata do alvo/backup/baseline.**
Esta preparação não autoriza nem executa recuperação de perfis reais.

## Alvo, estado e pendência

AGENTS.md, CLAUDE.md e relatório 2.4C.3 lidos; contratos, parser, serviços/stores,
políticas/CSRF, Up/Down e SQL incremental revisados.

Loader existente conferido exclusivamente em **127.0.0.1:55432 /
central_contingencia_dev**. Docker Compose publica somente 127.0.0.1:55432 → 5432;
container PostgreSQL saudável, versão **17.11**. Consulta READ ONLY confirmou o
nome do banco. Credenciais da conexão não foram exibidas.

EF migrations list confirmou **sete aplicadas e exatamente uma pendente**:

**20261012131756_AddProfileRecoveryReceipts**.

Dependência imediata: **20261012102737_AddProfileAssignments**, já aplicada.
As anteriores são InitialIdentityAndAuthenticationAudit, AddProxyPersistence,
AddProxyAudit, AddProxyImportBatches, AddOperationalProfilesAndAllocations e
AddOperationalAudit. Nenhuma outra migration poderá ser aplicada nesta homologação.
Estado repetido ao final: migration nova registrada = 0, tabelas de recuperação = 0.

Integridade lógica amostrada: zero constraints não validadas, zero índices inválidos
ou não prontos, três PKs esperadas nas tabelas referenciadas e zero transações abertas
de outras conexões no momento da consulta. Isso não equivale a certificação física
integral do PostgreSQL ou garantia de ausência de concorrência na execução futura.

## SQL integral e impacto

Arquivo: [security/etapa-2.4c3-migration.sql](security/etapa-2.4c3-migration.sql).
SHA-256 **856362CFBABF69A8AAB33746F248A11FC2BB830206713B2A7FD95F12DBE831BC**.
SQL regenerado explicitamente entre a migration anterior e a nova em
`.local/24c3-preparacao-generated.sql`; hash idêntico ao arquivo revisado.
EF confirmou ausência de mudanças de modelo após a migration.

Up inspecionado integralmente:

- Cria audit.ProfileRecoveryBatches: IDs, fingerprint protegido, data/correlação
  e contagens; PK e quatro CHECKs de IDs, contagens, timestamp e correlação.
- Cria audit.ProfileRecoveryLines: BatchId/Line, resultado e UUIDs opcionais dos
  recursos; PK composta e CHECK de linha/resultado/recursos obrigatórios.
- Quatro FKs: RESTRICT para proxies.Proxies, profiles.OperationalProfiles e
  allocations.ProxyAllocations; CASCADE exclusivamente do recibo para suas linhas.
- Cinco índices explícitos, incluindo único ActorId/IdempotencyKey, data e três
  FKs opcionais. Duas PKs geram mais dois índices físicos.
- Insere somente o registro autorizado em __EFMigrationsHistory, dentro da transação.

**Nenhuma operação destrutiva no Up**: sem ALTER de objetos existentes, UPDATE,
DELETE, TRUNCATE, DROP, alteração de credenciais, recifragem ou dados operacionais.
As duas novas tabelas deverão começar vazias. Contagens/fingerprints das 16 tabelas
atuais devem permanecer iguais, exceto a inclusão intencional da migration no
histórico EF. Estruturas anteriores devem ser byte-logicamente equivalentes no
comparador de fingerprints; novas tabelas precisam ser validadas explicitamente.

As FKs referenciam PKs UUID existentes e não impõem validação nova às linhas antigas,
porque as tabelas de origem serão novas/vazias. DDL/FKs podem adquirir locks breves
nas tabelas referenciadas; planejar janela sem operações administrativas e limitar
espera. Não foi feito ensaio de carga/bloqueios no banco homologado.

Down também revisado: remove apenas as duas tabelas novas, perdendo recibos e
idempotência caso já contenham dados. **Down não será utilizado automaticamente**.
Não deve ser tratado como recuperação segura depois de gravações.

## Backup completo verificado

Diretório local, ACL sem herança, FullControl para o usuário Windows atual:

`C:\PROJETOS\CENTRALCONTINGENCIA\.local\backups\etapa-2.4c3-preparacao-20261012T142014Z`

| Arquivo | Bytes | SHA-256 |
|---|---:|---|
| database.dump | 66251 | 111F656A70B9FBFE2C4E322B07F3828FE30E4744F27CBCB0E2C018903BA706A7 |
| globals.sql | 677 | 9273242848BD51331F9C5AA42A7500585F03025DA6D9F68896C7B26A3868EAA7 |

pg_dump custom completo com --create: schema/dados/owners/grants do banco atual.
pg_dumpall --globals-only preserva roles. Também copiados DPAPI, os dois arquivos
do keyring, fingerprints de dados/estruturas e catálogo do archive. Manifesto de
oito artefatos com tamanho/SHA-256 revalidado; cópias de chave iguais às originais.
Dump/globals contêm material sensível: permanecer somente nesse diretório protegido.

Verificação realizada:

1. pg_restore --list leu o catálogo.
2. pg_restore --file=/dev/null decodificou integralmente o archive, sem conexão a
   banco ou retenção de SQL com dados em plaintext.
3. SHA-256 do dump dentro do container igual ao arquivo Windows.
4. Fingerprints de dados do backup coincidem com o baseline das 16 tabelas.
5. Manifesto e chaves originais/copiadas conferidos novamente após testes.
6. Cópias temporárias do container removidas; backup protegido permanece disponível.

Script local `.local/prepare-24c3-backup.ps1` passou no parser PowerShell antes da
execução. Não executou migration ou restore. **Não houve ensaio de restauração do
backup em outro banco**: decodificação/hashes provam legibilidade/integridade dos
artefatos, mas não substituem validação operacional de um restore autorizado.

## Baseline das 16 tabelas

Arquivos `.local/24c3-preparacao-homolog-before.txt` e `.structure.txt`, também
copiados para o backup como database-fingerprints.txt e
database-structure-fingerprints.txt. Comparador existente revalidou integralmente
dados e estruturas ao final; nenhuma exclusão usada nesta preparação.
Fingerprints MD5 do conjunto ordenado de linhas, conforme mecanismo existente:

| Tabela | Registros | Fingerprint |
|---|---:|---|
| allocations.ProxyAllocations | 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.AuthenticationEvents | 38 | dac53d29bbc8bdc58225cab65f6d7ba6 |
| audit.OperationalEvents | 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.ProxyEvents | 131 | f9bb63ede01e38ead81d410ef42add37 |
| audit.ProxyImportBatches | 1 | 33baeb82e4e069f3d62d0b8c7f3eadbb |
| audit.ProxyImportLines | 132 | a65b91b2cba838e4f269d67c75c3192e |
| identity.AspNetRoleClaims | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetRoles | 3 | e810e5b2f2c20b88f0876fa4f654035e |
| identity.AspNetUserClaims | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUserLogins | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUserRoles | 4 | 8a07607fc6bed9799f4b16fff007f76b |
| identity.AspNetUserTokens | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUsers | 4 | ffe7fb1ac91c88cd61863e3abc985f20 |
| profiles.OperationalProfiles | 0 | d41d8cd98f00b204e9800998ecf8427e |
| proxies.Proxies | **131** | f1f2b42e10fc6573a75f93b777e7fcf6 |
| public.__EFMigrationsHistory | 7 | 975f0f2a5b175ef38379960344cace61 |

Há 130 UUIDs de proxies importadas nos recibos e uma preexistente. Zero perfis e
alocações. Nenhum login de teste, prévia com arquivo real, importação ou recuperação
foi executado nesse banco. Fingerprint integral de Proxies preservado inclui
credenciais/envelopes, status, região e timestamps, sem expor seus valores.

## DPAPI, AES e keyring

Arquivo DPAPI existente acessível pelo perfil Windows atual, chave development-v1
válida para AES-256-GCM. Roundtrip sintético, nonces distintos e adulteração rejeitada
aprovados usando o mecanismo existente. **131 envelopes existentes autenticados
em transação READ ONLY**, com plaintext apenas em buffers temporários zerados,
sem valores retornados, logados ou persistidos.

Cópia do keyring carregou dois encryptors e passou roundtrip Data Protection com
ApplicationName CentralContingencia.P6, **geração automática desabilitada**.
Arquivo DPAPI e os dois XMLs permaneceram com os mesmos SHA-256/conjunto; não
houve regeneração, substituição ou rotação. Hashes/tamanhos das cópias constam no
manifesto local; segredos não são reproduzidos neste relatório.

## Revisão funcional e segurança

[Contrato de prévia/commit](api/profile-recovery.md) revisado com implementação:

- Pareamento é host canônico de ProxyHost + porta validada, com exatamente um
  resultado. Nunca ordem de linha, aproximação textual, username/password ou
  equivalência PERFIL=PROXY. Nomes não são identificadores globais confiáveis.
- Duplicatas de perfil/endpoint, múltiplas correspondências, perfil existente sem
  comprovação do vínculo exato, reserva/vínculo conflitante e proxy indisponível
  geram impedimento. Não associar automaticamente ou sobrescrever vínculos.
- PLATAFORMA duplicada até duas ocorrências usa posições dos slots; células vazias
  continuam null. Cabeçalhos ambíguos bloqueiam; catálogo permanece autoridade.
- Prévia usa parser limitado e snapshot REPEATABLE READ/READ ONLY, sem SaveChanges,
  criação de perfis/alocações, auditoria de negócio, CSV persistido ou credenciais.
  Emite somente prova protegida stateless com validade de 15 minutos.
- Prova vincula ator, arquivo/opção e plano/estado. Commit reenvia CSV, revalida
  sob locks/transação; divergência exige nova prévia. Erro/conflito bloqueia lote
  inteiro, sem atualizações de proxies/credenciais. Reservas expiradas só são
  finalizadas/auditadas durante commit, reutilizando infraestrutura homologada.
- Idempotência ator/chave e fingerprint protegido retornam recibo histórico sem
  duplicação de recursos/auditoria. Mesma chave com outra fonte/opção retorna 409.
- Perfil, alocação, recibo e auditoria são atômicos; falha de auditoria causa rollback
  e 500 sanitizado. Concorrência/unicidade/timeouts retornam 409 sanitizado.
- Admin exigido na política HTTP e Application; cookies HTTPS/HttpOnly e CSRF
  compartilhados, rate limiting sensitive, no-store e limites multipart. Respostas,
  erros e auditorias não incluem credenciais. Sem tokens/CSV em cache persistente.

Esta revisão não aprova vínculos de nenhuma linha real. Fonte não foi analisada;
quantidade recuperável e impedimentos reais deverão ser avaliados em prévia futura,
com autorização própria. Associação recuperada representa o momento atual, não
datas históricas presumidas.

## Testes executados nesta preparação

| Verificação | Resultado atual |
|---|---|
| Build solução .NET em saída isolada | Aprovado, zero erros/avisos |
| Unitários completos | **237 aprovados** |
| Integração PostgreSQL/HTTP completa | **179 aprovados** |
| Total .NET | **416 aprovados**, zero falhas/ignorados |
| SQL regenerado | Idêntico por SHA-256 ao revisado |
| Consistência modelo/migration | Sem mudança pendente de modelo |
| Backup/hash/decodificação | Aprovados |
| AES/DPAPI/keyring | Aprovados, sem alteração de chaves |
| Snapshot final homologado | Dados/estruturas das 16 tabelas preservados |
| Fixtures p6_auth_test_* após testes | Zero remanescentes |

Os 30 casos próprios da recuperação e a regressão anterior cobrem pareamento,
duplicidades/ambiguidades, vínculos existentes, prova/opções/ator, expiração,
idempotência, concorrência, rollback de auditoria, preservação dos envelopes/histórico,
Admin/Manager/Operator, autenticação/CSRF e ausência de credenciais em logs/respostas
observados. Escritas/migrations dos testes ocorreram somente em p6_auth_test_UUID,
criado e removido pela fixture a partir de conexão administrativa ao banco postgres.

Saídas isoladas `.local/build-24c3-preparacao/`; resultados
`.local/24c3-preparacao-test-results/` e `.local/24c3-preparacao-tests.log`.
Harness criptográfico existente reutilizado com cópia do keyring no backup.
Não foram alterados fontes, frontend ou contratos, nem reiniciada a API local.
Frontend/Playwright não reexecutados: escopo é preparação de banco e revisão backend.

## Plano futuro de aplicação e validação — não executado

1. Receber autorização explícita para **somente** AddProfileRecoveryReceipts.
2. Revalidar alvo, única pendência, SQL/hash, backup/manifesto/chaves e baseline.
   Se houve mudança legítima desde a preparação, apresentar divergência antes de
   prosseguir; não ignorar ou sobrescrever fingerprints silenciosamente.
3. Aplicar migration alvo explicitamente, sem importação/recuperação nem outra migration.
4. Conferir histórico EF, duas tabelas vazias, tipos/constraints/FKs e sete índices
   físicos esperados. Validar estruturas novas contra o SQL revisado, sem ignorar
   tabelas desconhecidas. Comparar as 16 anteriores, descontando **somente** o
   novo registro autorizado no histórico EF; preservar todo o histórico anterior.
5. Confirmar 131 proxies, envelopes/chaves e demais fingerprints. Só depois considerar
   reinício autorizado da API com configuração/chaves existentes e smoke sem efeitos
   persistentes. Qualquer teste com escrita continua em banco descartável.
6. Recuperação real permanecerá bloqueada até autorização própria e revisão de prévia.

Comando alvo futuro, **não executado**:

```powershell
dotnet ef database update 20261012131756_AddProfileRecoveryReceipts --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure
```

## Riscos residuais e plano de recuperação

Risco de perda de dados pelo Up é baixo: DDL aditivo em transação, sem DML operacional.
Locks/FKs podem aguardar operações concorrentes; planejar janela e interromper em
timeout/divergência. Estado deve ser novamente conferido no instante da aplicação.
Backup requer conservação do diretório, ACL, usuário Windows DPAPI e keyring.
Recibos futuros dependem dessas chaves e não têm expurgo implementado. Riscos de
volume/locks e strings gerenciadas do upload continuam documentados na ADR 0012.

Se a migration falhar antes do commit: interromper, inspecionar histórico/objetos
em leitura e confirmar rollback transacional. Não executar novo update, Down,
restore ou correção manual automaticamente. Preservar diagnóstico sanitizado.

Se houver divergência depois do commit: manter a versão anterior da API, suspender
a ativação do módulo e apresentar o diagnóstico. Não apagar tabelas ou recibos
para recuperar por tentativa. Qualquer procedimento adicional exige aprovação.

Se restauração for necessária, conferir hashes e recuperar primeiro em ambiente
isolado explicitamente autorizado, validando schema, 16 fingerprints, envelopes e
keyring; somente então planejar troca/restore do banco local. Não sobrescrever
diretamente a homologação com backup antigo nem aplicar globals.sql indiscriminadamente.
Não restaurar/substituir chaves já íntegras; conservar cópias protegidas para desastre.
Não houve ensaio de restore nesta preparação e nenhum desses passos foi executado.

**Preparação aprovada; aplicação ainda não autorizada nem executada.**
Somente artefatos locais de preparação/backup/build/testes e este relatório foram
criados. Sem recuperação real, CSV real, escrita operacional homologada, migration,
reinício, modificação de chaves, commit, deploy ou integração n8n/AdsPower.
Aguardar autorização explícita do usuário antes da aplicação.
