# Preparação da homologação — Etapas 2.4B.1, 2.4B.2 e 2.4B.3

Data: 11/10/2026. **Preparação concluída; aplicação NÃO executada.**
Lidos integralmente AGENTS.md, CLAUDE.md e os relatórios das três etapas.
Inspecionados os Up/Down das migrations, SQLs completos, catálogo atual, loaders
de conexão/DPAPI, implementação AES e configuração Data Protection da aplicação.

## Parecer

**Recomendar aprovação da aplicação local das duas migrations, na ordem abaixo,
condicionada à autorização explícita do usuário e à repetição das pré-verificações
imediatamente antes da execução.** Não foi encontrado bloqueio técnico de schema
ou incompatibilidade criptográfica nas verificações realizadas.

Isso não declara as etapas funcionalmente homologadas. A nova API não foi
reiniciada; a homologação de runtime deverá ocorrer somente após aplicação e
validação autorizadas. Riscos e limites estão descritos neste documento.

## Migrations exatas, dependências e ordem

Histórico atual possui quatro registros, último
`20261011165123_AddProxyImportBatches`. O CLI EF conectado ao alvo confirmou
**exatamente duas migrations pendentes**:

| Ordem | Migration completa | Dependências e impacto |
| --- | --- | --- |
| 1 | 20261011223416_AddOperationalProfilesAndAllocations | Depende de proxies.Proxies e sua PK UUID existentes; cria schemas profiles/allocations, OperationalProfiles e ProxyAllocations |
| 2 | 20261011230449_AddOperationalAudit | Utiliza schema audit existente; cria OperationalEvents. Segue a primeira na sequência EF; sem FK para os recursos auditados |

A aplicação atual precisa de ambas para disponibilizar integralmente perfis,
alocações e auditoria. A Etapa 2.4B.3 não adicionou migration. O EF confirmou
`No changes have been made to the model since the last migration.`

SQLs inspecionados integralmente:

- [etapa-2.4b1-migration.sql](security/etapa-2.4b1-migration.sql)
  SHA-256 `26537F30BA30324192868E706CAFA227B0E51C62517B203ED7DA082A4443DA95`.
- [etapa-2.4b2-migration.sql](security/etapa-2.4b2-migration.sql)
  SHA-256 `7F346D1D10717BB5CF3528642B1342E81FE85EB858A6EE649A45EDEA6DC842F8`.

Regenerados com início/fim explícitos pelo assembly 2.4B.3, em arquivos locais
ignorados, e comparados por SHA-256: **byte a byte idênticos aos SQLs revisados**.
Nenhum dos SQLs foi enviado ao PostgreSQL para aplicação.

## Resumo do SQL e impacto esperado

Primeira migration: duas tabelas, duas PKs, oito CHECKs, duas FKs ON DELETE RESTRICT
e seis índices secundários. OperationalProfiles guarda UUID, nome, plataforma,
estado e timestamps. ProxyAllocations guarda recursos/ator/chave idempotente,
estado, prazo e histórico. xmin é nativo do PostgreSQL, não nova coluna física.

Índices:

- IX_OperationalProfiles_Status_CreatedAt.
- IX_ProxyAllocations_ActorId_IdempotencyKey, único inclusive no histórico.
- IX_ProxyAllocations_ProfileId_CreatedAt e IX_ProxyAllocations_ProxyId_CreatedAt.
- UX_ProxyAllocations_ActiveProfile e UX_ProxyAllocations_ActiveProxy, únicos
  parciais com filtro Status IN ('Reserved','Confirmed').

CHECKs: CK_Profiles_Id/Name/Platform/State/Times e
CK_Allocations_Ids/State/Times. FKs:
FK_ProxyAllocations_OperationalProfiles_ProfileId e
FK_ProxyAllocations_Proxies_ProxyId, preservando recursos com histórico.

Segunda migration: audit.OperationalEvents, PK_OperationalEvents, quatro CHECKs
CK_OperationalEvents_Ids/Action/Time/Correlation, e dois índices:
IX_OperationalEvents_ProfileId_OccurredAt e
IX_OperationalEvents_AllocationId_OccurredAt. AllocationExpired e o ator de sistema
da Etapa 2.4B.3 são compatíveis com os CHECKs já gerados. Não há credenciais nessa tabela.

