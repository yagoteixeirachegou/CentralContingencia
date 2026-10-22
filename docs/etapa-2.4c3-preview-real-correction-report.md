# Correção da prévia real 2.4C.3 — INFOPRODUTO

Data: 12/10/2026. **Correção implementada e testada; recuperação real bloqueada.**
Nenhuma confirmação, migration homologada ou gravação operacional executada.

## Implementação

INFOPRODUTO adicionado ao catálogo exato ProfileAssignments, compartilhado pelos
dois slots opcionais de OperationalProfile. Application, parser, validação dos
contratos de cadastro/edição, filtros e descrição OpenAPI usam esse catálogo.
O catálogo TypeScript foi atualizado: formulário/Zod dos dois slots e dropdown
do grid passam a oferecer o mesmo valor. Sem conversão de outras classificações,
sem redesign e sem alteração das regras de reserva, exclusividade ou disponibilidade.

Arquivos de produto alterados: Domain/Profiles/ProfileAssignments.cs;
frontend/features/proxies/grid/contracts.ts; migration/Designer/model snapshot
em Infrastructure/Persistence/Migrations. Contratos HTTP permanecem compatíveis,
com ampliação do catálogo; documentação atualizada em docs/api/operational-grid.md
e docs/api/profile-recovery.md. Testes em OperationalGridTests, ProfileRecoveryTests,
ProfileRecoveryEndpointTests e ProfilesPage.test.tsx.

O parser já preservava PLATAFORMA duplicada por posição, sem deslocar valores.
A opção existente skipRowsWithoutConnection=true foi reutilizada. Só permite
ignorar conexão inteiramente vazia; não aceita linhas parcialmente preenchidas.
O motivo NoConnectionSkipped e a linha física permanecem no resultado. Essa opção
participa da prova protegida e da idempotência; no commit futuro, o resultado
ignorado comporia o recibo e a auditoria existentes. Nenhum recibo foi criado agora.

## Migration necessária, NÃO aplicada

**20261012174401_AddInfoproductProfileAssignment**, dependente de
20261012131756_AddProfileRecoveryReceipts. É a única pendente na homologação.

SQL integral revisado em [security/etapa-2.4c3-infoproduto-migration.sql](security/etapa-2.4c3-infoproduto-migration.sql):
transação que substitui exclusivamente CK_Profiles_Platform1 e CK_Profiles_Platform2
por checks com os 17 valores anteriores mais INFOPRODUTO, mantendo IS NULL.
O INSERT em __EFMigrationsHistory registra somente a aplicação futura.
Não contém UPDATE/DELETE de dados, remoção de colunas/tabelas, alteração de proxies,
FKs, índices de exclusividade, auditoria ou criptografia. Compatível com todos os
valores anteriormente aceitos e perfis sem atribuições.

ALTER TABLE exige bloqueio e validação dos checks; planejar aplicação controlada
com backup e janela breve. Down volta ao catálogo anterior e pode falhar se houver
INFOPRODUTO persistido: não remover nem converter essas classificações para forçar
rollback. Não foi executado Up ou Down no banco homologado.

## Nova prévia somente leitura

Original autorizado em Downloads, 15.734 bytes, 11 colunas e 132 registros.
Arquivo não editado, removido, reconstruído nem copiado para o repositório.

**Distinção de execução:** a API HTTPS https://localhost:7090 foi verificada
com health 200, principal Admin existente e CSRF pelos mecanismos homologados,
sem login/logout que produziriam auditoria. Como há migration pendente, a API
homologada NÃO foi reiniciada com o código novo: ainda usa o catálogo anterior.
A prévia corrigida foi executada pelo mesmo caso de uso IProfileRecoveryService
em harness isolado, compilado com o código atualizado, usando conexão local
guardada 127.0.0.1:55432/central_contingencia_dev e default_transaction_read_only=on.
O store também abre REPEATABLE READ/SET TRANSACTION READ ONLY.
Não se trata de uma resposta HTTP corrigida emitida pela API 7090. O contrato
HTTP atualizado foi testado com banco descartável e CSV sintético.

Principal Admin, cookie e CSRF somente em memória; sem senhas de login.
Não houve nenhuma chamada ao commit. O harness não possui caminho de confirmação.
Prova não emitida porque persistem conflitos. Bytes zerados ao final; campos
temporários descartados. Strings gerenciadas não asseguram apagamento físico.
Artefato local sanitizado: .local/24c3-corrected-preview-summary.json.

| Indicador | Total |
|---|---:|
| Registros analisados | 132 |
| Linhas com proxy correspondente única | 131 |
| Perfis distintos válidos identificados | 130 |
| Registros elegíveis / propostas | 129 |
| Atribuições recuperáveis nas propostas | 129 |
| Ignorados | 1 |
| Inválidos | 0 |
| Conflitantes | 2 |
| Sem proxy correspondente / ambíguos | 0 / 0 |
| Plataforma 2 preenchida na fonte / prévia | 0 / 0 |
| Prova de confirmação | Não emitida |
| Chamadas à confirmação | 0 |

129 + 2 + 1 = 132. Elegibilidade individual não permite persistência parcial.
131 endpoints válidos correspondem a 131 proxies distintas, sem aproximação,
pareamento por ordem, nome, usuário ou senha. Linha ignorada não é ProxyNotFound.

Plataforma 1 identificada em 131 linhas: ESTEIRA DE PERFIL 116, ANUNCIANTE 9,
INFOPRODUTO 4, X1 1, MODERAÇÃO 1; inclui as duas conflitantes. Plataforma 1
recuperável em 129 propostas; Plataforma 2 recuperável em zero. As linhas 44–47
deixaram de ser inválidas. Status, região, datas e credenciais existentes continuam
sob autoridade do banco, sem atualização a partir da fonte.

