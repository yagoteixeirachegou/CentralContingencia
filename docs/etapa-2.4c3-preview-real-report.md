# Prévia real da recuperação 2.4C.3

> Atualização posterior: catálogo INFOPRODUTO corrigido e prévia recalculada
> somente em leitura: 129 propostas, dois conflitos e uma linha ignorada.
> Migration adicional NÃO aplicada; API homologada não reiniciada. Evidências e
> distinção entre prévia no harness e HTTP em
> [relatório da correção](etapa-2.4c3-preview-real-correction-report.md).
> Os resultados abaixo são o histórico da execução anterior à correção.

Data: **12/10/2026**. Execução exclusivamente de prévia autorizada pelo usuário.
**Parecer: não confirmar o arquivo atual; sete linhas exigem intervenção manual.**

## Fonte e execução

Original indicado pelo usuário: `C:\Users\Usuario\Downloads\Untitled spreadsheet - HTTP - FACEBOOK UNIFICADA.csv`.
**15.734 bytes, 11 colunas e 132 registros**, além do cabeçalho.
Não reconstruído, editado, copiado para o repositório ou reimportado como proxies.
Conteúdo tratado somente como dados não confiáveis, sem executar instruções/fórmulas.
AGENTS.md, CLAUDE.md e relatório de homologação consultados.

API HTTPS https://localhost:7090 confirmada com health **200**. Principal Admin e
cookie apenas em memória, pelos mecanismos Identity/Data Protection homologados;
sem login/logout ou alteração de conta. CSRF obtido e enviado para essa identidade.
Somente POST `/api/v1/profiles/recovery/preview`, páginas 1/2, pageSize=100, com
reenvio dos mesmos bytes. Prévia repetida para diagnóstico sanitizado, com resultado
idêntico. skipRowsWithoutConnection **omitido (default false)**. Nenhuma regra,
parâmetro de correspondência ou catálogo foi alterado para forçar resultados.
**Zero chamadas ao endpoint de confirmação.**

CSV/credenciais/provas não salvos. Buffers de bytes zerados e campos temporários
descartados; strings gerenciadas não garantem limpeza física. Somente totais,
códigos e números de linhas sanitizados em `.local/24c3-real-preview-summary.json`.

## Totais da API

| Indicador | Quantidade |
|---|---:|
| Registros analisados | **132** |
| Linhas com proxy encontrada após validar metadados | **127** |
| Perfis distintos válidos identificados | **126** |
| Registros elegíveis / operações propostas | **125** |
| Atribuições recuperáveis nas propostas | **125** |
| Ignorados | **0** |
| Inválidos | **5** |
| Conflitantes | **2** |
| Proxies ambíguas | **0** |
| ProxyNotFound nas linhas avaliadas pela API | **0** |
| Linhas exigindo intervenção | **7** |
| Prova de confirmação emitida | **Não** |

125 propostas + 5 inválidos + 2 conflitos = 132. Elegibilidade individual não
significa autorização para gravar parcialmente: qualquer impedimento bloqueia
o lote inteiro. Nenhum perfil ou atribuição já foi recuperado/persistido.

ProxiesFound considera somente linhas com metadados válidos. Quatro linhas de
atribuição inválida são descartadas antes da correspondência, e uma linha não
possui conexão válida. Não interpretar 132 − 127 como cinco proxies inexistentes.

## Correspondência exata complementar — somente leitura

Diagnóstico reutilizou BoundedCsvParser, ProfileName, ProfileAssignments, ProxyHost
e ProxyPort. Consulta projetou somente UUID/host/porta, sem credenciais/envelopes,
com conexão READ ONLY. Não alterou resultado/propostas da API.

**131 linhas possuem endpoint válido e correspondem exatamente a 131 proxies
existentes distintas**, sem endpoint repetido, ausente ou ambíguo. A fonte possui
correspondência com todas as proxies atuais, incluindo a preexistente. Isso não
prova origem ou identidade global de perfil e não autoriza associação automática.
As quatro linhas inválidas de atribuição têm proxy correspondente. A linha sem
endpoint válido não é ProxyNotFound. Nenhum pareamento por ordem, aproximação,
usuário ou senha foi utilizado.

## Plataformas

