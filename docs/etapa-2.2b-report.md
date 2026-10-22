# Etapa 2.2B — Interface React de Gestão de Proxies

Data: 11/10/2026. Escopo implementado e validado localmente; avaliação visual do
usuário permanece como próximo passo. **Houve dois eventos involuntários de auditoria
de login no banco homologado durante um ensaio inicial; detalhes e correção abaixo.**

## Inspeção e plano

AGENTS.md, CLAUDE.md e docs/etapa-2.2a-report.md lidos integralmente antes de alterar
arquivos. Inspecionados layout/autenticação, cliente HTTP, dependências, contratos
reais de POST/PATCH/GET, scripts e ambiente local. Plano apresentado: feature React,
consultas e tabela no servidor, formulários RHF/Zod com PATCH parcial e credenciais
omitidas, cache por sessão, CSRF compartilhado, testes e Chrome com dados sintéticos.

Versões verificadas: SDK .NET 10.0.300; Node 22.22.3; npm 10.9.8; React 19.3.0;
TypeScript 5.9.3; Vite 7.3.7; PostgreSQL 17 local; Chrome 154.0.8037.98.
Novas dependências: TanStack Query 5.104.1, TanStack Table 8.21.3, Radix Dialog 1.2.0
e Playwright Core 1.64.0 como ferramenta de desenvolvimento. RHF 7.89.0 e Zod 4.6.5
reutilizados. Instalação npm informou zero vulnerabilidades na auditoria automática.

## Funcionalidades concluídas

- Rota protegida /proxies e navegação no layout autenticado, com sidebar desktop
  e navegação adaptada no mobile. Tela inicial direciona para a gestão de proxies.
- TanStack Table com tabela semântica, ordenação/paginação manuais no servidor;
  filtros por host exato, porta, região exata, status, vencimento máximo e disponíveis.
  Sete ordenações da API, direção, tamanhos 10/20/50/100 e limite de 10000 páginas.
- Cartões globais usam total de três consultas pageSize=1; não contam a página atual.
  Disponíveis usa /available. Vencidos usa expiresBefore inclusivo com instante UTC
  do navegador. Horário e independência dos snapshots informados. available vem
  da API; Active não é usado como substituto. Falha dos indicadores exibe “—”.
- Cadastro e edição com RHF/Zod: DNS/IPv4 canônico/IPv6, porta, usuário, região,
  status, vencimento e limite de bytes UTF-8 da senha. API revalida todos os campos.
  Novo vencimento futuro; editar outros campos de registro vencido continua permitido.
- Edição consulta GET /{id}. Credenciais começam vazias e são preservadas por omissão.
  PATCH envia somente campos alterados, sem nulls ou objeto vazio. Vencimento intacto
  não é arredondado nem reenviado. Datas do formulário são convertidas para ISO UTC.
- TanStack Query com cancelamento, cache por identidade/papéis, limpeza ao sair,
  expirar ou mudar sessão/papéis, invalidação após escrita e atualização a cada 60s.
  Mutações sem repetição automática; falha de rede orienta consultar antes de repetir.
- Reutilização de cookies/CSRF no cliente HTTP existente, estendido para PATCH e
  leitura de JSON em 201. 204 e 201 vazio continuam suportados. Nenhum JWT/token novo.
- Admin/Manager veem ações de escrita; Operator consulta; papel desconhecido não
  dispara consultas. 403 da API prevalece sobre a permissão visual. Estados de
  loading, erro/retry, vazio, sucesso, acesso negado e sessão expirada implementados.
- Usuário persistido somente mascarado; senha persistida nunca é consultada ou
  exibida. Novas credenciais em memória, limpas após tentativa no servidor; sem
  storage, logs de payload ou persistência de cache. Erros HTTP usam mensagens locais.
- Componentes UI Tailwind/shadcn existentes e Dialog Radix com foco contido,
  Escape/fechamento/restauração de foco; labels/descrições/aria-invalid/alert/status,
  tabela com aria-sort e scroll horizontal confinado. Formulário responsivo.
- Lazy import da página de proxies separa seu bundle da autenticação.

Nenhum endpoint, DTO, migration, entidade ou regra de negócio do backend alterado.
Sem frontend para importação, exclusão, alocações, perfis ou integrações externas.