## Impedimentos e intervenção manual

* Linhas **22–23**: mesmo Perfil **1218**, proxies diferentes, DuplicateProfile
  em ambas. Não escolher uma, unir identidades ou criar duas alocações ativas.
  Administrador precisa esclarecer a identificação e o vínculo correto.
* Linha **131**: ausência confirmada de host, porta, usuário e senha. Ignorada
  explicitamente com NoConnectionSkipped; nenhuma operação proposta. Não inventar
  perfil, proxy ou atribuição. Numeração física original preservada.

## Recuperação parcial segura

**Não existe mecanismo de exclusão de conflitos no contrato atual.** Preview não
emite prova com Conflict/Invalid; commit revalida o arquivo integral e aborta o
lote, com transação, rollback, auditoria e exclusividade preservados. O backend
não aceita seleção de itens manipuláveis do frontend. skipRowsWithoutConnection
não pode ser usado para excluir as linhas 22–23, que têm conexão preenchida.

Se aprovada posteriormente, uma exclusão explícita poderia vincular números de
linhas originais e justificativas ao hash do arquivo, administrador, prova e
idempotência; revalidar tudo sob transação e relatar as exclusões sanitizadas.
Isso exige decisão, contrato e testes adicionais, não implementados nesta tarefa.
Outra alternativa é corrigir uma cópia da fonte mediante decisão humana e executar
nova prévia dos bytes dessa cópia; o original nunca deve ser alterado automaticamente.
Nenhuma alternativa autoriza confirmação agora. Atualizar o catálogo não resolve
a identidade duplicada nem autoriza recuperar 129 registros isoladamente.

## Testes e verificações reais

* dotnet build CentralContingencia.slnx com BaseOutputPath isolado: aprovado,
  zero avisos/erros.
* dotnet test CentralContingencia.slnx --no-build --no-restore, CENTRAL_TEST_POSTGRES
  apontando ao banco administrativo local postgres e fixtures UUID descartáveis:
  **421 aprovados, zero falhas/ignorados: 241 unitários + 180 integração**.
* Regressões novas: INFOPRODUTO em ambos os slots ou somente um; slot vazio
  preservado; recuperação/constraints em PostgreSQL descartável; proxy cifrada
  preservada; duplicidade 1218 bloqueada; ausência total de conexão ignorada com
  motivo; preenchimento parcial continua inválido. Suítes existentes cobrem
  concorrência, rollback, idempotência, expiração, Admin/CSRF e respostas sanitizadas.
* npm run lint e npm run typecheck: aprovados.
* npm test -- --maxWorkers=1: **146 aprovados em seis arquivos**. Primeira execução
  paralela teve três timeouts de carregamento sob compilação concorrente;
  reexecução sequencial integral aprovada sem alterar timeouts ou código funcional.
* npm run build: aprovado. Avisos existentes de comentários PURE no Zod/Rollup,
  sem impedir o build.
* node scripts/Verify-OperationalGridUi.cjs: **11 verificações Chrome/mocks
  aprovadas**, todas as APIs interceptadas, sem escrita homologada. Não é teste
  de recuperação real no navegador. Novo teste de componente verifica dropdowns
  INFOPRODUTO e ausência de preenchimento automático no segundo slot.
* EF has-pending-model-changes: nenhuma divergência modelo/snapshot. EF migrations
  list: oito aplicadas e somente a migration INFOPRODUTO pendente; consulta sem Up.

Harness inicial interrompido por resolução de IConfiguration ausente; registro
adicionado somente no harness isolado antes do resultado final. Instrumentação
auxiliar de etapa teve erro de compilação, corrigido antes da nova execução.
Nenhuma regra de produto foi contornada. Primeiro scaffold vazio usava assembly
anterior; descartado e regenerado com BaseOutputPath correto, sem aplicação.

## Preservação e segurança

Snapshot fresco .local/24c3-correction-before.txt e sidecar de estruturas,
capturado antes da nova prévia. Comparação final integral: **as 18 tabelas mantêm
contagens, fingerprints e estruturas idênticos, sem exclusões**.

| Objeto | Antes | Depois |
|---|---:|---:|
| Proxies | 131 | 131 |
| Perfis / alocações | 0 / 0 | 0 / 0 |
| Recibos / linhas de recuperação | 0 / 0 | 0 / 0 |
| Auditorias de autenticação | 38 | 38 |
| Migrations aplicadas | 8 | 8 |

Fingerprint de proxies: f1f2b42e10fc6573a75f93b777e7fcf6, preservado.
Hashes/tamanhos do CSV, arquivo DPAPI que protege a chave AES e arquivos do keyring
comparados antes/depois e idênticos. Data Protection no harness sem geração
automática; nenhuma chave substituída. AES e envelopes não alterados.
Prévia não salva perfil/alocação/recibo/auditoria. DTO sem usuário/senha/envelope;
corpos não gravados. Respostas e logs runtime comparados aos valores sensíveis
conhecidos da conexão/sessão/CSRF: nenhuma ocorrência observada; testes sintéticos
verificam ausência de credenciais. Não houve alteração das 131 proxies.

## Parecer e próximos passos

Correção de catálogo pronta para revisão. **Lote ainda bloqueado**, apesar de 129
propostas elegíveis. Antes de uso homologado da correção: aprovação específica
da migration, pré-verificações/backup, aplicação controlada e reinício da API;
depois nova prévia HTTP. Antes de recuperação real: resolver Perfil 1218 ou aprovar
e implementar mecanismo seguro de exclusão; revisar plano/estado concorrente,
backup e autorização explícita de confirmação.

Sem recuperação real, migrations homologadas, importação de proxies, alterações
de credenciais/chaves, commits, deploys ou integrações. **Aguardar revisão.**