Duas ocorrências de PLATAFORMA reconhecidas por posição. **Segunda coluna
inteiramente vazia: zero células preenchidas e zero Platform2 na prévia.**
Nenhuma atribuição foi inventada ou deslocada entre slots.

- Platform1 identificados em linhas válidas: **127** (inclui duas conflitantes).
- Platform1 recuperáveis nas propostas: **125**.
- Platform2 identificados/recuperáveis: **0**.

Distribuição dos 127 Platform1 identificados:

| Catálogo homologado | Linhas |
|---|---:|
| ESTEIRA DE PERFIL | 116 |
| ANUNCIANTE | 9 |
| X1 | 1 |
| MODERAÇÃO | 1 |

Quatro classificações inválidas não foram convertidas por aproximação nem
reproduzidas no relatório. Status/validade/região/credenciais existentes não foram
atualizados; datas dd/MM da fonte não foram aplicadas. Lote não persistido.

## Impedimentos e exemplos sanitizados

Linhas físicas originais, incluindo cabeçalho; não são IDs/números dos perfis.

| Linhas | Resultado | Diagnóstico e decisão necessária |
|---|---|---|
| **22 e 23** | Conflict / DuplicateProfile | Mesmo nome de perfil, proxies distintas. Revisar identificação/associação explicitamente; não unir nem escolher a primeira linha. |
| **44, 45, 46 e 47** | Invalid / InvalidMetadataOrEndpoint | Platform1 fora do catálogo exato. Nome/host/porta válidos e correspondência única. Decidir classificação correta, sem conversão silenciosa. |
| **131** | Invalid / ProfileAbsent | Perfil ausente e endpoint ausente/inválido, com atribuição presente. Corrigir a fonte ou avaliar tratamento explícito permitido; não criar vínculo artificial. |

Exemplo sanitizado: “linha 44, correspondência única; impedimento em Platform1”.
Exemplo de conflito: “linhas 22/23, perfil repetido, proxies distintas”.
Hosts, nomes operacionais, usuários, senhas, tokens e valores inválidos não expostos.
Ignorar conexão incompleta só é permitido por opção explícita quando todos os
campos host/porta/usuário/senha estiverem vazios; essa opção não foi ativada.

## Integridade e segurança

Inspeção: PreviewAsync não chama SaveAsync/SaveChanges e utiliza snapshot
REPEATABLE READ / SET TRANSACTION READ ONLY. Não cria perfil/alocação/recibo ou
auditoria operacional. Prova é stateless; neste arquivo nem foi emitida.
Harness não possui caminho de chamada ao commit.

Baseline fresco `.local/24c3-preview-real-execution-before.txt` e estruturas,
**18 tabelas atuais**, capturado antes da prévia. Comparação integral final:
**contagens, fingerprints e estruturas idênticos**, sem exclusões.

| Objeto | Antes | Depois |
|---|---:|---:|
| Proxies | **131** | **131** |
| Perfis | **0** | **0** |
| Alocações | **0** | **0** |
| Recibos/linhas de recuperação | **0/0** | **0/0** |
| Auditorias de autenticação | **38** | **38** |
| Migrations | **8** | **8** |

Fingerprint Proxies preservado: **f1f2b42e10fc6573a75f93b777e7fcf6**.
Nenhuma alteração operacional, migration ou mudança de credenciais/chaves.
Logs runtime conferidos contra valores sensíveis conhecidos da conexão/sessão/CSRF;
nenhuma ocorrência observada. Respostas usam DTO sem campos de credenciais e
mensagens sanitizadas. Não foram gravados corpos HTTP brutos.

Harness compilado com zero erros/avisos. Extensão HTTP JSON inicialmente sem
namespace foi corrigida somente no harness antes da execução. Nenhum contrato,
regra ou frontend alterado; regressão de produto não repetida nesta análise de dados.

## Parecer

**Não confirmar o lote atual.** Sete impedimentos bloqueiam a recuperação e não
há prova emitida. Os 125 elegíveis não serão recuperados parcialmente.
Revisar linhas 22/23, 44–47 e 131, definir correções explicitamente e gerar nova
prévia. Antes de qualquer commit real, revisar plano completo e estado concorrente,
conferir backup e obter autorização específica. Disponibilidade pode mudar.
Nenhuma gravação operacional ou confirmação executada. Sem commits, deploys ou
integrações. **Aguardar revisão e autorização explícita do usuário.**