## Arquivos alterados/criados

Todos os caminhos frontend abaixo são relativos a src/central-contingencia-web:

| Grupo | Arquivos |
| --- | --- |
| Dependências | package.json, package-lock.json |
| App e sessão | src/app/App.tsx, src/app/QueryScope.tsx |
| Layout/autenticação | src/components/layout/AuthenticatedLayout.tsx; src/features/auth/LoginPage.tsx (somente texto informativo) |
| UI | src/components/ui/button.tsx, input.tsx, select.tsx, dialog.tsx |
| HTTP compartilhado | src/lib/api/client.ts |
| Feature | src/features/proxies/contracts.ts, api.ts, form.ts, ProxyForm.tsx, ProxyTable.tsx, ProxiesPage.tsx |
| Testes | src/features/proxies/form.test.ts, ProxiesPage.test.tsx |

Na raiz: scripts/Verify-ProxyUi.cjs, scripts/Test-ProxyUiIntegration.ps1 e
scripts/browser/{BrowserFixture.csproj,Program.cs}. O harness não integra a solution
nem o produto; usa Infrastructure somente para preparar/verificar fixtures locais.

Documentação: AGENTS.md, CLAUDE.md, README.md, docs/architecture/README.md,
docs/api/README.md, docs/decisions/0005-proxy-management-ui.md, docs/ui/proxies.md e
este relatório. Artefatos locais ignorados: .local/verification-2.2b. O diretório
atual não é um repositório Git inicializado; nenhum commit/deploy foi realizado.

## Builds e testes executados

| Comando | Resultado final |
| --- | --- |
| dotnet build CentralContingencia.slnx --no-restore | Aprovado; 0 erros/avisos |
| ./scripts/Test-LocalPostgres.ps1 | 153 aprovados: 67 unitários + 86 integração; 0 falhas/ignorados |
| npm run lint | Aprovado |
| npm run typecheck | Aprovado |
| npm test | 63 aprovados: 7 autenticação existentes + 32 validação/payload + 24 fluxos/componentes |
| npm run build | Aprovado; dois avisos de anotação PURE em código da dependência Zod, removida pelo Rollup |
| npm run test:browser:proxies | 9 verificações Chrome com mocks aprovadas; 0 exceções JavaScript |
| ./scripts/Test-ProxyUiIntegration.ps1 | Aprovado integralmente: 9 verificações Chrome, persistência/cifra/auditoria/logs e limpeza |

Frontend build final: CSS ~20,27 kB; bundle inicial ~445,46 kB (gzip ~140 kB);
chunk da página ~121,26 kB (gzip ~37,54 kB). O aviso inicial de chunk >500 kB foi
resolvido pelo carregamento separado da feature; sem mudanças na compilação .NET.

Cobertura nova: permissões Admin/Manager/Operator/negado; totais globais independentes
de items; filtros/páginas/ordenação enviados ao servidor; POST/201 e PATCH com CSRF;
credenciais/vencimento intactos omitidos; troca explícita de credenciais; formulário
inválido, IPv4 ambíguo, UTF-8 inválido/limite; vencimento passado; PATCH vazio;
400/403/404/409/429/503; limpeza de credenciais após falha e ausência de repetição;
loading/vazio/retry; 401/sessão e logout seguido de novo login com dados novos.

A suíte .NET existente preserva validação/autorização, criptografia/integridade,
persistência, concorrência, CSRF, lockout, rate limiting, login/logout e auditoria.
Escritas desses testes usam bancos p6_auth_test_UUID descartáveis.

## Chrome e integração real

Chrome instalado, controlado por Playwright Core em modo headless. Inspeção visual
das capturas desktop/mobile/formulários realizada. 1440x1050 desktop e 390x844 mobile.
Mocks identificados por faixa de evidência; apenas /api/ é interceptado, preservando
os módulos Vite em /src/lib/api/. Nenhum mock foi incluído na aplicação entregue.

