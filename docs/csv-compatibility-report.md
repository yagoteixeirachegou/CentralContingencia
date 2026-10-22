# Correção de compatibilidade CSV — Etapa 2.3C

Data: 11/10/2026. Dados exclusivamente sintéticos. Homologação manual pendente.

## Decisões autorizadas

O usuário decidiu manter datas sem ano inválidas: cada data deve conter o ano no
próprio CSV. Não foi criada opção de ano, inferência de ano corrente ou avanço para
o próximo ano. Também autorizou ignorar explicitamente linhas incompletas apenas
quando host, porta, usuário e senha estiverem todos vazios, com aviso e relatório.

## Correções

- Colunas duplicadas reconhecidas: verificar todas as linhas, inclusive páginas
  posteriores. Com no máximo uma ocorrência preenchida e estrutura correta,
  mapear a ocorrência preenchida e ignorar somente colunas totalmente vazias,
  avisando a posição. Não mesclar colunas ou alterar numeração original. Duas
  ocorrências com conteúdo continuam erro, mesmo quando seus valores coincidem.
- Região complementar continua aplicada apenas em células ausentes/em branco,
  preservando regiões existentes. Indicador regionPresent corrigido para tratar
  espaço em branco coerentemente com essa regra.
- Datas dd/MM retornam ExpiryYearRequired, com linha/coluna e instrução clara de
  corrigir para dd/MM/yyyy. Datas completas continuam sujeitas à validade futura.
- Query booleana skipRowsWithoutConnection, padrão false, compartilhada por prévia
  e commit. Checkbox desmarcado por padrão, com descrição e confirmação explícita.
  Mudá-lo descarta prova/UUID da operação anterior, como mudar arquivo/região.
- Linha sem os quatro campos de conexão recebe EmptyRow e NoConnectionRowSkipped
  somente com a opção ativa, cabeçalhos válidos e contagem correta de campos.
  Não há validação/importação de seus metadados porque ela foi explicitamente
  descartada. Qualquer dado parcial de conexão mantém as validações normais.
- Linha ignorada permanece no resumo, prévia, relatório e lote de auditoria, com
  número original. Reutilizado resultado persistido Empty, ampliado documentalmente
  para vazio ou sem conexão; não houve alteração de schema ou migration.
- Opção true integra fingerprint/prova, impedindo mudanças entre prévia e commit.
  Opção false mantém fingerprint anterior sem alterações, preservando recibos legados.
- Mensagem explícita de que PERFIL, PLATAFORMA e ADsPower Lote não serão importados.
  Não foram criados perfis, vínculos ou alocações. ATIVO continua apenas Active;
  disponibilidade não ganhou equivalência nova e mantém regras existentes.

AES-256-GCM, chave/versionamento, cookies, Admin, CSRF, transação, idempotência,
auditoria sanitizada e proibição de sobrescrever proxies foram preservados.

## Reprodução e regressão

CSV sintético com 11 colunas, 132 registros, PLATAFORMA duplicado com segunda
ocorrência vazia, 128 regiões ausentes e última linha sem conexão. Com região
complementar explícita, datas completas futuras e opção de descarte ativa:
131 válidos, 1 ignorável; 128 regiões complementadas e 3 regiões existentes
preservadas; linha final 133 mantida. Datas dd/MM em registros elegíveis continuam
bloqueadas, conforme decisão do usuário. O arquivo real não foi aberto/importado.

| Validação | Resultado |
| --- | --- |
| Testes .NET | 263 aprovados: 146 unitários + 117 integração, zero falhas/ignorados |
| Testes frontend | 97 aprovados |
| Lint/typecheck frontend | Aprovados |
| Build frontend | Aprovado, avisos conhecidos de comentários PURE Zod/Rollup |
| Build solução .NET isolado | Aprovado, zero avisos/erros |
| Chrome/Playwright com mocks | Aprovado; capturas atualizadas |
| Smoke API homologada sem sessão | Aprovado; health/OpenAPI/401 e contratos sem escrita |
| Snapshot homologado | Contagens/fingerprints preservados |

13 novos casos .NET e 2 frontend cobrem 11/132, resolução da ocorrência não vazia,
duplicatas ambíguas/iguais e conteúdo após primeira página, ausência de ano mesmo
com descarte ativo, região preservada, descarte opcional versus conexão parcial,
alteração da opção após prova, recibo persistido/criptografia/replay com PostgreSQL
descartável, query inválida e interface/diálogo da nova opção. Testes existentes
de autorização, CSRF, concorrência, rollback e não exposição continuam aprovados.
Chrome usa mocks em todas as chamadas /api; confere nova opção nos dois requests,
datas sem ano, relatório de ignorados, casos mobile, erros e permissões.

## Arquivos e evidências

Application: PreviewContracts.cs, ImportContracts.cs, ProxyCsvPreviewService.cs e
ProxyCsvImportService.cs. Api: ProxyImportPreviewEndpoint.cs e Program.cs/OpenAPI.
Frontend import: api.ts, contracts.ts, ProxyImportPage.tsx, ImportPreviewTable.tsx,
ProxyImportPage.test.tsx. Testes: CsvCompatibilityTests.cs, CsvImportTests.cs e
CsvImportEndpointTests.cs. Script Chrome: Verify-CsvImportUi.cjs. Documentação
API/UI, AGENTS.md, CLAUDE.md e relatórios atualizados.

Capturas sintéticas atualizadas em .local/verification-2.3c: 01-previa-desktop.png,
02-confirmacao.png, 03-relatorio.png e 04-invalido-mobile.png; results.json registra
sucesso e ausência de vazamento observado em DOM/storage/console.

Builds isolados .local/build-csv-compat e .local/build-csv-compat-final não alteram
arquitetura. API local reiniciada com conexão/chave DPAPI existentes para disponibilizar
o comportamento corrigido. Nenhuma chave foi substituída, migration aplicada no
banco homologado, dado operacional alterado, commit ou deploy executado.
Testes que gravam usam apenas bancos descartáveis, removidos pelas fixtures.

## Por que o CSV original falhava

O problema não era ter 11 colunas ou 132 registros: ambos estão dentro dos limites.
O mapeamento rejeitava qualquer PLATAFORMA duplicado, bloqueando todas as linhas
antes das validações individuais. Removido esse bloqueio, surgiam os erros de
datas sem ano e de região obrigatória ausente quando não fornecida opção explícita.
A linha com metadados sem proxy não era vazia e impedia a transação inteira.

Agora a duplicata vazia pode ser resolvida, a região complementar pode preencher
as ausências e a linha sem conexão pode ser ignorada por opção explícita. **O CSV
original continua corretamente recusado até que suas datas recebam o ano no arquivo.**
Não basta escolher ATIVO: não substitui requisitos de vencimento/disponibilidade.

Acesso: https://localhost:5173/proxies/import (Admin). Avaliação manual pendente;
não confirmar importação homologada ou usar arquivo real sem autorização própria.
Nenhuma próxima etapa foi iniciada.
