# Diagnóstico de compatibilidade CSV — antes das correções

Data: 11/10/2026. Investigação baseada nas características fornecidas pelo usuário,
sem abrir/importar o CSV real e sem acesso de escrita ao banco homologado.

Inspecionados BoundedCsvParser, ProxyCsvPreviewService, contratos de prévia/commit,
ProxyCsvImportService, regras de disponibilidade e frontend de importação.
Nenhuma regra funcional foi modificada nesta investigação.

## Reprodução sintética

Harness local `.local/csv-compatibility-diagnosis/Diagnosis.csproj`, com Application
real, leitor de conflitos em memória e 132 registros/11 colunas. PLATAFORMA aparece
duas vezes; a segunda ocorrência tem células vazias. Há exatamente 128 regiões
ausentes, datas 31/12 sem ano, status ATIVO e um registro final com metadados mas
sem host/porta/usuário/senha. As variações são geradas apenas em memória.

| Cenário sintético | Válidos | Inválidos | Erros de data | Erros de região |
| --- | ---: | ---: | ---: | ---: |
| Estrutura original com duplicata vazia | 0 | 132 | Não avaliados | Não avaliados |
| Retirada somente a coluna duplicada vazia | 0 | 132 | 132 | 128 |
| Região complementar explícita | 0 | 132 | 132 | 0 |
| Datas completas com ano sintético futuro explícito | 131 | 1 | 0 | 0 |

Numeração preservada: linhas físicas 2..133 no arquivo sintético, sem campos
multilinha. Em arquivo com campos multilinha, Line/EndLine preservam posições
originais; não se presume que números físicos equivalham aos do arquivo real.
Assert adicional confirmou que a região complementar não substituiu regiões
existentes. Status Active reconhecido depois de desbloquear o schema.

## Causas

1. O parser estrutural aceita 11 colunas e 132 registros, dentro dos limites.
   A camada de mapeamento marca qualquer nome normalizado repetido como
   DuplicateHeader, mesmo sem dados na segunda ocorrência. Isso gera InvalidHeaders
   em todas as linhas e suprime validações de campo posteriores.
2. ParseDate admite dd/MM/yyyy, yyyy-MM-dd e ISO com timezone, mas não dd/MM.
   Ausência de ano é rejeitada deliberadamente para evitar inferência silenciosa.
3. Região obrigatória do domínio torna as 128 ausências inválidas se defaultRegion
   não for explicitamente fornecido. Esse fallback já funciona no backend/frontend
   e só preenche células ausentes ou em branco; valores existentes são preservados.
4. A linha com metadados e sem conexão não é EmptyRow: ela recebe erros de campos
   obrigatórios. A confirmação atual aborta todo o arquivo diante dessa linha.
5. ATIVO já mapeia para Active. Disponibilidade depende adicionalmente de validade,
   instante de criação e ausência de alocação; não foi criada nova equivalência.
6. PERFIL/PLATAFORMA/lote são reconhecidos e têm validações textuais, porém não
   entram no agregado Proxy, persistência ou vínculos. Aviso atual fala em ausência
   de vínculos; a UI deve explicitar que os valores não serão importados.

## Tratamento proposto e decisões pendentes

Duplicatas reconhecidas: examinar todas as linhas antes do mapeamento. Aceitar
somente quando houver no máximo uma coluna com conteúdo, ignorando apenas as
ocorrências integralmente vazias com aviso de posição. Não juntar valores linha
a linha nem escolher silenciosamente a primeira coluna se houver duas preenchidas.
Ambiguidades permanecem erro; colunas malformadas continuam erro. Não remover
posições físicas para preservar números de coluna/linha nos diagnósticos.

Ano: proposta de campo explícito de ano para dd/MM, sem predefinir ano corrente
ou incrementar ano quando a data estiver passada. Datas completas continuam
inalteradas; datas impossíveis/vencidas continuam inválidas. Opção deve integrar
prévia, confirmação, prova/fingerprint e idempotência, mantendo compatibilidade
dos recibos existentes quando não utilizada. Alternativa: exigir ano no próprio CSV.
Decisão do usuário solicitada antes de implementar.

Linha incompleta: manter erro e exigir correção/remoção manual é a opção
conservadora. Alternativa exige opção explícita para ignorar linha sem qualquer
dado de conexão, com aviso/contagem no relatório, jamais descartar linha parcialmente
preenchida ou inválida silenciosamente. Decisão do usuário solicitada.

Após decisões: implementar tratamento inequívoco, testes de regressão sintéticos
para 11/132, ambiguidades, região preservada, ano ausente/explícito/bissexto/vencido,
linhas incompletas, prova alterada e replay, além de regressões frontend/backend.
Nenhuma mudança de criptografia, chave, migration ou dados homologados necessária
para o diagnóstico. Etapa 2.3C continua aguardando homologação manual.
