# API — Pré-visualização de CSV (Etapa 2.3A)

`POST /api/v1/proxies/import/preview?page=1&pageSize=20`

Requer HTTPS, sessão Admin, cookie antiforgery e header X-CSRF-TOKEN obtido após
login em GET /api/v1/auth/csrf. Manager/Operator não possuem acesso. Rate limiting
sensitive existente: padrão 10 requisições/minuto por IP, além do limite global.
Respostas no-store. A Etapa 2.3B adiciona [confirmação controlada](proxy-csv-import.md).

Envie multipart/form-data com **exatamente um arquivo no campo file**; não enviar
outros campos. page e pageSize são parâmetros query: 1..10000 e 1..100; padrões
1 e 20. Não há sessão/cache de upload: reenvie o arquivo para cada página. O nome
do arquivo e o MIME da seção não são utilizados como prova de encoding/conteúdo.

## Configuração

Seção ProxyImportPreview de appsettings/variáveis de ambiente:

| Opção | Padrão | Faixa de configuração |
| --- | ---: | --- |
| MaxFileBytes | 2097152 (2 MiB) | 1024..10485760 |
| MaxRows | 5000 | 1..10000 registros, excluindo cabeçalho |
| MaxPhysicalLines | 10000 | MaxRows..50000, incluindo cabeçalho |
| MaxFieldCharacters | 4096 | 1024..8192 |
| MaxColumns | 32 | 10..64 |

Configuração inválida impede startup. Overhead multipart limitado a 16 KiB além
do arquivo. Limite JSON das outras operações continua 16 KiB. Arquivos não são
bufferizados em disco; aplicação e host/reverse proxy devem manter a mesma política
e proibir logging de corpos antes de receber material real.

## Cabeçalhos e formatos

Obrigatórios: PERFIL, PLATAFORMA, PROXY, PORTA, USUARIO ou USUÁRIO, SENHA, STATUS,
DATA DE VENCIMENTO. Opcionais: REGIÃO e ADsPower Lote. Nomes não dependem de caixa,
acentos ou espaços repetidos. Não há aliases genéricos/posicionais; cabeçalhos
desconhecidos geram avisos; cabeçalhos ausentes e duplicados ambíguos bloqueiam a validação.
Duplicatas reconhecidas são resolvidas somente se no máximo uma ocorrência tiver
conteúdo em todo o arquivo e as linhas tiverem a quantidade correta de campos.
Ocorrências totalmente vazias geram IgnoredEmptyDuplicateHeader com posição original.
Não são mescladas colunas preenchidas, mesmo com valores idênticos.
Nomes desconhecidos não aparecem nas respostas, somente posição da coluna.

UTF-8 estrito com/sem BOM; separador ; ou ,; aspas duplas, escape "" e multilinha.
Datas dd/MM/yyyy ou yyyy-MM-dd representam início do dia UTC; ISO exige Z/offset.
Desde a Etapa 2.3C.1, query `referenceYear` opcional aceita um único inteiro entre
1 e 9999, sem padrão. Aplica-se exclusivamente a datas dd/MM sem ano; datas
completas são preservadas. Sem opção, dd/MM recebe ExpiryYearRequired. Calendário
é validado, incluindo 29/02 e anos bissextos; não há inferência do ano atual.
ReferenceYearApplied avisa a linha cuja data foi completada; expiresAt contém
o vencimento final interpretado. Opção inválida/repetida retorna 400.
Vencimento precisa ser futuro. Estados e incompatibilidades na [ADR 0006](../decisions/0006-proxy-csv-preview.md).
REGIÃO ausente não recebe default automático: o modelo exige região e a linha é
inválida. A Etapa 2.3B permite query `defaultRegion`, explicitamente informada e
validada pelo domínio, usada apenas em células ausentes/vazias.

## Resposta e códigos

200: CsvPreviewResponse contém delimiter, columns (posição/campo canônico ou null),
headerIssues, summary, page/pageSize e items. Desde a Etapa 2.3B inclui também
confirmationToken e confirmationExpiresAt: prova protegida, válida por 15 minutos,
vinculada ao administrador, arquivo, defaultRegion, skipRowsWithoutConnection e referenceYear. Summary inclui total, valid, invalid,
duplicates, empty, conflicts e warnings. Total inclui linhas vazias internas;
quebra de linha final não cria registro vazio extra. valid + invalid = total.
duplicates/empty são subconjuntos de invalid; conflicts e warnings são indicadores
independentes. warnings soma ocorrências nas linhas e avisos de cabeçalho.

Items mantêm a ordem original e trazem line/endLine, hostMasked, port,
usernameMasked/passwordMasked, status, expiresAt, flags de presença de perfil,
plataforma, região e lote, valid/duplicate/existingConflict, errors e warnings.
Issues usam line, column (posição 1-based ou null), code e mensagem estática.
Nenhuma senha ou usuário em claro, envelope, CSV original ou nome de arquivo.
Erros de schema/validação de linhas retornam 200 para permitir diagnóstico; arquivo
estruturalmente inválido não retorna uma prévia parcial.

| HTTP | Significado |
| --- | --- |
| 400 | CSV vazio/malformado, UTF-8 inválido, multipart inválido, página inválida ou CSRF inválido |
| 401 | Sem sessão válida |
| 403 | Papel sem permissão |
| 413 | Limite de arquivo/linhas/campos/colunas ultrapassado |
| 415 | Request não é multipart/form-data (após autorização e CSRF) |
| 429 | Rate limiting; observar Retry-After |

Falhas de header multipart/extensão estrutural podem retornar 400. ProblemDetails
de CSV malformado/limite incluem line sem conteúdo. Cancelamentos interrompem o
processamento. Header-only com schema reconhecido retorna total zero.

Arquivo e credenciais existem apenas na memória do request. Não há gravação,
auditoria de conteúdo, cache compartilhado ou retenção do CSV. A prova stateless
não armazena o arquivo nem suas credenciais. Atomicidade, idempotência e confirmação
estão documentadas na [ADR 0007](../decisions/0007-controlled-csv-import.md).