**Up aditivo:** nenhum DROP, TRUNCATE, UPDATE, DELETE, ALTER de tabela antiga,
conversão de dados, recifragem ou alteração de Identity/chaves. Cada script tem
START TRANSACTION/COMMIT e um INSERT intencional em __EFMigrationsHistory.
Após aplicação esperada: três tabelas novas vazias e seis entradas no histórico EF.

**Down destrutivo:** remove as três tabelas novas e perderia seus históricos e
auditorias. Não será usado como recuperação automática, especialmente após escrita.

Compatibilidade atual: schemas profiles/allocations ainda ausentes; audit já existe;
nenhum dos nomes das três tabelas ou oito índices novos conflita com o catálogo.
proxies.Proxies.Id é UUID com PRIMARY KEY (Id), compatível com a FK nova. Índices
são criados em tabelas novas vazias, sem verificar unicidade de registros antigos.

## Estado atual e integridade verificada

Alvo conferido pelo loader, catálogo e publicação Docker:
**127.0.0.1:55432 / central_contingencia_dev**, container PostgreSQL local healthy,
PostgreSQL 17.11, tamanho observado 8.418.995 bytes. Docker publica apenas loopback
55432 para 5432. Credenciais de conexão não foram impressas.

Histórico registrado:

1. 20261011015548_InitialIdentityAndAuthenticationAudit.
2. 20261011024141_AddProxyPersistence.
3. 20261011025452_AddProxyAudit.
4. 20261011165123_AddProxyImportBatches.

Contagens e fingerprints MD5 de representação JSON canônica ordenada, sem conteúdo
de registros exposto, capturados antes do backup e comparados novamente ao terminar:

| Tabela | Registros | Fingerprint |
| --- | ---: | --- |
| audit.AuthenticationEvents | 36 | d100d460cc5cc96361baf00e47643121 |
| audit.ProxyEvents | 1 | 5ba744b09f724c0490304f6e050983c8 |
| audit.ProxyImportBatches | 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.ProxyImportLines | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetRoleClaims | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetRoles | 3 | e810e5b2f2c20b88f0876fa4f654035e |
| identity.AspNetUserClaims | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUserLogins | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUserRoles | 4 | 8a07607fc6bed9799f4b16fff007f76b |
| identity.AspNetUserTokens | 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUsers | 4 | ffe7fb1ac91c88cd61863e3abc985f20 |
| proxies.Proxies | 1 | ef5461380fcc7b87c8a7cbcca2463cbd |
| public.__EFMigrationsHistory | 4 | 76cd7ea7b083fadbc106d86ccbeb2dbe |

As 13 tabelas permaneceram idênticas. Comparação com o snapshot anterior da Etapa
2.4B.3 também passou nos schemas identity/audit/proxies. MD5 aqui serve para
detectar alteração de snapshot, não como proteção criptográfica de backup.

Verificações somente leitura: zero índices indisvalid/indisready inválidos, zero
constraints não validadas, zero vínculos usuário/papel órfãos, zero envelopes AES
com formato inválido e zero metadados básicos de proxy inconsistentes. Na amostra
de sessões não havia outra conexão ao banco nem transação aberta. Isso não garante
ausência de tráfego no instante de uma aplicação futura.

O dump completo conseguiu ler o banco. Não foi realizada verificação física
exaustiva de disco/páginas. Uma consulta inicial usou um nome Identity que não existe;
a transação de leitura foi abortada, sem alterações, e a consulta foi corrigida
para AspNetUserRoles/AspNetUsers/AspNetRoles e repetida com sucesso.

## Compatibilidade das chaves

Chave AES atual: development-v1, 32 bytes, carregada da proteção DPAPI existente
somente em memória. Verificações com implementação atual:

- Roundtrip de senha sintética aprovado.
- Dois nonces diferentes para operações iguais.
- Ciphertext adulterado recusado por autenticação GCM.
- O único envelope homologado foi autenticado em transação READ ONLY. Plaintext
  permaneceu em buffer temporário, zerado ao terminar; não foi exibido nem persistido.
- Keyring copiado: duas chaves carregadas e seus encryptors instanciados; roundtrip
  Data Protection sintético com ApplicationName CentralContingencia.P6 aprovado.
  Geração automática explicitamente desabilitada; originais e cópias não mudaram.

