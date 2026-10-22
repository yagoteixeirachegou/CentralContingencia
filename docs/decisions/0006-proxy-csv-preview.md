# ADR 0006 — Pré-visualização CSV sem persistência

Data: 11/10/2026. Etapa 2.3A, autorizada pelo usuário.

## Decisão

Application/Proxies/Import contém parser limitado, mapeamento explícito e caso de
uso. Domain/ProxyRules e Application/ProxyInputRules compartilham as invariantes
existentes com o agregado, a API CRUD e o adapter AES. A extração é restrita a
essas regras; não muda o comportamento das operações anteriores.

Infrastructure implementa uma porta exclusiva de leitura de conflitos. Projeta
somente host/porta, sem materializar entidades ou selecionar credenciais/envelopes.
Consultas parametrizadas em lotes de até 100 endpoints limitam a complexidade SQL.

Api lê MultipartReader diretamente, sem IFormFile binding, ReadForm ou buffering
em disco. Exige exatamente uma seção de arquivo chamada file. O limite de corpo
Kestrel é ampliado somente para esse endpoint; JSON mantém 16 KiB. Um stream
contador protege também requests sem Content-Length. Há limite adicional de
16 KiB para overhead multipart, 16 headers por seção e 4096 bytes de headers.
O buffer do upload é zerado no finally; strings gerenciadas não oferecem garantia
de apagamento físico. Nenhum cache, arquivo, token, auditoria de conteúdo ou
credencial persistida é criado pela prévia.

Admin é exigido tanto no HTTP como na Application. Cookies, CSRF, no-store e rate
limiting sensitive existentes são reutilizados. Para uploads, X-CSRF-TOKEN deve
estar no header: sua ausência é recusada antes que antiforgery tente ler o form.
O OpenAPI declara multipart explicitamente, sem metadata Accepts que desviaria
media types incorretos para o fallback de roteamento antes da autorização.

## Semântica

- UTF-8 estrito, BOM opcional; outros encodings são recusados. Detecção de vírgula
  ou ponto e vírgula somente fora das aspas do cabeçalho; cabeçalho ambíguo é erro.
- Aspas duplas escapadas por duas aspas, campos multilinha, LF/CRLF/CR; mensagens
  usam linha física inicial e final do registro. Aspas malformadas e NUL são erros.
- Normalização de cabeçalhos: Unicode FormD, remoção de acentos, uppercase
  invariável e espaços normalizados. Somente nomes explicitamente reconhecidos.
  Não ecoar nomes desconhecidos, valores inválidos, filename ou conteúdo original.
- Duplicata interna: host normalizado + porta + usuário exato. PERFIL e senha não
  definem identidade. A chave é um HMAC com material aleatório por request, descartado
  ao fim; não persiste hashes ou credenciais. Ocorrências posteriores são inválidas.
- Host/porta já existentes geram aviso de possível conflito, sem comparar senhas
  nem presumir unicidade do endpoint. Esse aviso não invalida automaticamente o
  registro: o modelo atual admite múltiplos usuários e não possui unicidade host/porta.
- Active/ATIVO, Suspended/SUSPENSO, Disabled/INATIVO/DESATIVADO são mapeamentos
  explícitos. DISPONÍVEL não é um estado válido; a prévia não promete disponibilidade.
- Datas: dd/MM/yyyy, yyyy-MM-dd ou ISO com segundos e Z/offset, fração opcional.
  Datas sem hora representam meia-noite UTC, sem adivinhar timezone da planilha.
  Vencimento precisa ser futuro, conforme o contrato atual de criação. Datas
  históricas tornam a linha inválida, embora o agregado consiga representar vencidos.
- Senha segue limite existente de 1024 bytes UTF-8; usuário 128, região 100.
  PERFIL (200), PLATAFORMA (100) e lote (128) recebem somente validação estrutural;
  não existe validação de domínio de perfil ou integração AdsPower nesta etapa.

## Incompatibilidades e limites

PERFIL, PLATAFORMA e ADsPower Lote não possuem persistência/vínculos no modelo atual.
REGIÃO é opcional no arquivo, mas obrigatória no agregado: ausência é explicitada
como aviso de schema e erro nas linhas; não se inventa região padrão. Sem relações,
não é possível detectar conflitos de perfil/alocação. Células parecidas com fórmulas
permanecem texto; aviso não significa executar nem avaliar comandos.

Para minimizar exposição, a resposta fornece host/usuário/senha mascarados, flags
de presença dos metadados e valores tipados de porta/status/vencimento. Não ecoa
os nomes de perfis, plataformas, região ou lotes. A futura UI precisará respeitar
esse contrato; ampliar exposição requer decisão explícita.

Resumo cobre todo o arquivo. Paginação apenas da resposta; cada página exige novo
upload e nova análise. Não há estado de confirmação nem snapshot persistente. A
consulta de conflitos é indicativa e pode mudar até uma eventual importação futura.
Linhas inválidas/duplicadas/ vazias são categorias sobrepostas, documentadas no API.

Sem migrations, gravação de proxies, frontend, integrações ou importação definitiva.
