# Preparação da atualização 2.4C.3 — INFOPRODUTO

Data: 12/10/2026. **Parecer favorável à aplicação controlada da migration,
condicionado a autorização explícita e repetição das pré-verificações.**
Recuperação real permanece bloqueada por decisão de negócio pendente.
AGENTS.md, CLAUDE.md e relatório da correção revisados. Nenhuma mudança de produto,
restart da API, migration homologada ou confirmação realizada nesta preparação.

## Migration e SQL integral

Nome: **20261012174401_AddInfoproductProfileAssignment**.
Dependência: **20261012131756_AddProfileRecoveryReceipts**, já aplicada.
Consulta EF atual com conexão local em modo READ ONLY: oito migrations aplicadas;
somente AddInfoproductProfileAssignment pendente. Alvo conferido:
**127.0.0.1:55432 / central_contingencia_dev**, exclusivamente homologação local.

Arquivo SQL revisado integralmente:
[security/etapa-2.4c3-infoproduto-migration.sql](security/etapa-2.4c3-infoproduto-migration.sql).
SHA-256: `6180179CCE7B417C7C6853D7DBB998BC4C16E21B2CED859314E6151DD58997E3`.
Up, Down, snapshot e configuração EF inspecionados.

| Objeto | Alteração prevista |
|---|---|
| profiles.OperationalProfiles / CK_Profiles_Platform1 | Substituir check pelo catálogo anterior mais INFOPRODUTO, preservando IS NULL |
| profiles.OperationalProfiles / CK_Profiles_Platform2 | Mesma ampliação independente |
| public.__EFMigrationsHistory | Inserir exclusivamente o registro da nova migration quando autorizada |
| Colunas, tipos, nulabilidade e tabelas | Nenhuma alteração |
| Índices, FKs e exclusividade de alocações | Nenhuma alteração |
| Enumerações PostgreSQL | Nenhuma alteração: slots são strings com CHECK, não enums nativos |
| Dados operacionais, credenciais e estruturas de recuperação | Nenhuma alteração |

**Aditiva na semântica do catálogo, não destrutiva para dados.** O SQL contém
DROP CONSTRAINT seguido de ADD CONSTRAINT dos dois checks dentro da mesma
transação; portanto não é exclusivamente composto de comandos ADD.
Não contém DROP TABLE/COLUMN, UPDATE ou DELETE. Todos os 17 valores anteriores
continuam válidos; INFOPRODUTO torna-se o 18º valor. Não existe conversão de dados,
renumeração, nova associação ou escrita nas 131 proxies.
As duas tabelas de recibos e suas constraints/FKs permanecem intactas.

## Segurança e compatibilidade funcional

* ProfileAssignments aceita INFOPRODUTO em qualquer slot, em ambos simultaneamente
  ou com o outro null. Cadastro, edição e validação de recuperação reutilizam
  essa autoridade. OpenAPI deriva a descrição do mesmo catálogo.
* Formulário/Zod e dropdown do grid usam o catálogo TypeScript atualizado.
  Teste de componente confirma opção nos dois slots e valores inicialmente vazios.
* PLATAFORMA duplicada continua mapeada por posição. A segunda coluna vazia não
  recebe cópia da primeira: Platform2 permanece **null**.
* DuplicateProfile nas linhas 22–23 continua impedindo emissão de prova e
  confirmação. Não houve escolha de proxy para Perfil 1218.
* Linha 131 pode ser ignorada com **skipRowsWithoutConnection=true**, somente
  porque host/porta/usuário/senha estão todos vazios. Aviso NoConnectionSkipped,
  linha original, resultado Ignored e nenhuma operação proposta. Preenchimento
  parcial continua bloqueado. Opção ligada ao fingerprint/prova e idempotência.
* Prévia não grava; confirmação futura revalida sob transação e mantém Admin,
  CSRF, idempotência, concorrência, exclusividade, rollback e auditoria.
* Auditabilidade existente: recibo futuro registra administrador, horário,
  correlation ID, totais, número da linha e outcome Ignored; não registra CSV ou
  credenciais. **Código detalhado NoConnectionSkipped e justificativa textual não
  são colunas do recibo**: aparecem na prévia e devem constar na documentação
  sanitizada da aprovação. Nenhum recibo/auditoria de recuperação criado agora.

API homologada não reiniciada: seu assembly ainda usa catálogo anterior. Os testes
abaixo validam o código corrigido; uso HTTP homologado de INFOPRODUTO depende de
aplicação autorizada da migration e reinício posterior validado.

## Situação dos registros da fonte