Nenhuma alteração de formato de envelope, associated data, KeyId, chave, DPAPI,
application discriminator ou configuração criptográfica pelas migrations.
Não foram criados cookies, sessões ou provas de importação na homologação.

## Backup completo e evidências

Diretório local ignorado e protegido por ACL exclusiva do usuário Windows atual:

`C:\PROJETOS\CENTRALCONTINGENCIA\.local\backups\etapa-2.4b-20261011T235137Z`

Ponteiro: `.local/etapa-2.4b-backup-path.txt`. Evidências:

| Arquivo | Bytes | SHA-256 |
| --- | ---: | --- |
| database.dump | 32266 | 8F7081ED78E6321B0C46D955AE8D0B8C53526CF25FE8B2CE09BB3C05491BE3B0 |
| globals.sql | 677 | 5CBCED1EFFD0D5525ADD749D17C55AA158ECD39CE96F848895D99AFBB2FFAF42 |
| database-fingerprints.txt | 781 | 3D39B072B6FBCDE34542C3C36C2256D3E7D8DDEAECEB0649A63870FEAC50E40B |

database.dump: pg_dump custom completo, incluindo schema, dados, owners, grants,
histórico EF e metadados de criação. globals.sql preserva roles globais; contém
material sensível e não deve ser publicado ou aplicado cegamente sobre outro cluster.
Também copiados proxy-development-v1.dpapi e todo o diretório DataProtection-Keys.
SHA-256 de cada arquivo está no manifest.json privado, junto de tamanho/caminho.
Conteúdo de chaves, dados e roles não consta deste relatório.

Verificações executadas: pg_restore --list, 13 entradas TABLE DATA no catálogo,
pg_restore --file=/dev/null decodificando todos os blocos sem conexão/restore,
comparação SHA-256 do dump entre container e cópia Windows, validação de todo o
manifesto após os testes criptográficos, igualdade das cópias DPAPI/keyring com
originais, ACL de todos os arquivos e imutabilidade das 13 tabelas após backup.
Cópias temporárias de dump/globals no container foram removidas depois da validação.

Script local `.local/prepare-24b-backup.ps1` teve sintaxe PowerShell validada antes
da execução. Probe criptográfico em `.local/24b-crypto-check`, ignorado pelo Git;
nenhuma implementação da aplicação foi modificada para essas verificações.

**Limite:** não foi executado ensaio de restauração efetiva em outro banco. Leitura
integral/decodificação/hashes comprovam disponibilidade e legibilidade do arquivo,
não uma restauração operacional completa. Backup DPAPI depende da preservação do
mesmo perfil Windows e contexto de proteção; não é uma cópia portátil da chave em claro.
O backup local não substitui uma política futura de cópia externa protegida.

## Testes repetidos

Executada seleção atual, com build, em PostgreSQL descartável e dados sintéticos:
AllocationDomainTests, ReservationReliabilityTests (dois projetos),
AllocationPersistenceTests e OperationalEndpointTests.

**28 unitários + 42 de integração = 70 aprovados, zero falhas ou ignorados.**
Incluem expiração no prazo, disponibilidade readonly, recuperação concorrente,
confirmação idempotente/conflitante, reutilização, índices de exclusividade,
fechamento/histórico, rollback e HTTP 500 sanitizado em falhas de auditoria.
Também revalidam permissões, CSRF, OpenAPI e upgrade aditivo preservando proxy
sintética no banco descartável. Zero bancos p6_auth_test_* remanescentes.

```powershell
. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES = $env:ConnectionStrings__DefaultConnection
try {
  dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b3/ --filter 'FullyQualifiedName~ReservationReliabilityTests|FullyQualifiedName~AllocationDomainTests|FullyQualifiedName~AllocationPersistenceTests|FullyQualifiedName~OperationalEndpointTests'
} finally { Remove-Item Env:CENTRAL_TEST_POSTGRES }
```

Os 361 testes .NET e 104 frontend do relatório 2.4B.3 são evidência histórica;
nesta preparação foi repetida a seleção de 70, além das verificações criptográficas.
Nenhum login/escrita de teste foi feito na API homologada.

## Bloqueios, transações e riscos

