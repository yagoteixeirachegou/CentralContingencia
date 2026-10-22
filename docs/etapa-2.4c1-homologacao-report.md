# Homologação 2.4C.1 — Grid Operacional Unificado

Data: **12/10/2026**. Execução controlada autorizada pelo usuário.
**Parecer: homologação técnica local aprovada no escopo abaixo; aguardando revisão.**
Não iniciar a Etapa 2.4C.2 automaticamente.

## Pré-verificações e migration autorizada

AGENTS.md, CLAUDE.md e relatório 2.4C.1 lidos; Up/Down e SQL incremental inspecionados
integralmente. Conexão pelo loader existente conferida exclusivamente em
**127.0.0.1:55432 / central_contingencia_dev**. Credenciais não exibidas.

EF confirmou seis migrations aplicadas e exatamente uma pendente:
**20261012102737_AddProfileAssignments**, após
20261011230449_AddOperationalAudit. A lista foi repetida imediatamente antes da
aplicação; script PowerShell passou no parser antes de ser executado.

SQL aprovado: [security/etapa-2.4c1-migration.sql](security/etapa-2.4c1-migration.sql),
SHA-256 `031C75CD11965695D4A587E3774E4ED0BE62E807FE81A0782EB2872C0817D293`.
Somente profiles.OperationalProfiles recebe alterações:

- Platform1 e Platform2 varchar(100), nullable, sem defaults.
- CK_Profiles_Platform1 e CK_Profiles_Platform2: null ou rótulo do catálogo de 17 atribuições.
- Platform legado passa a permitir null; texto e CHECK legado preservados.
- Registro intencional no histórico EF, dentro da transação de migration.

Não há UPDATE, DELETE, TRUNCATE, remoção de coluna/tabela, recifragem ou alteração
de Proxies/Identity/alocações/auditorias no Up. A expansão inclui ALTER em coluna
existente; não foi confundida com criação exclusivamente de objetos novos.
Down não executado na homologação; perderia atribuições e possui guarda de null legado.

Antes da aplicação: 131 proxies, zero perfis/alocações; zero constraints não
validadas, índices inválidos/não prontos ou transações de outras conexões na amostra.
Essas verificações não equivalem a certificação física integral do PostgreSQL.

## Backup e chaves

Backup completo atualizado **após a importação das 131 proxies**, em diretório local
ignorado e com ACL restrita ao usuário Windows:

`C:\PROJETOS\CENTRALCONTINGENCIA\.local\backups\etapa-2.4c1-20261012T112806Z`

| Artefato | Bytes | SHA-256 |
| --- | ---: | --- |
| database.dump | 64755 | 25E6E2B3887412BE04C0A740F65AC3A6A92B2E0F052FB3C6F778F7CBC1A46F15 |
| globals.sql | 677 | 8EFFCCE8364DF8FB75C3C1319070ADBF69D3F94FB7B4DBFC029371EC8E4CD906 |

pg_dump custom completo, metadados de criação/owners/grants e dados; globals preserva
roles. Copiados arquivo DPAPI existente e dois arquivos do keyring Data Protection.
Inclui manifesto com tamanhos/SHA-256, catálogo pg_restore e fingerprints de dados.
Todos os hashes revalidados antes da aplicação e depois dos testes.

pg_restore --list e --file=/dev/null concluíram: archive integralmente decodificado
sem enviar SQL a banco ou guardar dump SQL em texto. Hash do dump no container
comparado ao arquivo Windows. Cópias temporárias do container removidas.
Não foi executado ensaio de restauração dos dados: legibilidade/hash não garantem
sozinhos uma restauração operacional completa. Backup permanece local e protegido.
globals.sql e o dump contêm material sensível; não publicar ou aplicar cegamente.

DPAPI acessível pelo perfil Windows atual; AES-256-GCM com chave de 32 bytes:
roundtrip sintético, nonces distintos e rejeição de adulteração aprovados antes
e depois da execução. **131 envelopes existentes autenticados em READ ONLY**,
com plaintext somente em buffers temporários zerados; nenhum valor retornado/salvo.
Keyring copiado carregou dois encryptors e validou roundtrip com ApplicationName
CentralContingencia.P6, geração automática desabilitada. Conjunto e bytes das chaves
originais idênticos ao backup ao terminar; nenhuma regeneração/substituição.

## Execução e validação estrutural

Comando efetivamente executado, após todas as guardas:

```powershell
dotnet ef database update 20261012102737_AddProfileAssignments --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
```

Conexão e BaseOutputPath fornecidos somente ao processo pelo loader/propriedade
existentes; binário da Etapa 2.4C.1 em `.local/build-24c1/`.
EF adquiriu lock de migration, informou Applying migration para o target exato,
Done e código 0. **Nenhuma outra migration aplicada.** Nenhuma importação ou edição
manual operacional. Comparação pós-migration aprovada antes de iniciar a API.