Integração real final usa Vite 5174 com proxy exclusivo para API 7091 e PostgreSQL
p6_ui_test_UUID, com role homônima NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOINHERIT.
O harness confirma database/user e ausência de INSERT na auditoria homologada.
Aplica as três migrations existentes somente nessa fixture e provisiona Admin,
Operator e Manager sintéticos. Senhas aleatórias e chave AES temporária em memória;
sem argumentos sensíveis, arquivos de credenciais, storageState, HAR ou traces.
Os rate limits são ampliados apenas na API descartável para os cenários sequenciais.

Validado: login pelo formulário real, sessão com cookie HttpOnly/Secure/Strict,
listagem vazia, POST sem CSRF 400, cadastro 201, GET/detalhe/PATCH, campos omitidos,
respostas mascaradas/invalidação, logout/me 401, Operator lê e POST forçado recebe
403, Manager vê gestão, remoção de cookies seguida de me 401 e aviso de sessão
expirada. Última rodada: nove verificações e zero exceções JavaScript.

A fixture final confirmou um proxy, dois eventos de proxy (criação/alteração),
senha decifrável pelo adapter AES somente na verificação interna e auditoria sem
senha. Os logs isolados não continham chave, senha de login ou senha sintética do
proxy. Nos corpos HTTP observados, zero ocorrências de senha/usuário em claro,
ciphertext/keyId. Dois corpos abandonados/cancelados durante troca de sessão não
puderam ser lidos integralmente pelo runner; leitura possui limite de 2s. Essa
limitação é registrada, sem afirmar inspeção de todo tráfego possível. Respostas
e auditoria sanitizadas também são verificadas pela suíte .NET.

API/Vite/banco/role isolados encerrados/removidos ao final. TLS autoassinado ignorado
somente nos contextos de teste localhost; confiança do Windows não foi alterada.
A perda de sessão no Chrome foi provocada por remoção de cookies, sem esperar
30 minutos reais; a expiração temporal é coberta pelos testes existentes .NET.

## Capturas e evidências

Arquivos locais, ignorados pelo Git, com dados exclusivamente sintéticos:

| Evidência | Arquivo em .local/verification-2.2b |
| --- | --- |
| Lista desktop com mocks e totais globais | mocks-desktop.png |
| Cadastro e edição mascarada | mocks-cadastro.png; mocks-edicao-mascarada.png |
| Vazio e erro | mocks-vazio.png; mocks-erro.png |
| Operator mobile | mocks-mobile-operator.png |
| Formulário mobile e área de credenciais/ações | mocks-mobile-cadastro.png; mocks-mobile-cadastro-credenciais.png |
| API real após PATCH e Operator | api-descartavel-desktop.png; api-descartavel-operator.png |
| Edição com API real | api-descartavel-edicao-mascarada.png |
| Resultados sanitizados | mocks-results.json; api-descartavel-results.json; final-readonly-results.json |
| Logs | isolated-api-stdout.log; isolated-api-stderr.log; vite-stdout.log; vite-stderr.log |

![API real, PostgreSQL descartável e dados sintéticos](../.local/verification-2.2b/api-descartavel-desktop.png)

![Formulário mobile com mocks sintéticos](../.local/verification-2.2b/mocks-mobile-cadastro.png)

Capturas/JSON ficam no workspace local. Para compartilhar o relatório fora dele,
copiar apenas as capturas positivas e JSON sanitizados; não compartilhar logs ou
arquivos locais de segredos. Não são necessárias credenciais para abrir capturas.

## Incidente de isolamento e correções

O primeiro runner tinha uma interceptação ampla que capturava também um módulo
Vite; restringida ao pathname /api/. Depois, a fixture criava corretamente um banco
temporário, mas a conexão da API de teste ainda apontava para a homologação: a
atribuição .ConnectionString do DbConnectionStringBuilder foi interpretada pelo
adaptador PowerShell como palavra-chave, preservando a conexão original.

**Duas tentativas de login de um usuário sintético inexistente geraram LoginFailed
no banco homologado, elevando a auditoria de autenticação de 15 para 17.** Não houve
criação de proxies, auditorias de proxy, usuários ou lockout de conta homologada.
Os dois eventos têm ActorId e ResourceId nulos e permanecem preservados; não foram
apagados ou alterados, pois a remoção exigiria nova autorização. O ocorrido foi
comunicado ao usuário durante a execução. A restrição de não gravar no banco foi
violada por esses eventos involuntários; não se declara preservação integral dos dados.

