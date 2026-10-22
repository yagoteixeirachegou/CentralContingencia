# Etapa 2.4C.3 — Recuperação controlada de perfis e atribuições

Data: **12/10/2026**. Implementação backend concluída e validada com dados sintéticos
em bancos descartáveis. **Nenhuma recuperação real executada. Homologação e aplicação
da migration dependem de autorização explícita separada.**

## Diagnóstico de viabilidade e plano executado

AGENTS.md, CLAUDE.md e relatórios das Etapas 2.3/2.4C.1/2.4C.2 consultados. Inspecionados
parser, contratos/confirmador, entidades e Value Objects, stores EF, disponibilidade,
reservas, auditoria, DbContext, migrations, endpoints e fixtures existentes.
Diagnóstico e plano apresentados antes de implementar.

O importador 2.3 valida metadados PERFIL/PLATAFORMA, mas não cria perfis/vínculos nem
armazena esses textos no recibo. Não é possível extraí-los novamente do banco.
O fluxo de criação de proxies não deve ser reutilizado para recuperar associações.
É necessário reenviar a fonte em um fluxo separado e verificar correspondências.
Nesta etapa nenhum CSV real foi aberto, enviado ou reimportado.

Consulta final **READ ONLY** confirmou: **131 proxies, 130 UUIDs distintos registrados
como Imported nos recibos e uma proxy fora desses recibos; zero perfis/alocações**.
Essa evidência corresponde ao contexto de 130 importadas e uma preexistente.
Snapshot inicial/final das 16 tabelas preservado, incluindo registros de importação.
Não é uma prova de autoria ou de conteúdo da planilha original: isso exige conciliação
futura da fonte, com autorização própria.

Contagem agregada READ ONLY encontrou **zero grupos de host/porta duplicados**
na homologação atual, sem expor seus valores. Isso favorece a correspondência
determinística, mas não dispensa revalidação por linha no momento da confirmação.

Implementação incremental: leitor de metadados sobre parser compartilhado; avaliação
de correspondências/impedimentos; prévia protegida; transação/idempotência/auditoria;
contratos HTTP/OpenAPI; testes unitários/integrados e regressão completa.
Decisões na [ADR 0012](decisions/0012-controlled-profile-recovery.md).

## Regras implementadas

- Cabeçalhos PERFIL/PROXY/PORTA obrigatórios. PLATAFORMA 1/2 opcionais, ou uma/duas
  ocorrências do cabeçalho PLATAFORMA por posição. A segunda ocorrência vazia resulta
  em Platform2 null. Primeira vazia não recebe o texto da segunda. Misturas de formatos,
  mais de duas ocorrências e outros duplicados são ambiguidades bloqueantes.
- Perfil/atribuições/host/porta validam pelos Value Objects e catálogo existentes.
  Catálogo é exato após trim; valores desconhecidos não são convertidos por aproximação.
  UTF-8/BOM, delimitadores/aspas, limites e linhas originais vêm de BoundedCsvParser.
- Correspondência somente por host canônico + porta, com **exatamente um** resultado.
  Host/porta não são únicos no domínio: múltiplos resultados geram AmbiguousProxy.
  Não usar ordem das linhas, nome, senha, usuário ou aproximações para desempatar.
- Nome repetido no CSV (case-insensitive) e endpoint repetido bloqueiam o lote,
  inclusive as primeiras ocorrências. Nome existente no banco não prova identidade:
  impedir reutilização/criação automática, mesmo se o perfil existente estiver livre.
- Proxy não localizada, indisponível ou com reserva/vínculo conflitante gera
  impedimento explícito. Nunca sobrescrever vínculo ativo ou modificar perfil existente.
- Somente vínculo Confirmed já associado à proxy, com perfil Active, nome exato e
  slots exatos, pode retornar AlreadyLinked sem mudanças. Não duplicar auditoria/alocação.
- Linha sem perfil/atribuições é Ignored. Atribuição sem perfil é Invalid. Linha com
  metadados e todos os campos host/porta/usuário/senha vazios exige opção explícita
  skipRowsWithoutConnection; linha parcialmente preenchida não recebe essa exceção.
- Não interpretar STATUS/validade/região/credenciais da fonte como atualizações.
  **dd/MM não precisa de referenceYear neste fluxo**, pois o vencimento existente não
  é modificado. Disponibilidade vem da proxy persistida e de seus vínculos efetivos.
  ATIVO da fonte não torna uma proxy disponível. Lote não é persistido ou calculado.