Histórico final: sete migrations, adição única do target com ProductVersion 10.0.8.
Platform/Platform1/Platform2 conferidas como varchar(100), nullable, sem default;
os dois novos CHECKs estão validados e contêm o catálogo exato. PKs/índices/FKs e
constraints anteriores preservados. Número de tabelas continua 16.

Para comparação de schema, criado banco/role descartáveis via harness existente,
com migrations conhecidas incluindo o target. A estrutura de todas as outras
15 tabelas teve de coincidir com o baseline homologado; só a tabela de perfis
forneceu o fingerprint novo esperado. Referência removida ao terminar.
O comparador não ignorou estruturas ou tabelas desconhecidas: exigiu conjunto
completo e schema exato. Nenhuma correção de produto/comparador foi necessária.

Artefatos de referência/baseline:
`.local/24c1-homolog-before.txt`, sidecar `.structure.txt`,
`.local/24c1-reference.structure.txt` e `.local/24c1-expected.structure.txt`.
Schema de profiles.OperationalProfiles: fingerprint anterior
`22d977f84cb09c8ac190f067b0e14b35`; esperado/final
`2fa58ec8f139a25c1cbc64646addea40`.

## Comparação das 16 tabelas

Fingerprints de dados: MD5 de JSON ordenado, sem conteúdo exposto, usados apenas
para detectar mudanças de snapshot. Todos os registros anteriores foram preservados;
para History excluiu-se somente a inserção autorizada, exigindo também ID/version
corretos. Não houve exclusão genérica de dados ou aceitação de alterações operacionais.

| Tabela | Antes → depois | Fingerprint dos registros anteriores preservado |
| --- | ---: | --- |
| allocations.ProxyAllocations | 0 → 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.AuthenticationEvents | 37 → 37 | f3a08b901092cb4da9bac9c11b5c5da5 |
| audit.OperationalEvents | 0 → 0 | d41d8cd98f00b204e9800998ecf8427e |
| audit.ProxyEvents | 131 → 131 | f9bb63ede01e38ead81d410ef42add37 |
| audit.ProxyImportBatches | 1 → 1 | 33baeb82e4e069f3d62d0b8c7f3eadbb |
| audit.ProxyImportLines | 132 → 132 | a65b91b2cba838e4f269d67c75c3192e |
| identity.AspNetRoleClaims | 0 → 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetRoles | 3 → 3 | e810e5b2f2c20b88f0876fa4f654035e |
| identity.AspNetUserClaims | 0 → 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUserLogins | 0 → 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUserRoles | 4 → 4 | 8a07607fc6bed9799f4b16fff007f76b |
| identity.AspNetUserTokens | 0 → 0 | d41d8cd98f00b204e9800998ecf8427e |
| identity.AspNetUsers | 4 → 4 | ffe7fb1ac91c88cd61863e3abc985f20 |
| profiles.OperationalProfiles | 0 → 0 | d41d8cd98f00b204e9800998ecf8427e |
| proxies.Proxies | **131 → 131** | **f1f2b42e10fc6573a75f93b777e7fcf6** |
| public.__EFMigrationsHistory | 6 → 7 | c9ee7168febe35528017da556d09374a (seis registros antigos) |

Fingerprint completo do histórico com sete registros:
`975f0f2a5b175ef38379960344cace61`. Comparação repetida após runtime/testes passou.
Baseline final completo: `.local/24c1-homolog-final.txt` e seu sidecar de estruturas.
Nenhum perfil/alocação criado na homologação. Recibos CSV e envelopes intactos.

## API e smoke tests HTTPS

Após aprovação estrutural/dados/chaves, API iniciada com assembly 2.4C.1, conexão,
ambiente Development e chaves existentes. Não havia listener anterior identificado;
o procedimento também exige reconhecer o processo antes de encerrar eventual
instância existente. PID registrado **5908**; HTTPS **https://localhost:7090**,
HTTP http://localhost:5041. **Mantida em execução.** Nenhum frontend alterado.

| Smoke | Resultado |
| --- | --- |
| /health | 200 Healthy |
| /openapi/v1.json | 200, contrato operacional presente |
| auth/me, grid, profiles, allocations sem sessão | 401 |
| Consulta autenticada Admin no banco homologado | 200; sem login/logout ou escrita |
| Grid homologado, sete páginas de 20 | 131 linhas e UUIDs únicos; total global 131 |
| Ordem de vencimento asc/desc e região contains case-insensitive | Aprovados |
| Perfil/plataforma/filtros combinados sem correspondência | Total 0 correto |
| Filtros dos quatro estados | Available=131; Reserved/InUse/Unavailable=0 nesta leitura |
| Metadados de perfil/plataformas sem associação | Null nas 131 linhas |
| Query pageSize inválida | 400 |
| Endpoints anteriores de proxies/perfis/alocações autenticados | 200 |
| Credenciais nas respostas do grid | Ausentes |

Para evitar gravar auditoria de login na homologação, o harness local readonly
gerou em memória um ticket efêmero de cinco minutos a partir do principal de um
Admin existente, usando factories Identity e o keyring atual com geração desabilitada.
Conexão do harness configurada com default_transaction_read_only=on. O ticket não
foi publicado, persistido ou reutilizado no navegador; nenhum usuário/senha foi
modificado. A API validou o cookie e o security stamp normalmente. Login/logout
com senha foram exercitados somente no banco descartável, como descrito abaixo.

