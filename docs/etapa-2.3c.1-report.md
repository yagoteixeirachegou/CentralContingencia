# Etapa 2.3C.1 — Ano de referência

Data: 11/10/2026. Implementação e validação automatizada concluídas; homologação
manual do administrador pendente. Não iniciar outras etapas automaticamente.

## Implementação e decisões

- Campo **Ano de referência (opcional)** inicialmente vazio em `/proxies/import`.
  Aceita inteiro de 1 a 9999, intervalo do calendário suportado, sem ano automático.
- Query `referenceYear` opcional nos endpoints existentes de prévia e confirmação.
  Opção repetida, vazia, não inteira ou fora do intervalo retorna ProblemDetails 400.
- O parser compartilhado aplica o ano somente ao formato exato dd/MM. Datas
  dd/MM/yyyy, yyyy-MM-dd e ISO com timezone permanecem intactas. Sem opção, dd/MM
  continua inválido com ExpiryYearRequired.
- Calendário validado por parsing estrito; vencimento futuro continua sujeito às
  regras existentes de Application/domínio. 29/02 em ano não bissexto e 31/04 são
  inválidos. Escolher um ano válido não torna automaticamente toda data importável.
- Prévia mostra `expiresAt` recebido da API na coluna **Vencimento interpretado
  (UTC)**, com data e hora, evitando deslocamento pelo fuso do navegador.
  ReferenceYearApplied avisa cada linha completada. Datas inválidas ficam sem valor.
- Ano integra fingerprint/prova protegida junto com arquivo, administrador, região
  e opção de ignorar linhas sem conexão. Alterar/omitir ano na confirmação retorna
  409. Replay usa as mesmas opções; reutilizar UUID com opções diferentes é rejeitado.
  Ano ausente preserva exatamente o algoritmo anterior de fingerprint, inclusive
  quando a opção de ignorar linhas está ativa.
- Alterar ano na interface descarta a prévia/prova e inicia nova operação. O diálogo
  explicita o ano escolhido. A confirmação reenvia arquivo e opções, revalida com o
  mesmo parser, mantendo transação, criptografia, auditoria e idempotência existentes.
- Região complementar e tratamento explícito de linhas sem qualquer conexão foram
  preservados. ATIVO continua status operacional; não foi criada regra de disponibilidade.
  PERFIL, PLATAFORMA e lote continuam sem persistência/vínculos.

Esta autorização substitui a decisão anterior de exigir ano sempre no CSV.
Não foram alterados Domain, Infrastructure, esquema do banco, chaves AES/DPAPI ou
keyring. Não houve migrations no banco homologado, importação real, commit ou deploy.

## Arquivos alterados

- Application/Proxies/Import: PreviewContracts.cs, ImportContracts.cs,
  ProxyCsvPreviewService.cs e ProxyCsvImportService.cs.
- API: Proxies/ProxyImportPreviewEndpoint.cs e Program.cs (OpenAPI).
- Frontend/features/proxies/import: api.ts, contracts.ts, ProxyImportPage.tsx,
  ImportPreviewTable.tsx e ProxyImportPage.test.tsx.
- Testes .NET: CsvCompatibilityTests.cs, CsvImportTests.cs e CsvImportEndpointTests.cs.
- scripts/Verify-CsvImportUi.cjs.
- AGENTS.md, CLAUDE.md, docs/api/proxy-csv-preview.md,
  docs/api/proxy-csv-import.md, docs/ui/csv-import.md e este relatório.

## Verificações executadas

| Comando/verificação | Resultado |
| --- | --- |
| `dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3c.1/` | Aprovado; zero erros/avisos |
| `dotnet test CentralContingencia.slnx --no-build --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3c.1/` | 278 aprovados: 159 unitários + 119 integração; zero ignorados |
| `npm run lint` | Aprovado |
| `npm run typecheck` | Aprovado |
| `npm test -- --run` | 104 aprovados, quatro arquivos, na execução final |
| `npm run build` | Aprovado; dois avisos conhecidos de comentários PURE em Zod/Rollup |
| `node scripts/Verify-CsvImportUi.cjs` | Aprovado no Chrome; quatro grupos de verificações e quatro capturas |
| Smoke HTTP local, após reiniciar API | Health/OpenAPI 200; endpoints protegidos sem sessão 401; CSRF 200; contratos existentes preservados |
| OpenAPI em execução | referenceYear opcional, inteiro 1..9999, sem default, nos dois endpoints |
| Comparação antes/depois do banco homologado | Contagens e fingerprints de registros preservados |
| Logs da API reiniciada | Sem chave AES carregada, credenciais sintéticas ou exceção não tratada |

Para integração, CENTRAL_TEST_POSTGRES foi definido somente no processo usando o
loader local existente, sem expor conexão. A fixture cria e elimina PostgreSQL
descartável, com suas migrations existentes e dados sintéticos. Não executa gravações
no banco homologado. A suíte inclui autenticação, papéis, CSRF, criptografia, auditoria,
concorrência e idempotência, além das novas regressões de ano/calendário/prova.

A primeira execução frontend teve um timeout de um segundo no teste existente
da listagem, enquanto outras verificações rodavam; 103 testes passaram. A repetição
da suíte completa passou em 104/104. Nenhum teste foi removido ou ignorado.
Uma verificação avulsa OpenAPI inicialmente falhou por quoting do PowerShell/Node;
foi repetida com here-string via stdin e aprovada, sem efeitos sobre a aplicação.

## Evidências de navegador

Todas as requisições `/api` foram interceptadas com mocks sintéticos; chamadas
não previstas foram bloqueadas. Nenhuma escrita chegou à API homologada.
Validado: ano inicialmente vazio, envio idêntico na prévia/confirmação, data final
em UTC, data completa preservada, região, linhas ignoradas, confirmação/cancelamento,
expiração, retry com mesma chave, permissões, desktop e mobile.
Zero erros JavaScript e zero vazamentos de credencial/prova detectados no console;
sem credenciais/prova no DOM ou storage. Capturas desktop/mobile inspecionadas.

- [Prévia desktop](../.local/verification-2.3c.1/01-previa-desktop.png)
- [Confirmação explícita](../.local/verification-2.3c.1/02-confirmacao.png)
- [Relatório sintético](../.local/verification-2.3c.1/03-relatorio.png)
- [Prévia inválida mobile](../.local/verification-2.3c.1/04-invalido-mobile.png)
- [Resultado automatizado](../.local/verification-2.3c.1/results.json)

Artefatos locais ignorados pelo Git; não contêm arquivos reais nem segredos reais.
A comparação em `.local/year-reference-before.txt` guarda apenas contagens/hashes.

## Execução local e pendências

Frontend disponível em **https://localhost:5173/proxies/import**, com sessão Admin.
Também acessível por Proxies → Importar CSV. API reiniciada em
https://localhost:7090 com configurações, DPAPI e keyring existentes, mantida em
execução; Vite existente permanece disponível. HTTPS usa certificado de desenvolvimento.

Para testar manualmente, selecione somente CSV sintético, informe ano quando houver
dd/MM e confira o vencimento interpretado. Datas completas não mudam; datas
impossíveis/vencidas bloqueiam a confirmação. Não confirmar gravação na homologação
sem autorização específica. As escritas automatizadas foram validadas somente com
mocks e banco descartável; o smoke homologado permanece sem sessão e sem gravação.

Falta avaliação manual do usuário para homologar a Etapa 2.3C.1. Nenhuma etapa
seguinte foi iniciada.