O analisador de proxies e suas validações anteriores permanecem intactos: somente
parser/limites/transporte/prova e domínio são compartilhados. Mapeamento diferente é
necessário porque recuperação não cria proxies nem valida credenciais para persistência.
Frontend/layout não foram alterados.

## Prévia, confirmação e segurança

Novos endpoints Admin:

- **POST /api/v1/profiles/recovery/preview**
- **POST /api/v1/profiles/recovery/commit**

[Contrato completo](api/profile-recovery.md), documentado também no OpenAPI.
Cookies HTTPS, CSRF, no-store, rate limiting sensitive e upload limitado existentes.
Multipart contém exatamente file; não usa ReadForm/arquivos temporários persistentes.
Application também exige Admin, independentemente da política HTTP.

Prévia possui resumo global (total, proxies encontradas, perfis distintos identificados,
slots recuperáveis, ignorados, inválidos, conflitos e propostas), diagnósticos de
cabeçalho/linha e página limitada. Resultado Proposed informa CreateProfile e
CreateConfirmedAllocation. Não cria recursos, reservas ou recibos.
Itens retornam UUID da proxy e metadados válidos de perfil/slots, sem host da fonte,
usuário, senha, filename ou valores inválidos. Reenvio obrigatório para outras páginas.

Snapshot de prévia usa REPEATABLE READ/READ ONLY. Prova Data Protection de 15 minutos
vincula ator, bytes do arquivo, opção e plano completo, incluindo UUIDs/estados/
timestamps/prazos relevantes. Não há cache compartilhado de arquivo ou credenciais.
Namespaces de fingerprint são específicos à recuperação, impedindo uso de prova da
importação de proxies. Paginação não altera a semântica da prova.

Confirmar exige POST específico, reenvio do CSV, X-Import-Confirmation e
Idempotency-Key UUID. Dados manipuláveis de uma tabela frontend não são aceitos
como prévia validada. Sob transação e locks, o plano é consultado e recalculado:
arquivo/opção/ator/estado divergente ou prova expirada → 409 e nova prévia.

Política **tudo ou nada**: Invalid/Conflict impede todas as propostas. Ignored e
AlreadyLinked permanecem no recibo. Novos perfis recebem Reserved → Confirmed pela
AllocationDomainService na mesma transação, usando TTL existente. Associação é
**atual**, com timestamp da recuperação; não inventar início histórico da planilha.
Reservas expiradas são finalizadas e auditadas pelo helper homologado reutilizado,
antes de inserir novo vínculo. Histórico e índices de exclusividade preservados.

Auditorias ProfileCreated/AllocationReserved/AllocationConfirmed/AllocationExpired,
perfil, alocação, recibo e linhas são atômicos. Falha de auditoria reverte tudo.
Recibo também audita ator/data/correlação, contagens e UUIDs/resultados, sem CSV,
host, nome de perfil, atribuições ou credenciais. Nenhuma proxy é criada/alterada,
nenhuma senha existente é decifrada/recifrada; nenhum mecanismo criptográfico muda.

Idempotência durável por ator/chave. Replay exige mesmo arquivo/opção e sessão Admin,
retorna recibo original mesmo após expiração da prova, sem nova escrita/auditoria.
Outra fonte/opção com mesma chave → 409. Outra chave exige prévia atual e não
sobrescreve vínculos. Replay histórico não reativa uma alocação posteriormente liberada.

Locks começam por ProxyAllocations, seguindo o store existente, depois perfis,
proxies/recibos. SHARE ROW EXCLUSIVE bloqueia mutações concorrentes relevantes e
permite leitura; lock_timeout 5 s. Timeout/deadlock/unicidade/serialização retornam
409; falhas internas de persistência/auditoria retornam **500 sanitizado**, não 400.
CSV/estrutura/opções/CSRF inválidos → 400; autorização 401/403; limites 413/415/429;
lote inválido na validação final 422. Não há retry automático de mutação.

## Persistência e migration

**20261012131756_AddProfileRecoveryReceipts**, dependente de
20261012102737_AddProfileAssignments. **Gerada e aplicada somente pelas fixtures
descartáveis; não aplicada na homologação**.

[SQL incremental integral](security/etapa-2.4c3-migration.sql).
SHA-256: `856362CFBABF69A8AAB33746F248A11FC2BB830206713B2A7FD95F12DBE831BC`.