Correções: setters explícitos set_ConnectionString/set_Item/get_ConnectionString,
validação do destino antes do startup e role exclusiva sem permissão para inserir
auditoria homologada. O runner real passou a usar um Vite separado e proxy normal,
evitando efeitos de cookie do forwarding Playwright. Leitura de corpos cancelados
limitada no tempo; logs em uso lidos com compartilhamento ReadWrite; fixture e role
removidas no finally. Os ensaios iniciais falharam antes da rodada final aprovada.
Falhas iniciais de testes de componentes eram seletores sem escopo do modal e falta
de espera pelo detalhe; corrigidas. O ensaio de escrita após ordenação foi ajustado
para localizar o novo proxy por filtro exato em vez de presumir sua página.

Estado final confirmado por consultas somente leitura:

| Item | Antes | Final |
| --- | ---: | ---: |
| Migrations homologadas | 3 | 3 |
| Usuários homologados | 3 | 3 |
| Auditorias de autenticação | 15 | 17 |
| Proxies homologados | 0 | 0 |
| Auditorias de proxies | 0 | 0 |
| Tentativas falhas acumuladas/contas bloqueadas | — | 0 / 0 |
| Bancos descartáveis remanescentes | 0 | 0 |
| Roles de UI descartáveis remanescentes | 0 | 0 |

Sem migrations novas/aplicadas na homologação, importações, dados reais, alterações
de arquitetura, commits ou deploys. Nenhuma correção ou limpeza de dados homologados
foi executada após detectar o incidente.

## Execução local e avaliação visual

Frontend deixado em https://localhost:5173/proxies (Vite PID 23068); API existente
em https://localhost:7090 (PID 5044). PostgreSQL saudável em 127.0.0.1:55432.
Confirmação final somente leitura: frontend/health/OpenAPI 200; auth/me e consultas
de proxies anônimas 401; cinco operações REST e schema de resposta sem senha mantidos.
Os PIDs/serviços são estado desta execução e podem mudar ao reiniciar o computador.

Usar sua conta interna existente para avaliação. O banco homologado continua sem
proxies; as capturas mostram dados sintéticos em mocks ou fixture já removida.
Não foi deixado login de teste ou bypass de autenticação no produto. Avaliação
visual sem escrita é possível; novo cadastro/alteração homologada exige autorização.

Para reiniciar em terminais PowerShell distintos:

```powershell
# Raiz: chave DPAPI existente, somente no processo; nunca imprimir variáveis.
. ./scripts/Set-LocalProxyEncryption.ps1
./scripts/Start-LocalApi.ps1
```

```powershell
cd src/central-contingencia-web
npm ci
npm run dev
```

Não executar migration/provisionamento no banco homologado para iniciar a UI.
Validação reproduzível: comandos da tabela, node scripts/Verify-ProxyUi.cjs com
Vite 5173 ativo, e ./scripts/Test-ProxyUiIntegration.ps1 na raiz para a fixture real.
Este último exige .env local, portas 7091/5174 livres e permissão PostgreSQL para
CREATE ROLE/DATABASE. Capturas também estão acessíveis pelos caminhos acima.

## Limitações e próximos passos

- Aprovar aparência/fluxos pelo usuário. Chrome validado; não houve revisão com
  leitor de tela nem matriz Safari/Firefox. Tab/foco/Escape/labels e mobile verificados.
- Cartões são consultas independentes e podem divergir durante alterações; não há
  endpoint de resumo atômico. Horário local do cliente afeta o corte expiresBefore
  e o rótulo de vencido, mas não substitui a disponibilidade enviada pelo servidor.
- Não há alocações, teste de conectividade, exclusão, importação, idempotency key ou
  ETag. PATCH segue integralmente o contrato existente e não promete prevenção de
  toda edição baseada em leitura antiga. Nenhuma funcionalidade adicional iniciada.
- Dois eventos de auditoria involuntários permanecem documentados; qualquer remoção
  ou outra alteração homologada depende de aprovação explícita.
- MFA/gestão de chaves e requisitos de produção permanecem fora desta etapa;
  certificados de desenvolvimento não substituem TLS válido em produção.
- Após avaliação desta etapa, definir e autorizar separadamente a próxima fatia.