Resultados da prévia corrigida anterior, executada em harness somente leitura,
documentada em [relatório da correção](etapa-2.4c3-preview-real-correction-report.md).
Nesta preparação não foi reenviado CSV nem executado commit. Hash original e
estado operacional comparados novamente sem divergência; elegibilidade final
deve ser reavaliada em nova prévia HTTP após atualização, pois validade e vínculos
são verificados no momento da operação.

| Indicador | Quantidade |
|---|---:|
| Registros da fonte | 132 |
| Correspondências únicas de proxy | 131 |
| Perfis distintos válidos identificados | 130 |
| Propostas elegíveis / atribuições recuperáveis | 129 / 129 |
| Conflitos | 2 |
| Ignorados | 1 |
| Inválidos / ambiguidades / sem correspondência | 0 / 0 / 0 |
| Plataforma 2 preenchida | 0 |

Quatro INFOPRODUTO, linhas 44–47, passaram a ser elegíveis. **Os 129 elegíveis
continuam apenas propostas**: não podem ser confirmados isoladamente pelo contrato
atual, que exige arquivo integral sem Conflict/Invalid. Não há exclusão de conflitos
na confirmação; skip de conexão vazia não se aplica às linhas 22–23.

## Estratégia proposta para CSV corrigido e recuperação integral

Não criar nem editar uma cópia nesta preparação. Após decisão humana:

1. Preservar original imutável fora do repositório e registrar SHA-256, tamanho
   e versão. Hash conferido: `A7441ABD055FC9277A7C0C11F182ED0BFE77AA73686BC43A579A08BAA13A37AE`;
   15.734 bytes. Tratar qualquer cópia como arquivo sensível com acesso restrito.
2. Administrador esclarecer se 1218 representa dois perfis distintos com
   identificação incorreta ou um único perfil com duas associações propostas.
   No primeiro caso, fornecer a identificação verdadeira de cada perfil; não
   criar sufixos ou IDs arbitrários. No segundo, indicar explicitamente o vínculo
   atual correto e a destinação da outra linha, pois exclusividade impede ambos.
3. Produzir cópia versionada apenas com correções humanas aprovadas. Preservar
   conexão/credenciais e classificações originais, inclusive INFOPRODUTO e slot 2
   vazio. Manter linha 131 e opção explícita de ignorar ausência total de conexão.
   Nenhuma alteração de região, validade ou status é necessária para a recuperação.
4. Registrar manifesto sanitizado: hashes original/corrigido, responsável, data,
   justificativa e campos/linhas modificados; sem senhas, usuários de proxy,
   cookies ou provas. Se houver remoção de linha da **cópia** aprovada pelo humano,
   documentar exclusão e mapa de linhas original→cópia; não perder rastreabilidade
   nem tratar número físico como identidade. Arquivar evidência sensível separada,
   fora de Git e logs. Não remover linhas automaticamente.
5. Executar nova prévia do arquivo corrigido inteiro; hash alterado invalida a
   prova anterior. Conferir correspondência exata, exclusividade e propostas.
   Só emitir/usar prova nova quando não houver conflito ou invalidade. Revisar
   ignorados, obter autorização específica e reenviar exatamente bytes/opções.
6. Recuperação integral significa todos os registros efetivamente aprovados da
   cópia em uma transação, não criar um perfil para toda proxy. Se forem dois
   perfis distintos legítimos, o resultado pode atingir 131 elegíveis + 1 ignorado,
   sujeito a nova validação. Se for um único perfil, não é possível alocar as duas
   proxies simultaneamente: o total dependerá da destinação humana da outra linha.

Essa estratégia usa contrato existente e não exige mecanismo novo de exclusão.
Se a preferência for excluir conflitos mantendo todos os bytes originais, será
necessário aprovar outra evolução: linhas/justificativas explicitamente vinculadas
à prova, hash, administrador e idempotência, revalidadas e auditadas. Não implementada.

## Backup atual e chaves

Backup novo completo preparado por pg_dump --format=custom --create e pg_dumpall
--globals-only. Diretório local ignorado, ACL restrita ao usuário Windows:
`.local/backups/etapa-2.4c3-update-preparation-20261012T175512Z`.

* database.dump: **73.769 bytes**, SHA-256
  `BE55F49C3C622EBAD34219FB9FD7FD3C4E4D3233F41E564FF212320B526BF4D1`.
* pg_restore --list: catálogo legível com **18 tabelas**, incluindo recibos atuais.
* pg_restore --file=/dev/null: arquivo integral decodificado sem conexão/restore.
* Hash do dump copiado corresponde ao arquivo produzido no container.
* Manifesto com **8 artefatos**: dump, globals, catálogo, fingerprints de dados,
  fingerprints de estruturas, DPAPI e dois arquivos do keyring; tamanhos/hashes
  integralmente conferidos. Cópias temporárias removidas por caminhos explícitos.
