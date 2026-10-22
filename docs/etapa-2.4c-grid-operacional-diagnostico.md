# Diagnóstico — Grid operacional da Etapa 2.4C

Data: 11/10/2026. **Auditoria somente de leitura; implementação aguardando aprovação.**
Este relatório é o único arquivo criado. Nenhum código, migration, registro, chave,
layout ou servidor foi alterado. Nenhuma importação, login automatizado, commit,
deploy ou Etapa 2.4D foi executado.

## Evidências e limites

Inspecionados AGENTS.md, CLAUDE.md, relatórios 2.3A/B/C/C.1 e compatibilidade CSV,
preparação/homologação 2.3B, relatórios 2.4A/B.1/B.2/B.3/C, revisão e
preparação/homologação 2.4B, contratos de proxies, CSV e perfis/alocações.
Conferidos parser/analisador/commit, entidades, contratos Application, stores EF,
mapeamentos/índices, endpoints e componentes React existentes.

Conexão do loader conferida: **127.0.0.1:55432 / central_contingencia_dev**.
Consultas agregadas executadas por psql com `ON_ERROR_STOP=1`, em transações
`REPEATABLE READ READ ONLY`; `transaction_read_only=on` confirmado.
Não foram publicados hosts, usuários, senhas, nomes reais de perfis, corpos de
arquivos, fingerprints protegidos de importação ou material de chaves.

O CSV real não foi aberto nem reenviado. O banco comprova o resultado persistido,
mas não conserva valores das colunas descartadas nem identifica a versão exata da
planilha original. Formato de 11 colunas, segunda PLATAFORMA vazia e datas dd/MM
corresponde ao contexto fornecido e às regressões sintéticas documentadas.

## 1. Diagnóstico dos 131 registros

| Evidência atual | Quantidade |
| --- | ---: |
| Proxies cadastradas | **131** |
| Proxies registradas como Imported nos recibos | **130** |
| Proxies fora dos recibos de importação | **1** |
| Lotes CSV concluídos | 1 |
| Linhas no recibo desse lote | 132 |
| Imported / Empty / Conflict | 130 / 1 / 1 |
| Invalid / Duplicate no recibo | 0 / 0 |
| Perfis operacionais | **0** |
| Alocações, incluindo todo o histórico | **0** |
| Proxies importadas com qualquer alocação | **0** |
| Eventos operacionais de perfis/alocações | 0 |
| Eventos de criação/alteração de proxies | 131 |

Os 130 UUIDs de proxy registrados nas linhas Imported continuam presentes.
Portanto, há 131 proxies no total, mas o lote criou 130: uma proxy preexistia e
uma linha foi tratada como conflito. O recibo Conflict não guarda ProxyId;
sem a fonte original não se deve identificar por suposição qual registro causou
o conflito. Empty pode significar linha inteiramente vazia ou linha sem conexão
ignorada explicitamente; o recibo não permite distinguir essas possibilidades.

Todas as 131 proxies estão Active, não vencidas e elegíveis à disponibilidade
na leitura efetuada, pois não existem alocações. Isso é resultado das condições
completas do backend nesse instante, **não equivalência geral entre Active e Disponível**.

Há seis migrations registradas, até
`20261011230449_AddOperationalAudit`, incluindo as duas migrations 2.4B já aplicadas.
Zero constraints não validadas, índices inválidos/não prontos, alocações órfãs ou
duplicidades de alocação ativa por proxy/perfil. Não é uma verificação física
exaustiva do PostgreSQL nem ensaio de restauração.

Fingerprints agregados de representação JSON ordenada foram coletados sem expor
registros. O fingerprint atual de `proxies.Proxies` é
`f1f2b42e10fc6573a75f93b777e7fcf6`, com 131 linhas. MD5 serve aqui apenas à comparação
de snapshots, não à proteção de segredos. Baselines antigos com uma proxy são
anteriores à importação informada e não foram usados para sinalizar corrupção.

## 2. PERFIL, PLATAFORMA 1 e PLATAFORMA 2 no importador