- audit.ProfileRecoveryBatches: UUIDs do lote/ator/chave, fingerprint protegido,
  data/correlação e contagens. CHECKs de IDs, tempo finito, correlação e contagens.
- audit.ProfileRecoveryLines: chave composta BatchId/Line, UUIDs opcionais de
  proxy/perfil/alocação e outcome. CHECK de resultado/linha e recursos obrigatórios
  nos resultados Recovered/AlreadyLinked.
- Cinco índices explícitos: único ActorId/IdempotencyKey, OccurredAt, e índices
  para cada FK opcional ProxyId/ProfileId/AllocationId. Mais dois índices das PKs.
- FKs RESTRICT para proxies/perfis/alocações; CASCADE somente recibo → suas linhas.

Up revisado: somente duas tabelas novas, constraints/FKs/índices e registro EF,
dentro de transação. Não há ALTER de tabelas existentes, UPDATE, DELETE, TRUNCATE,
remoção de colunas, recifragem ou modificação de Identity/Proxies.
FKs podem exigir locks breves nas tabelas referenciadas. Down apaga os recibos/linhas
e perde idempotência/auditoria da recuperação; não elimina proxies/perfis/alocações.
Não executar Down como recuperação improvisada. Backup e plano de recuperação
atualizados serão necessários em homologação futura autorizada.

Modelo EF verificado: **nenhuma mudança pendente de modelo após a migration**.
No banco homologado há **sete migrations existentes**, zero registro desta migration
e zero tabelas novas de recuperação. Não reiniciar a nova API contra esse schema.

## Arquivos

- Domain/Profiles/ProfileRecoveryBatch.cs: recibo/linhas e invariantes.
- Application/Profiles/Recovery: contratos, leitor sobre parser existente e casos
  de uso de avaliação/prova/idempotência.
- Infrastructure/Profiles/EfProfileRecoveryStore.cs: snapshot, locks e transação.
- Infrastructure/Allocations/EfAllocationStore.cs: helper de expiração compartilhado;
  sem alteração da política/regra homologada.
- Configuração EF de recuperação, DbContext, DI, migration/Designer/model snapshot.
- Api/Proxies/ProxyImportPreviewEndpoint.cs: transporte multipart reutilizado para
  rotas próprias de recuperação; Program.cs/OpenAPI atualizado.
- ProfileRecoveryTests.cs e ProfileRecoveryEndpointTests.cs.
- AGENTS.md, CLAUDE.md, ADR 0012, contrato/índices de API e arquitetura, SQL e relatório.

Nenhum pacote acrescentado. Dependências Clean Architecture permanecem voltadas
para dentro; Domain não depende de EF/HTTP. Nenhum código React/layout alterado.

## Testes e builds reais

Versões conferidas: SDK .NET 10.0.300, Node 22.22.3, npm 10.9.8, Docker 29.5.2.

| Verificação | Resultado |
|---|---|
| Build .NET final em saída isolada | Aprovado, zero erros/avisos |
| Unitários .NET completos | **237 aprovados**, zero falhas/ignorados |
| Integração PostgreSQL/HTTP completa | **179 aprovados**, zero falhas/ignorados |
| Total .NET final | **416 aprovados** |
| Casos novos de recuperação | **21 unitários + 9 integração** |
| Frontend lint/typecheck | Aprovados |
| Frontend Vitest completo | **145 aprovados**, seis arquivos |
| Frontend build | Aprovado; dois avisos conhecidos Rollup/Zod |
| EF has-pending-model-changes | Sem alterações pendentes |
| Comparação final do banco homologado | 16 tabelas preservadas em dados/estruturas |
| Chaves DPAPI/keyring | Três arquivos com hashes preservados; conjunto do keyring igual |
| Bancos p6_auth_test_* remanescentes | Zero |
| HTTPS frontend/API existentes | /proxies e /health HTTP 200 |

Cobertura: recuperação correta com slots independentes/ausentes, originais duplicados
e cabeçalhos explícitos/ambíguos, BOM/vírgula/aspas/linhas físicas, inválidos, perfis
duplicados, endpoints repetidos/ambíguos, proxy inexistente/bloqueada, nomes existentes,
reserva ativa, reenvio adulterado/opção/ator diferente, expiração da prova, concorrência
com mesma/diferente chave, replay sem nova auditoria, AlreadyLinked com nova chave,
recuperação auditada de reserva expirada, histórico e ausência de credenciais nos
corpos/recibos/auditoria/logs observados. Autorizações Admin/Manager/Operator,
anônimo, CSRF, limite de arquivo e contrato multipart OpenAPI exercitados por HTTP.

