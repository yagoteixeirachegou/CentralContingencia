# Etapa 2.4C.1 — Backend do Grid Operacional Unificado

Data: **12/10/2026**. Implementação e validação automática concluídas.
**Migration não aplicada à homologação; aguardar aprovação.** Nenhum frontend,
recuperação CSV, integração, commit ou deploy implementado/executado.

Atualização posterior: migration aplicada e validada mediante autorização na
[homologação 2.4C.1](etapa-2.4c1-homologacao-report.md). O relato abaixo preserva
o estado histórico da entrega; não reaplicar a migration.

## Alterações realizadas

Lidos AGENTS.md, CLAUDE.md e diagnóstico do grid; inspecionados domínio, contratos,
repositórios, mapeamentos, endpoints, OpenAPI e fixtures. Plano apresentado antes
da implementação. SDK .NET 10.0.300, EF/Identity 10 e stack existentes preservados,
sem dependências novas e sem alteração das referências Clean Architecture.

- OperationalProfile ganhou Platform1/Platform2 nullable, independentes, com o
  mesmo catálogo aprovado de 17 atribuições. Primeiro/segundo/nenhum/ambos aceitos;
  segundo sem primeiro e valores iguais aceitos. Não foi criada restrição arbitrária.
- Catálogo central ProfileAssignments valida strings aparadas e rótulos exatos;
  não aceita vazio ou valor desconhecido. CHECKs protegem a persistência.
  ATIVO (LIVRE) não é status operacional nem disponibilidade.
- Platform legado permanece texto livre com regras antigas, sem converter ou
  perder valores. Novos cadastros podem omiti-lo: coluna persistida null, resposta
  legada string vazia para manter o tipo JSON. Não é alias para Platform1.
- POST/PATCH/GET de perfis acrescentam slots opcionais. PATCH detecta presença:
  omitido preserva, null limpa slot. Nome/platform legados continuam recusando null
  no HTTP. Auditoria atômica, perfil terminal, CSRF e políticas permanecem.
- Novo caso de uso/port/DTO na Application, adapter na Infrastructure, endpoint na
  API. SQL fixo parametrizado via EF Core SqlQueryRaw, somente dados operacionais;
  LEFT JOIN de vínculo efetivo/perfil e filtros antes da paginação. Sem N+1 ou
  materialização de username, senha, envelope, chave ou prova.
- Count e página compartilham REPEATABLE READ READ ONLY e instante único do servidor.
  Reserva vencida não bloqueia disponibilidade e GET não grava expiração/auditoria.
- Confirmed → InUse mesmo depois de vencer/bloquear; Status e vencimento separados.
  Histórico nunca duplica linhas nem é removido.

Regras e locks de reserva/confirmação/liberação/expiração/fechamento, índices de
exclusividade, reutilização, rollback e idempotência homologadas não foram alterados.
Domain não depende de EF/SQL/HTTP. ADR: [0011](decisions/0011-operational-grid-profile-assignments.md).

## Contrato do endpoint

**GET /api/v1/proxies/operational-grid**, Admin/Manager/Operator, sessão HTTPS,
no-store; anônimo 401, papel não permitido 403. Leitura não exige CSRF e não grava;
mutações existentes continuam exigindo CSRF. OpenAPI atualizado.

| Parâmetro | Regra |
| --- | --- |
| profile | Contains literal case-insensitive, até 200 |
| region | Contains literal case-insensitive, até 100 |
| platform | Catálogo, correspondência Platform1 OU Platform2 |
| availability | Available / Reserved / InUse / Unavailable; omitido = todas |
| sort / direction | expiresAt, asc/desc; defaults expiresAt/asc; desempate proxyId |
| page / pageSize | 1..10000 / 1..100; defaults 1/20 |

