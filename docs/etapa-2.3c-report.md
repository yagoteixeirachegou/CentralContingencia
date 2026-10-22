# Relatório — Etapa 2.3C: Frontend de Importação CSV

Data: 11/10/2026. Implementação concluída; **homologação manual pendente**.
AGENTS.md, CLAUDE.md e relatórios 2.3A/2.3B/homologação foram lidos. Contratos
reais Application/OpenAPI e componentes existentes inspecionados antes das mudanças.

## Implementação

Rota Admin `/proxies/import`, acessível por Importar CSV na gestão de proxies.
Reutiliza layout autenticado, componentes Button/Input/Dialog, cliente HTTP,
AuthProvider, proteção de rota, QueryScope e invalidação TanStack Query.
Não há redesign, nova dependência ou alteração backend.

- Seleção explícita de arquivo .csv não vazio, até 2 MiB inicialmente. A API mantém
  autoridade sobre encoding, tamanho configurado, estrutura e regras de domínio.
- Formulário RHF/Zod com região complementar explícita. Não parseia CSV no cliente
  nem replica validações de host/porta/status/credenciais/vencimento.
- Multipart com somente file; nome enviado normalizado para import.csv, Content-Type
  com boundary definido pelo navegador; CSRF compartilhado obtido antes da mutação.
- Resumo global e tabela de 100 linhas por página, numeração original, proxy
  mascarado, porta, indicação de região, status reconhecido, erros e avisos.
- Prova protegida e vencimento usados conforme API. Diálogo exige confirmação
  explícita; cancelamento não grava. Sem percentuais de progresso inventados.
- UUID por operação mantido em retries/503/rede e após 409 com nova prévia.
  Não há retry automático. Trava síncrona e controles desabilitados evitam duplo envio.
- Alterações de arquivo/região descartam a prova e iniciam outra operação.
  Expiração exige nova prévia. Sessão expirada usa o tratamento compartilhado.
- Relatório de importados/ignorados/inválidos/conflitantes, inclusive erro 422,
  e invalidação das consultas de proxies após sucesso.

Arquivo, prova e chave somente no estado local da página. Mutations não armazenam
arquivo/prova nas variáveis/dados do cache compartilhado: preview recebe apenas
número da página e mutations retornam void. Sem localStorage/sessionStorage,
console de conteúdo, HAR ou traces. Credenciais retornadas mascaradas não são
renderizadas como campos recuperáveis. Mensagens por código/campo reconhecido
não ecoam mensagens arbitrárias; relatório 422 é validado/filtrado por schema Zod.

## Compatibilidade e decisões

API não retorna região textual da linha: a tabela mostra Informada no CSV, Ausente
ou a região complementar explicitamente informada. Não foi inventada propriedade
nem modificado o backend. PERFIL/PLATAFORMA/lote continuam sem vínculos operacionais.

Prévia conta duplicatas/vazios como inválidos, embora confirmação possa ignorá-los.
Se invalid é zero, pode confirmar desde que haja linhas, prova válida e headers
sem erros bloqueantes. Se há inválidos potencialmente ignoráveis, todas as páginas
devem ser revisadas, e erros adicionais impedem confirmação. Nenhuma estimativa
de elegibilidade baseada apenas na página atual. Backend revalida e decide.

Paginação reenvia o mesmo File com opções idênticas e gera nova prova. Guardar CSV
temporário no frontend persistente foi rejeitado. Mutations não usam retry automático
para evitar confirmação sem ação explícita e pressão no limite de requisições.
Erro transitório mantém UUID para recuperar o resultado idempotente. Ao sair da
página/recarregar, não existe retomada persistente; permanecer na página em caso
de resposta incerta. Conflitos detectados novamente pelo backend aparecem no relatório.

## Arquivos

- `src/central-contingencia-web/src/features/proxies/import/`: contracts.ts,
  api.ts, ProxyImportPage.tsx, ImportPreviewTable.tsx e ProxyImportPage.test.tsx.
- `src/central-contingencia-web/src/lib/api/client.ts`: suporte multipart/headers
  de confirmação e leitura opcional de ProblemDetails, preservando CSRF/cookies.
- `src/central-contingencia-web/src/app/App.tsx`: rota lazy protegida Admin.
- `src/central-contingencia-web/src/features/proxies/ProxiesPage.tsx`: link Admin
  e quebra de linha dos botões para acomodar a nova ação em telas pequenas.
- `scripts/Verify-CsvImportUi.cjs`: Chrome isolado com API totalmente mockada.
- AGENTS.md, CLAUDE.md, README.md, docs/ui/csv-import.md e este relatório.

## Testes e builds