Rollback instalado por trigger exclusivamente na fixture descartável comprovou 500
sanitizado, sem perfil/alocação/recibo remanescentes e com proxy íntegra. Fingerprints
integrais do proxy incluem estado, timestamps, usuário e envelope cifrado, sem
imprimir seus valores. Suíte anterior também cobre liberação/reutilização, histórico,
exclusividade, concorrência, expiração, AES/adulteração, autenticação e CSV.

Fixtures usam conexão administrativa ao banco **postgres**, criam p6_auth_test_UUID
antes de migrations/seed/requests e removem somente seus próprios bancos. Nunca
aplicam migration ou gravam em central_contingencia_dev. Evidências locais:
`.local/24c3-test-results/`, `.local/24c3-verified-tests.log`,
`.local/24c3-recovery-final.log`, `.local/24c3-frontend-tests.log`.

Não houve Playwright novo: nenhuma interface foi criada/alterada. Fluxo de recuperação
foi validado por TestServer/HTTP e PostgreSQL real descartável, sem CSV real ou Chrome
autenticado na homologação. Nenhum resultado histórico de navegador é apresentado
como execução desta etapa.

Comandos finais:

```powershell
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-24c3/
# CENTRAL_TEST_POSTGRES em memória, Database=postgres; fixture troca para seu UUID.
dotnet test CentralContingencia.slnx --no-build --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-24c3/ --logger "trx;LogFilePrefix=verified" --results-directory .local/24c3-test-results
# Na pasta frontend: lint, typecheck, npm test -- --run --maxWorkers=1, npm run build.
```

Ocorrências resolvidas: fixtures unitárias precisaram ativar proxies explicitamente,
pois Create inicia Disabled. Consultas EF FromSql precisaram selecionar xmin, coluna
de sistema ausente em SELECT *, para proxies e perfis com concorrência otimista.
Comparação inicial de entidades antes de persistir divergira devido à precisão de
timestamps do PostgreSQL; comparação agora usa duas releituras persistidas, sem
ignorar campos. Todas as suítes finais aprovadas; nenhum teste removido/ignorado.

## Preservação, riscos e pendências

Snapshot `.local/24c3-before.txt` e sidecar de estruturas confirmados integralmente
ao final. **131 proxies, 130 importadas/uma preexistente, zero perfis/alocações e
sete migrations** mantidos. DPAPI e dois arquivos do keyring comparados por SHA-256
com o manifesto homologado anterior, sem exibir/descriptografar valores.
Não houve login de teste na homologação, importação, escrita operacional, regeneração
de chave, reinício, commit, deploy ou integração. Servidores existentes preservados.

Limitações: dados originais descartados não são recuperáveis sem a fonte; CSV real
não foi inspecionado nesta etapa, logo não há estimativa de quantos vínculos serão
recuperáveis. Nomes existentes/conflitos exigem conciliação explícita futura; não
há mecanismo de associação manual forçada neste fluxo. Snapshot limita proxies,
perfis e alocações relevantes a 10.000 elementos por conjunto; excesso é recusado,
nunca truncado e considerado válido. Locks de tabela reduzem paralelismo; nenhum
benchmark de carga/produção foi executado. Strings gerenciadas não garantem limpeza
física de plaintext transitório do upload; campos/buffers são descartados/zerados,
sem cache persistente/compartilhado. Recibos dependem do keyring e não têm expurgo.

**Backend pronto para revisão técnica.** Próximos passos condicionados:
revisar regras/SQL; autorizar separadamente backup e homologação da migration;
validar preservação/schema e só então reiniciar a API nova; depois gerar prévia da
fonte sob autorização própria e aprovar explicitamente os vínculos antes de qualquer
recuperação real. A fonte pode conter proxies atualmente indisponíveis: esses
impedimentos não serão contornados por inferências ou atualização de credenciais.

URLs atuais **https://localhost:5173/proxies** e **https://localhost:7090** continuam
na versão operacional homologada anterior para avaliação. Endpoints novos não foram
ativados nesse banco. Aguardar autorização explícita; não iniciar outra etapa.