Filtros combinam por AND globalmente. `%`, `_` e `\` pesquisados literalmente.
Query desconhecida/repetida, busca vazia, valores inválidos e ordem insegura: 400.
Persistência indisponível: 503 sanitizado.

Resposta items/page/pageSize/total/evaluatedAt. Item: proxyId, profileId?,
profileName?, platform1?, platform2?, host, port, region, expiresAt, status,
availability, allocationId?, reservationExpiresAt?. Sem vínculo, metadados de
perfil/plataformas null. Reserved só quando prazo futuro; Confirmed → InUse;
sem vínculo efetivo → Available se Active/criada/válida, senão Unavailable.
Não garante que a proxy continuará livre até a próxima operação.

Contrato completo: [api/operational-grid.md](api/operational-grid.md).
Filtro legado platform de GET /profiles continua exato/textual; filtro platform
do grid consulta somente novos slots. Frontend existente continua sem mudanças.

## Migration e SQL revisados

**20261012102737_AddProfileAssignments**, após
20261011230449_AddOperationalAudit. Gerada, compilada e aplicada somente pelas
fixtures descartáveis. Designer/snapshot EF alinhados; has-pending-model-changes
retornou `No changes have been made to the model since the last migration.`

SQL completo em [security/etapa-2.4c1-migration.sql](security/etapa-2.4c1-migration.sql).
SHA-256: `031C75CD11965695D4A587E3774E4ED0BE62E807FE81A0782EB2872C0817D293`.
Up/Down inspecionados integralmente:

- ALTER de profiles.OperationalProfiles.Platform para permitir null, sem conversão
  ou alteração do texto existente. CHECK textual legado preservado e compatível
  com null pelo comportamento SQL de CHECK.
- ADD Platform1 e Platform2 varchar(100), nullable, sem default artificial.
- ADD CK_Profiles_Platform1 e CK_Profiles_Platform2, catálogo idêntico e null aceito.
- INSERT do ID/version no histórico EF somente quando aplicada; transação SQL.
- Nenhum UPDATE/DELETE/TRUNCATE, remoção de coluna/tabela ou alteração de
  Proxies/Identity/alocações/auditorias. Nenhuma recifragem, chave ou índice novo.

É uma **expansão não destrutiva**, mas inclui relaxamento de NOT NULL em coluna
existente; não afirmar que é exclusivamente criação de objetos. ALTER TABLE exige
lock de schema e os CHECKs podem examinar a tabela. Planejar janela e timeout.

Down remove slots/CHECKs e seria destrutivo para novas atribuições. Inclui guarda
que rejeita retorno a NOT NULL se existem Platform legados null; não preenche
strings artificiais. Nenhum Down aplicado na homologação. Tentativa em fixture
descartável comprovou a recusa antes de remover colunas; dados e registro da
migration permaneceram. Recuperação futura exige plano próprio.

Teste de upgrade desde o schema anterior preservou perfil com platform livre fora
do catálogo, UUID, metadados e envelope de proxy; slots começaram null. CHECK novo
recusou valor desconhecido diretamente no PostgreSQL descartável.

## Testes e resultados reais

| Verificação final | Resultado |
| --- | --- |
| Build solução .NET isolado | Aprovado; 0 erros/avisos |
| Unitários .NET completos | **216 aprovados**, 0 falhas/ignorados |
| Integração PostgreSQL/HTTP completa | **170 aprovados**, 0 falhas/ignorados |
| Total .NET | **386 aprovados** |
| Novos casos | 16 unitários + 9 integração |
| Revalidação específica de upgrade/guarda Down | 1 aprovado após acrescentar a verificação da guarda |
| Modelo EF/migration | Sem mudança pendente de modelo |
| Frontend lint/typecheck | Aprovados; nenhum código frontend alterado |
| Frontend Vitest | **128 aprovados**, execução com um worker |
| Frontend build | Aprovado; dois avisos conhecidos Rollup/Zod |
| Snapshot homologado | Dados e estruturas das 16 tabelas idênticos |
| DPAPI/keyring conferidos por SHA-256 | 3 arquivos, bytes preservados no intervalo da regressão |
| Fixtures remanescentes após limpeza | 0 |

Cobertura nova: slots independentes/ausentes/iguais, catálogo completo/inválido,
validação sem mutação parcial, autorização antes de persistir, limites/query,
PATCH null versus omitido, CSRF e Operator sem escrita; leitura Admin/Manager/Operator,
anônimo 401/Integration 403; upgrade e CHECKs; proxies sem perfil, suspensas,
desativadas, vencidas, reservadas, confirmadas bloqueadas/vencidas; reserva vencida
sem escrita/auditoria, histórico/reutilização sem duplicar linhas; filtro OR dos
slots/AND dos filtros, nome/região parciais com caixa diferente, curingas literais,
paginações e ordens, no-store/OpenAPI e ausência de credenciais nas respostas/SELECTs.
Suíte anterior de autenticação, AES/adulteração, CSV, expiração, concorrência,
confirmação idempotente, exclusividade e rollback passou integralmente.

Massa sintética de **2.000 proxies**: página 10 de 100, total 2000, **2 SELECTs**
para contagem/página, nenhum N+1 ou coluna sensível selecionada. Na execução final,
tempo observado da consulta: **55 ms**, incluindo preparação/contagem/página.
Não é benchmark de produção, percentil, ensaio de carga concorrente ou SLA.
Trigram/índices específicos permanecem possibilidade futura mediante medição.

Resultados TRX e logs em `.local/24c1-test-results/`, `.local/24c1-final-tests.log`
e `.local/24c1-frontend-tests.log`, ignorados pelo Git. Sem Playwright novo: nenhuma
UI implementada; testes HTTP no TestServer e PostgreSQL descartável exercitam o
contrato. Não há homologação runtime da nova API no banco existente.

Comandos finais relevantes:

```powershell
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-24c1/
# CENTRAL_TEST_POSTGRES usa postgres administrativo; AuthFixture muda para
# p6_auth_test_UUID antes de migrations/seed/requests. Nunca apontar para homologação.
dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-24c1/ --logger "trx;LogFilePrefix=final" --results-directory .local/24c1-test-results
# EF com BaseOutputPath definido no processo; comandos não aplicam schema:
dotnet ef migrations script 20261011230449_AddOperationalAudit 20261012102737_AddProfileAssignments --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build --output docs/security/etapa-2.4c1-migration.sql
dotnet ef migrations has-pending-model-changes --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
# src/central-contingencia-web:
npm run lint
npm run typecheck
npm test -- --run --maxWorkers=1
npm run build
```

Ocorrências resolvidas: Docker estava parado e foi iniciado, incluindo somente
o container PostgreSQL existente; snapshot aguardou startup. Build inicial no
sandbox não compilou e passou em execução autorizada com saída isolada. Primeira
consulta LINQ não traduziu filtros após construção do DTO/Value Object: substituída
por read model SQL fixo parametrizado, com todos os filtros no banco. Query numérica
repetida passou a ser validada antes do parsing para retornar 400, sem 500. Ajustados
case/tipagem de parâmetros OpenAPI; versão final compilada/testada sem avisos.

Revisão automática rejeitou comando que disponibilizava conexão homologada ao
harness. Substituído por conexão administrativa ao banco postgres; fixtures trocam
para bancos UUID antes de qualquer escrita. Nenhum bypass ou execução do comando
rejeitado. Tratamento de stderr do PowerShell interrompeu uma execução e deixou uma
fixture: confirmados prefixo UUID, sentinela sintética, migration desta etapa e
ausência de conexões, e removido exclusivamente esse banco descartável.

Primeira execução frontend concorrente teve quatro timeouts em testes existentes;
repetição completa com um worker passou 128/128, sem mudar testes/frontend. Nenhum
teste removido ou ignorado para obter aprovação.

## Preservação e riscos residuais

Alvo homologado confirmado **127.0.0.1:55432 / central_contingencia_dev**.
Snapshot inicial `.local/24c1-before.txt` e sidecar de estruturas comparados ao final:
**16 tabelas preservadas nas contagens/fingerprints/estruturas**. 131 proxies,
0 perfis, 0 alocações, 6 migrations; migration nova registrada=0, colunas novas=0.
Nenhum login de teste na homologação; envelopes/recibos/chaves não foram alterados.
Nenhuma senha existente foi decifrada. A nova API não foi iniciada contra schema antigo.

Arquivos centrais: Domain/Profiles (OperationalProfile e ProfileAssignments);
Application/Allocations (contratos/caso de uso), Application/Proxies/OperationalGridContracts;
Infrastructure/Allocations/EfOperationalProfileStore, Proxies/EfOperationalGridReader,
configuração EF de perfil/DI/migration/designer/snapshot; API/Operations,
Proxies/OperationalGridEndpoint e Program/OpenAPI; OperationalGridTests,
OperationalGridEndpointTests e regressões de ApiTests/OperationalApplicationTests.
Documentação: AGENTS/CLAUDE, ADR 0011, contratos, SQL e este relatório.
Nenhuma entidade Proxy, criptografia, importador ou arquivo-fonte frontend alterado.

Clientes devem usar novos slots para classificações; platform legado não aparece
automaticamente como Platform1 no grid. Não converter perfis por inferência.
Catálogo textual exige evolução explícita dos rótulos/CHECKs; não tratar renomeação
como cosmética. Contains com ILIKE pode usar scan em volume maior; teste local não
garante desempenho em produção. Estado pode mudar depois da resposta do GET.
Locks de mutação e limitações de retry documentadas nas etapas 2.4B permanecem.

## Próximos passos para homologação

1. Revisar este relatório, compatibilidade legada e SQL incremental.
2. Mediante autorização própria, preparar backup atualizado das **131 proxies**,
   demais tabelas e chaves/keyring; conferir hashes, alvo e única pendência.
3. Aplicar somente migration aprovada, validar schema e preservação dos dados/chaves.
   **Não executado nesta entrega.**
4. Só após schema aprovado reiniciar API com configurações/chaves existentes,
   validar leituras homologadas e smoke de escrita em banco descartável.
5. Frontend unificado e conciliação CSV continuam etapas separadas, não iniciadas.

**Backend pronto para revisão e preparação de homologação. Aguardar aprovação antes
de aplicar migration; não declarar esta etapa homologada no banco existente.**