Instância isolada do mesmo assembly em https://localhost:7091 usou banco e role
p6_ui_test_UUID, sem INSERT na auditoria homologada. Chaves/senhas sintéticas em
memória. **35 requests de smoke aprovados**: login/sessão/logout, CSRF, cadastro
sintético de proxy/perfis, slots, grid Available → Reserved → InUse, confirmação
repetida idempotente, proxy suspensa ainda InUse, liberação → Unavailable,
reativação/reutilização → Available, expiração lógica sem gravação na leitura,
confirmação vencida 409, histórico Expired e disputa simultânea 201/409.
Todos os efeitos de escrita ficaram nesse banco descartável, removido com a role
e o servidor de teste no finally. API principal permanece ativa.

Evidências sanitizadas: `.local/24c1-readonly-smoke.json` e
`.local/24c1-smoke-results.json`. Nenhuma captura de CSV real, HAR, trace ou payload
com credenciais. TLS autoassinado aceito apenas nos clientes locais de smoke;
confiança do Windows não foi alterada.

## Builds, testes e segurança

| Verificação nesta homologação | Resultado |
| --- | --- |
| Build solução, saída isolada homolog-check | Aprovado, 0 erros/avisos |
| Builds dos harnesses criptográfico/fixture/readonly | Aprovados, 0 erros/avisos |
| Unitários .NET completos | **216 aprovados**, 0 falhas/ignorados |
| Integração .NET/PostgreSQL completa | **170 aprovados**, 0 falhas/ignorados |
| Total .NET | **386 aprovados** |
| Criptografia e 131 envelopes, antes/depois | Aprovados |
| Snapshot dados/schema e chaves, depois dos testes | Aprovados |
| Bancos p6_auth_test/p6_ui_test e roles de smoke restantes | **0** |

Suíte inclui catálogo/slots/null/compatibilidade, upgrade e guarda Down descartável,
filtros/ordem/paginação, uma linha por proxy e estados, autorização, CSRF, ausência
de credenciais; regressões completas de Identity, criptografia, importador CSV,
exclusividade, expiração/recuperação concorrente, idempotência e rollback de auditoria.
CENTRAL_TEST_POSTGRES partiu de postgres administrativo, nunca do banco homologado;
fixtures criaram/trocaram para bancos UUID antes das migrations e escritas.
Testes executados com --no-build usando saída atual; build final em outra pasta
evitou substituir DLLs da API ativa. Resultados em `.local/24c1-homolog-test-results/`
e `.local/24c1-homolog-tests.log`.

Respostas/logs de smoke verificadas contra segredos sintéticos; logs principais
conferidos também contra senha de conexão e chave AES atual, sem imprimir valores.
Nenhuma ocorrência encontrada. DTOs do grid não selecionam/retornam credenciais.
Isso cobre o material observado, não uma garantia sobre configurações futuras de log.

Não foram necessários nem repetidos lint/typecheck/Playwright nesta homologação
backend: nenhum frontend foi implementado/alterado. Os resultados frontend anteriores
constam no relatório 2.4C.1, não são apresentados como nova execução.

## Riscos residuais e recuperação

- Homologação local técnica; não equivale a avaliação manual, produção ou teste de carga.
  Estados ocupados foram exercitados na instância descartável, mantendo a homologação vazia de vínculos.
- Backup não teve restauração completa de dados em ensaio; DPAPI depende do mesmo
  perfil Windows. Manter dump/globals/keyring/DPAPI sob acesso restrito.
- platform legado permanece separado dos slots; não recuperar metadados CSV ou
  inferir associações. Os 131 registros continuam sem perfis e isso é esperado.
- Contains pode exigir otimização em volume maior; teste anterior de 2000 linhas
  e ausência de N+1 não certificam desempenho de produção. Locks/retries e demais
  limites operacionais das etapas 2.4B permanecem.
- Nenhum rollback/restore/Down indicado ou executado. Se houver incidente futuro,
  interromper escrita, preservar evidências e pedir autorização para recuperação;
  testar restore em banco isolado antes de considerar substituição. Não perder
  novos slots nem inventar valores legados para satisfazer o schema antigo.

Somente documentação e harnesses locais de homologação foram criados nesta execução;
nenhuma funcionalidade de produto, conta, proxy, perfil, alocação ou integração nova.
Não houve divergência de banco/chaves, migration adicional, importação, commit,
deploy ou início da Etapa 2.4C.2. Uma consulta opcional de PID foi bloqueada pelo
sandbox; disponibilidade foi comprovada pelos smoke HTTPS, sem necessidade de
alteração/reparo do serviço ou dados.

**Etapa 2.4C.1 homologada tecnicamente no ambiente local e pronta para a revisão
do usuário e futura integração com frontend. Aguardar revisão; não avançar de etapa.**