`ProxyCsvPreviewService` reconhece PERFIL e PLATAFORMA, exige seus cabeçalhos e
valida seus textos: até 200 e 100 caracteres, respectivamente, sem controles e
sem vazio nas linhas elegíveis. A prévia retorna apenas flags de presença.
São **processados para validação e descartados para persistência**.

O aviso `UnmappedMetadata` informa expressamente que PERFIL, PLATAFORMA e
ADsPower Lote não serão importados nem vinculados. `CsvImportCandidate` carrega
somente dados de proxy; `ProxyCsvImportService` cria Proxy, evento de proxy,
lote e recibos por linha. Não cria OperationalProfile ou ProxyAllocation.
O fingerprint protegido é um comprovante, não uma cópia recuperável das células.

Cabeçalhos PLATAFORMA repetidos são resolvidos apenas quando no máximo uma
ocorrência possui conteúdo em todo o arquivo, com estrutura completa. A ocorrência
inteiramente vazia é ignorada com `IgnoredEmptyDuplicateHeader`. Duas ocorrências
preenchidas são ambíguas e bloqueadas, mesmo quando os valores coincidem.
**O parser não interpreta duas ocorrências como duas atribuições independentes.**
Cabeçalhos literais PLATAFORMA 1 e PLATAFORMA 2 também não estão no mapa atual:
geram UnknownHeader; sem PLATAFORMA reconhecida haverá MissingHeader.

Assim, nenhum valor de perfil ou plataforma da planilha pode ser exibido pelo grid
a partir das tabelas atuais. As 131 linhas devem mostrar PERFIL e ambas as
plataformas vazios até associações explicitamente cadastradas ou recuperadas.
Não se deve gerar perfis a partir de host, região, ordenação ou número da linha.

Região complementar, ano de referência explícito, descarte opt-in de linhas sem
conexão, prova protegida e idempotência CSV permanecem existentes e fora de alteração.
ATIVO é mapeado para o estado operacional Active da proxy. Não foi convertido
em classificação de perfil nem em disponibilidade incondicional.

## 3. Domínio e persistência necessários

O agregado `OperationalProfile` possui hoje **uma Platform obrigatória**, texto
livre validado até 100 caracteres. EF usa coluna Platform NOT NULL e CHECK textual.
Não há enum/catálogo de atribuições, Platform2 ou suporte à ausência de Platform.
ProfileName protege o nome, mas não o torna identificador global único.

Proposta: duas atribuições opcionais, `Platform1` e `Platform2`, no próprio
OperationalProfile, com enum de códigos estáveis e rótulos portugueses separados.
Ausência deve ser null, não uma classificação inventada. Validar o catálogo no
domínio; não criar entidade/tabela ProxyPerfil ou duplicar Proxy para montar o grid.
Manter Status do perfil independente das classificações e do Status da proxy.

Catálogo aprovado para futura implementação: DARKPOST, MODERAÇÃO, ANUNCIANTE,
HOST PÁGINAS, FLAG, BACKUP PIXEL, ESTOURADO, RESTRITA, BUGADO, ATIVO (LIVRE),
ESTEIRA DE PERFIL, MINERAÇÃO, FLAG (LIVRE), TIKTOK, BM, X1 e GOOGLE ADS.
ATIVO (LIVRE) jamais altera disponibilidade. Não impor exclusividade entre os dois
slots sem decisão: o pedido estabelece independência, mas não esclarece repetição
do mesmo valor nos dois slots nem se Platform2 pode existir sem Platform1.

**Migration necessária para as duas atribuições opcionais.** O endpoint de leitura
isoladamente não exige nova tabela. Caminho mínimo proposto: manter a coluna física
Platform para o primeiro slot, adicionar Platform2 nullable, permitir null em
Platform e revisar CHECKs/catálogo. Não remover colunas/tabelas ou regravar proxies.
A alteração de nulabilidade/CHECK é DDL em tabela existente: não chamar isso de
criação exclusivamente de objetos novos. Inspecionar SQL e locks antes de aprovação.