| Verificação | Resultado |
| --- | --- |
| npm run lint | Aprovado |
| npm run typecheck | Aprovado |
| npm test | 95 aprovados: 63 existentes + 32 novos |
| npm run build | Aprovado, dois avisos conhecidos de comentários PURE de Zod/Rollup |
| dotnet test solução com PostgreSQL descartável | 250 aprovados: 134 unitários + 116 integração; 0 falhas/ignorados |
| dotnet build em .local/build-2.3c | Aprovado, 0 avisos/erros |
| Chrome/Playwright | Aprovado: quatro grupos de verificações; quatro capturas |
| Snapshot banco homologado antes/depois | Contagens/fingerprints idênticos |
| Bancos descartáveis remanescentes | Zero |

Novos testes: seleção explícita/extensão/vazio/tamanho, multipart/CSRF sem header
JSON, prévia/resumo, DOM/storage sem credenciais/prova, cabeçalhos/data inválidos,
região ausente e query idêntica em commit, duplicatas/conflitos, duplicata com erro
adicional, paginação integral, diálogo/cancelamento, duplo envio, falhas HTTP,
rede/503 e retry idempotente, 409 e nova prévia conservando UUID, relatório 422,
prova expirada/ausente, troca de arquivo, sessão expirada, volta à listagem e
permissões Admin/Manager/Operator. Fluxos backend de escrita testados exclusivamente
com dados sintéticos e PostgreSQL descartável pelas fixtures existentes.

A primeira rodada frontend apresentou timeouts de carregamento lazy em ambiente
frio e falha no teste de envio duplo; a trava foi antecipada para o handler síncrono.
Rodadas completas finais aprovadas. Build inicial no diretório 2.3B encontrou DLL
bloqueada pela API ativa: repetido em saída 2.3C isolada, sem parar/reiniciar a API.

### Capturas e evidências visuais

Chrome real, Playwright-core existente; contexto isolado, interceptação total de
/api e banner de mocks. Nenhuma chamada de escrita chegou à API homologada.
Prévia/cancelamento/confirmar/relatório, tela inválida mobile, prova expirada,
503/retry com mesma chave, papéis e ausência de console/DOM/storage sensível
foram verificados. Zero pageErrors e consoleLeaks no resultado JSON.

- [Prévia desktop](../.local/verification-2.3c/01-previa-desktop.png)
- [Confirmação explícita](../.local/verification-2.3c/02-confirmacao.png)
- [Relatório final](../.local/verification-2.3c/03-relatorio.png)
- [CSV inválido mobile](../.local/verification-2.3c/04-invalido-mobile.png)
- `.local/verification-2.3c/results.json`.

Capturas desktop/mobile inspecionadas visualmente; tabela possui rolagem horizontal
em tela pequena. Artefatos permanecem locais e ignorados pelo Git. Não houve fluxo
CSV autenticado da interface contra API real nesta rodada: navegador usou mocks;
o contrato/segurança/persistência reais foram validados pelos testes backend.

## Preservação e limites

Homologação manteve 4 usuários, 32 auditorias de autenticação, 1 proxy, 1 auditoria
de proxy, zero lotes/linhas de importação e quatro migrations. Sem login de teste,
importação, migration, modificação de chave ou registros homologados. API existente
e Vite permaneceram disponíveis. Sem commits/deploys ou etapa posterior iniciada.

Limite inicial de seleção 2 MiB é conservador e corresponde ao padrão atual; a API
pode configurar limite diferente e recusar upload. Muitas páginas podem atingir
rate limiting: UI informa espera, sem reenvio automático. A prévia mascara o host
e representa região por presença, reduzindo informação disponível para revisão.
Estado em memória não sobrevive a recarga/navegação. Não há progresso percentual
porque o transporte existente não fornece progresso medido. Limitações backend
documentadas na ADR 0007 continuam. Revisão humana ainda necessária.

## Reprodução e avaliação manual

```powershell
# src/central-contingencia-web
npm run lint
npm run typecheck
npm test
npm run build
# raiz, com Vite disponível e Chrome instalado
node scripts/Verify-CsvImportUi.cjs
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3c/
```

Backend testado com CENTRAL_TEST_POSTGRES carregado em memória pelo script local
de conexão, usando saída existente .local/build-2.3b e --no-build/--no-restore.
Os bancos de teste foram removidos. Não executar database update na homologação.

URL exata: **https://localhost:5173/proxies/import**. Autenticar como Admin ou
acessar Proxies → Importar CSV. Não confirmar escrita na homologação durante
avaliação visual sem autorização própria; não usar arquivos reais. Guia em
[docs/ui/csv-import.md](ui/csv-import.md).

**Etapa 2.3C implementada e validada automaticamente, aguardando a avaliação manual
do usuário antes de ser considerada homologada.**

## Correção posterior de compatibilidade

Decisões explícitas e resultados posteriores em
[csv-compatibility-report.md](csv-compatibility-report.md): duplicata reconhecida
vazia tratada sem ambiguidades; ano continua obrigatório no CSV; opção explícita
para ignorar linhas sem conexão vinculada à prova. Totais atualizados: 263 testes
.NET e 97 frontend aprovados; Chrome com mocks e capturas atualizados. Sem migrations
ou alterações de dados homologados. Homologação manual permanece pendente.