DDL das tabelas novas obtém locks de catálogo/objetos; criação da FK para proxies
pode aguardar locks na tabela referenciada e conflitar com operações concorrentes.
Índices normais são criados em tabelas novas vazias. Não há CREATE INDEX CONCURRENTLY
ou SQL sem transação nesses scripts. Cada migration tem sua transação; se a primeira
confirmar e a segunda falhar, a primeira permanece aplicada. Não supor atomicidade
conjunta ao executar as duas separadamente.

Planejar janela sem escritores, repetir snapshot/backup antes de aplicar caso haja
mudança, limitar espera por locks e não iniciar nova API entre as duas aplicações.
Após aplicação, validar três tabelas vazias, oito índices, três PKs, doze CHECKs,
duas FKs RESTRICT e seis migrations registradas. Dados anteriores devem conservar
os fingerprints; o fingerprint do histórico EF mudará intencionalmente.

O lock de tabela das operações da 2.4B.3 pode limitar throughput; não foi feito
benchmark. Expiração física é sob demanda, sem job. Reserve/release ainda retornam
409 em retry. O risco R4 de leitura de disponibilidade pós-commit de escrita de
proxy permanece documentado; não justifica prometer rollback ou retry cego de POST.
Esses limites não tornam as migrations destrutivas, mas devem integrar a revisão
funcional antes da homologação definitiva.

## Aplicação futura proposta — NÃO EXECUTADA

Depois de autorização: confirmar alvo exato, exatamente duas pendências, hashes
dos SQLs/backup, disponibilidade DPAPI/keyring, ausência de divergências, espaço e
janela sem escrita. Se qualquer verificação divergir, interromper antes de aplicar.
Usar targets explícitos, conferindo o histórico após cada passo:

```powershell
# SOMENTE APÓS AUTORIZAÇÃO EXPLÍCITA E PRÉ-VERIFICAÇÕES APROVADAS.
. ./scripts/Set-LocalDatabase.ps1
$env:BaseOutputPath = 'C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b3/'
try {
  dotnet ef database update 20261011223416_AddOperationalProfilesAndAllocations --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
  # Interromper em falha e verificar o registro/estrutura antes do segundo passo.
  dotnet ef database update 20261011230449_AddOperationalAudit --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
} finally { Remove-Item Env:BaseOutputPath }
```

Os comandos acima são proposta para revisão, não script pronto para execução
automática: cada passo exige conferência de sucesso/estado, timeout e ausência de
novas pendências. Não executar migrations add nem update sem target.

## Plano de recuperação

1. Em falha, interromper e manter a versão anterior da API; coletar diagnóstico
   sanitizado, migrations registradas, catálogo e fingerprints. Não repetir automaticamente.
2. Falha antes de commit deve desfazer a migration corrente. Confirmar isso em
   leitura; não presumir rollback apenas pela mensagem do cliente.
3. Se a primeira confirmou e a segunda falhou, preservar esse estado e diagnosticar.
   A versão anterior não depende das tabelas novas. Completar a segunda ou desfazer
   schema exige decisão/autorização posterior, sem apagar histórico automaticamente.
4. Se houver alteração inesperada dos registros antigos, bloquear uso/escritas e
   preservar evidências. Não restaurar sobre a homologação automaticamente.
5. Com autorização de recuperação, conferir manifestos e hashes; restaurar primeiro
   o archive em banco separado, com alvo distinto e pg_restore --exit-on-error,
   respeitando metadados/roles. Não usar --create/--clean contra o alvo homologado
   sem plano específico, pois podem recriar/substituir objetos.
6. Comparar schema, todas as contagens/fingerprints e envelopes; somente depois
   autorizar eventual substituição do banco. globals.sql não deve modificar roles
   de outros bancos; conferir identidade/owners antes de usá-lo.
7. Reutilizar as chaves originais. Se recuperação das cópias for necessária, verificar
   igualdade e restaurar no mesmo perfil/contexto DPAPI mediante autorização,
   sem gerar chaves novas. Validar AES e Data Protection sem expor valores.
8. Reiniciar a API apenas após schema completo, preservação validada e autorização;
   smoke tests de escrita devem continuar em bancos descartáveis. Homologação
   funcional das etapas depende dessa validação posterior.

**Aguardar autorização explícita.** Não foram aplicadas migrations, reiniciada API,
alterados registros/chaves, executadas importações, commits, deploys ou frontend.