No estado auditado a tabela de perfis está vazia, evitando conversão de classificações
legadas. Isso deve ser reconferido antes de gerar/aplicar a futura migration;
valores livres eventualmente cadastrados depois não poderão ser convertidos ou
descartados silenciosamente. A coluna legada/API platform requer plano explícito
de compatibilidade: alias temporário para Platform1, depreciação documentada e
sem aceitar simultaneamente aliases divergentes. Clientes atuais e exemplos com
plataforma livre serão afetados pela validação de catálogo. Aprovar essa mudança
contratual antes da implementação; não prometer compatibilidade sem ajustes/testes.

Não alterar Proxy.ExpiresAt para DateOnly nesta revisão: preservar a semântica UTC
e a validade que já regem importação, reserva e disponibilidade.

## 4. APIs atuais e endpoint de consulta recomendado

| API atual | Capacidade | Lacuna para o grid |
| --- | --- | --- |
| GET /api/v1/proxies e /available | Proxies paginadas, available booleano, vencimento ordenável | Sem perfil, plataformas ou motivo da indisponibilidade; região exata |
| GET /api/v1/profiles e /{id} | Uma plataforma e nome, dados do perfil | Nome/plataforma exatos; uma plataforma obrigatória |
| GET /api/v1/allocations | Filtros profileId/proxyId/status, paginação | Não projeta perfil/proxy juntos ou estado lógico único |
| GET /api/v1/profiles/{id}/allocations | Histórico paginado | Histórico parcial não define a associação atual |
| POST/PATCH profiles; close; reserve/confirm/release | Operações homologadas e auditadas | Reutilizáveis; não precisam ser substituídas pelo grid |

Montar as linhas no navegador exigiria múltiplas consultas e junções entre páginas
parciais. Filtrar PERFIL/PLATAFORMA após paginar proxies produziria totais e páginas
incorretos; consultar perfil/alocação por linha introduziria N+1 HTTP. Buscar tudo
para filtrar no cliente não atende centenas/milhares de registros.

Recomenda-se **GET /api/v1/proxies/operational-grid**, rota proposta, ainda inexistente.
Um caso de uso/DTO/port específico de leitura em Application; adapter EF em
Infrastructure; endpoint com política Operations, HTTPS/cookie, no-store e erros
sanitizados existentes. Sem CQRS completo, cache de credenciais ou novo agregado.

Consulta parte de Proxies e faz LEFT JOIN somente com a alocação efetivamente atual
e seu perfil: Confirmed ou Reserved com prazo futuro. Released/Expired/reservas
vencidas não entram como vínculo atual, embora permaneçam no histórico.
Não juntar todo o histórico e paginar linhas repetidas. Índices de exclusividade
mantêm no máximo uma linha de vínculo atual por proxy/perfil.

DTO mínimo: proxyId/host/port/status/region/expiresAt, disponibilidade calculada,
profileId/name/platform1/platform2 opcionais e allocationId/state/deadline opcionais.
Sem username em claro, senha, envelope, chave ou prova CSV. Sem vínculo atual,
campos do perfil/plataformas devem ser null, renderizados vazios.

Todos os filtros antes de Count/Skip/Take: profile contains case-insensitive,
region contains case-insensitive, plataforma em slot1 OU slot2, disponibilidade;
AND entre filtros diferentes. Escapar `%`, `_` e escape no ILIKE para pesquisa
literal contains; parâmetros SQL, tamanho de busca limitado, whitelist de ordem,
pageSize até 100 e desempate por proxyId em expiresAt asc/desc.
Usar um único instante do servidor para expiração e elegibilidade da consulta;
devolver evaluatedAt e, preferencialmente, total/itens em snapshot readonly comum.
GET não recupera/audita reservas vencidas e não constitui garantia de reserva.

### Disponibilidade: decisão indispensável

Três rótulos não cobrem todos os estados possíveis. Uma proxy bloqueada/vencida
sem alocação não pode aparecer como Disponível, Reservada ou Em uso.
Proposta sujeita a aprovação:

- Confirmed → Em uso, mesmo se a proxy posteriormente vencer/bloquear, com Status
  e Vencimento indicando o problema; o vínculo não termina automaticamente.
- Reserved ainda no prazo → Reservada; status/validade continuam expostos.
- Sem vínculo efetivo e Active/válida → Disponível.
- Sem vínculo efetivo e inelegível → Indisponível, com motivo não sensível.