* DPAPI e dois arquivos do keyring idênticos ao baseline anterior e ao backup.
* AES-256-GCM/DPAPI: roundtrip sintético, nonces distintos e adulteração rejeitada;
  **131 envelopes existentes autenticados em transação READ ONLY**, sem saída ou
  persistência de plaintext; buffers sensíveis zerados pelo harness.
* Keyring de backup: duas chaves carregadas, roundtrip sintético aprovado,
  geração automática desabilitada. Sem regeneração/substituição de chaves.

Limitação: integridade/decodificação do backup validada, **restauração não executada**.
Não restaurar esse dump com dados reais como parte dos testes sintéticos.

## Fingerprints e estado homologado

Snapshot fresco `.local/24c3-update-preparation-before.txt` e sidecar de estruturas.
Baseline da correção também revalidado. Comparação final integral: **18 tabelas
com contagens, fingerprints e estruturas idênticos**, sem exclusões.

| Tabela | Contagem | Fingerprint dos dados |
|---|---:|---|
| allocations.ProxyAllocations | 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.AuthenticationEvents | 38 | dac53d29bbc8bdc58225cab65f6d7ba6 |
| audit.OperationalEvents | 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.ProfileRecoveryBatches | 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.ProfileRecoveryLines | 0 | d41d8cd98f00b204e9800998ecf8427e |
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
| proxies.Proxies | **131** | **f1f2b42e10fc6573a75f93b777e7fcf6** |
| public.__EFMigrationsHistory | 8 | 9af2716c876028faf575b5cc3f455a08 |

## Testes executados nesta preparação

dotnet test CentralContingencia.slnx --no-build --no-restore, BaseOutputPath isolado,
filtro ProfileRecovery/OperationalGrid/AesGcm: **60 aprovados, zero falhas/ignorados
(41 unitários + 19 integração)**. CENTRAL_TEST_POSTGRES usa postgres administrativo
local; fixtures criam/migram bancos p6_auth_test_UUID e removem somente esses bancos.
Dados exclusivamente sintéticos. Cobertura: INFOPRODUTO em ambos os slots, null,
recuperação sem modificar proxy, duplicate profile bloqueado, skip explícito,
arquivo/opções/prova, idempotência, concorrência, rollback/auditoria, Admin/CSRF,
segredos ausentes, grid e constraints no PostgreSQL descartável.
TRX em `.local/24c3-update-preparation-tests`.

npm test -- --maxWorkers=1 ProfilesPage.test.tsx OperationalGridPage.test.tsx:
**42 aprovados**, mocks, incluindo dropdowns INFOPRODUTO e slot 2 vazio.
Builds/lint/typecheck e suíte integral anteriores aprovados conforme relatório
da correção (421 .NET/146 frontend/11 Chrome). Nesta tarefa documental não houve
alteração de produto; esses builds não foram repetidos. Harness criptográfico
existente executado com configurações atuais e sem escrever dados operacionais.

## Riscos, aplicação futura e recuperação de falhas

ALTER TABLE pode aguardar ACCESS EXCLUSIVE e valida os checks. Hoje não há perfis,
mas disponibilidade pode mudar: repetir contagens, fingerprints, conexão, hashes,
SQL e migrations imediatamente antes de aplicação. Planejar timeout e interromper
se houver divergência. Aplicar exclusivamente a migration identificada somente
com autorização; conferir histórico/checks/dados/chaves antes de reiniciar a API.

Se falhar antes do commit, a transação deve preservar checks/histórico; verificar
o estado em leitura e apresentar diagnóstico antes de qualquer recuperação.
Não realizar Down automaticamente: depois de existir INFOPRODUTO, o catálogo
antigo rejeita esses perfis. Priorizar diagnóstico/correção aprovada; restauração
de backup exige autorização, ambiente e procedimento próprios, nunca sobrescrever
o banco automaticamente. Backups DPAPI dependem do contexto Windows atual para uso.

**A atualização de schema é recomendada; a recuperação do CSV continua bloqueada.**
Aprovação da migration não autoriza confirmar recuperação. Resolver as linhas
22–23 com decisão humana e nova prévia; linha 131 permanece ignorada explicitamente.
Não há senhas/provas/cookies no relatório, nem alteração das 131 proxies, perfis,
CSV original ou chaves. Sem commits, deploys ou integrações.
**Aguardar autorização explícita.**