Recomenda-se acrescentar Indisponível ao filtro. Alternativa: mostrar “—” e motivo
para esse caso, incluído apenas em Todas. É necessário escolher antes da implementação.

### Desempenho

Projeção SQL sem carregar agregados/credenciais, sem consultas por linha; dois
comandos fixos para contagem/página são aceitáveis com snapshot coerente.
Índices atuais: host/porta, status/vencimento, região; alocações por proxy/perfil
e histórico, mais únicos parciais ativos. Índice B-tree de região não resolve
ILIKE '%texto%'; perfil só possui índice status/criação. Ordenação apenas por
ExpiresAt pode exigir índice próprio conforme plano/carga.

Validar EXPLAIN e tempos com dados sintéticos volumosos em banco descartável.
Para milhares, avaliar trigram/GIN em Name/Region e índices nos slots apenas após
medição; pg_trgm exigiria extensão/DDL e aprovação própria, não foi instalado.
Não prometer desempenho com base em 131 registros. Locks de tabela nas mutações
existentes serializam escritas de alocação; grid de leitura não deve acrescentar locks
de escrita. Histogramas/contadores não podem ser estimados da página atual.

## 5. Frontend único e operações preservadas

Usar /proxies como entrada operacional única, mantendo shell, estilos, componentes
e autenticação existentes. TanStack Table/Query com query key incluindo todos os
filtros/página/ordem; reset de página ao mudar filtros; cancelamento/debounce da
pesquisa. Não montar joins nem calcular disponibilidade no cliente.

Cadastro/edição de proxy e importação existentes continuam acessíveis por ações.
Perfil e classificações podem ser criados/editados em dialog contextual; reservar
a proxy da linha permite selecionar explicitamente um perfil Active livre.
Lista de perfis elegíveis precisa de consulta server-side ou ampliação aprovada
de filtro, pois a listagem atual não filtra perfil desocupado. Backend sempre
revalida; não ocultar perfis usando apenas a página do histórico.

Reserva exibe o prazo retornado, confirmação usa allocationId e endpoint existente,
liberação exige diálogo explícito. Histórico abre sob demanda no mesmo contexto,
filtrado por proxyId e paginado, preservando mudanças de perfil entre reutilizações.
Encerrar perfil usa close existente com confirmação distinta de “Liberar proxy”.
Manter 409, replay idempotente de confirm, ausência de retries automáticos em
reserve/release, CSRF e invalidação conjunta de grid/proxies/profiles/allocations.

Admin/Manager operam; Operator consulta. Prova de importação e arquivos somente
em memória. Relação e deadline exibidos vêm da consulta backend. Um timer pode
solicitar refresh no prazo, sem confirmar/liberar automaticamente.

Rotas antigas /profiles e /profiles/:id podem encaminhar para o contexto do grid
sem manter segunda listagem operacional. **Perfis sem proxy não possuem linha no
grid**; oferecer dialog secundário para localizar/gerenciar esses perfis, sem
inventar linhas de proxy vazia. Decidir o tratamento dos links antigos e de perfis
Closed/sem vínculo antes de remover navegação redundante.

## 6. Recuperação de vínculos da fonte original

Não existe material no banco para reconstruir automaticamente PERFIL/PLATAFORMA.
O grid poderá listar imediatamente as 131 proxies, mas não recuperará seus vínculos
por ser implementado. Recuperação é operação separada, ainda não autorizada.

Plano futuro: obter a revisão exata da fonte e as opções usadas; conciliar em memória
os números originais com Imported → ProxyId no recibo, após comprovar a versão da
fonte. Número de linha isolado não comprova identidade; edição/reordenação invalida
essa correspondência. Fingerprint protegido depende também das opções e não guarda
essas opções como campos recuperáveis; sem comprovação, exigir conciliação humana.

Linha Conflict não possui ProxyId; host/porta não são únicos no domínio e não
equivalem a PERFIL. Resolver conflitos e nomes repetidos explicitamente, sem união
automática de perfis. Não reaproveitar commit de proxies para recuperar vínculos.
Gerar uma prévia específica de conciliação e exigir aprovação dos pares
profileId/proxyId antes de qualquer escrita, com transação, auditoria e exclusividade.
Não inventar datas históricas de alocação ou identificação AdsPower ausente.
Definir se vínculo recuperado representará associação confirmada atual e qual
data/motivo auditável utilizar. Nunca regravar credenciais ou sobrescrever proxies.

## 7. Preservação, segurança e plano incremental

Os 131 envelopes têm formato válido: nonce 12 bytes, tag 16 e ciphertext entre
1 e 1024 bytes, todos com KeyId development-v1. Isso não prova autenticidade GCM
individual; nesta auditoria nenhuma senha foi descriptografada. Código AES-256-GCM
inspecionado mantém associated data vinculada ao UUID/KeyId e nonce aleatório;
não há motivo para recifrar proxies ou alterar DPAPI/keyring para o grid.

Antes de uma futura migration: backup **atualizado após a importação**, hashes,
snapshot de todas as tabelas/chaves, revisão do SQL e autorização separada.
Backups históricos com uma proxy não representam os 131 registros atuais.
Após aplicação autorizada, exigir mesmos UUIDs/contagens/fingerprints de Proxies,
envelopes, recibos, histórico e Identity. Nunca restaurar backup antigo sobre o banco
para ajustar o grid. GETs e filtros não podem gravar auditoria de negócio/expiração.

| Passo futuro, condicionado à aprovação | Entrega e verificação |
| --- | --- |
| 1. Fechar decisões | Disponibilidade inelegível, slots/catalogo, compatibilidade REST e perfis sem proxy |
| 2. Domínio/persistência | Duas atribuições opcionais, regras e testes; gerar/revisar migration sem aplicar na homologação |
| 3. API de leitura | Grid SQL, filtros combinados, paginação/ordem, ausência de N+1/segredos; contratos/OpenAPI |
| 4. API de metadados | Cadastro/PATCH dos slots opcionais, sem mudança de exclusividade; testes e compatibilidade |
| 5. Interface operacional | Uma listagem, dialogs contextuais e histórico; layout preservado, papéis/CSRF |
| 6. Validação isolada | Lint/typecheck/builds/suítes, Playwright mocks/descartável; volume, expiry, 409, replay e rollback |
| 7. Homologação autorizada | Backup novo, migration aprovada e fingerprints; avaliação manual sem escrita automática |
| 8. Conciliação opcional | Projeto separado e aprovação própria para recuperar metadados/vínculos da fonte |

Preservar testes de exclusividade, recuperação sob demanda, confirmação idempotente,
fechamento/reutilização e rollback de auditoria. Acrescentar regressões de null nos
slots, catálogo, filtro OR entre slots e AND entre filtros, contains literal/case,
uma linha por proxy com histórico extenso, reserva vencida sem escrita, proxy
inelegível/ocupada, paginação global estável e ausência de N+1/credenciais.

Integrações n8n/AdsPower permanecem futuras. REST e UUIDs permitem cliente HTTP,
mas autenticação de serviço/escopos e recuperação de operações ainda precisam de
etapa específica; não reutilizar conta humana Admin nem automação de navegador.

## 8. Parecer e verificações desta auditoria

**Aprovar a direção arquitetural do grid por proxy; aguardar decisões e autorização
antes da implementação.** O modelo de alocação pode ser preservado. As lacunas são
classificações opcionais do perfil, consulta conjunta, filtros e organização da UI;
uma junção no frontend não resolve corretamente esses requisitos.

Consultas READ ONLY concluíram com código 0. A comparação final confirmou
**contagens e fingerprints idênticos nas 16 tabelas**, incluindo as 131 proxies,
recibos, Identity, auditorias, perfis, alocações e histórico EF.
Catálogo/invariantes foram conferidos;
não foram executados builds, testes de escrita ou Playwright nesta auditoria
documental. Os 361 testes .NET e 128 frontend do relatório 2.4C são evidências
históricas, não uma execução nova nem prova dos filtros propostos.
Não foi feito benchmark, backup novo, restauração, migration ou recuperação da fonte.
Somente este relatório foi criado. Aguardar revisão do usuário.
