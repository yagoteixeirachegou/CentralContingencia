# Etapa 2.4C — Frontend de perfis operacionais e alocações

Data: 11/10/2026. **Implementação e validação automática concluídas; aguardando
homologação manual do usuário.** Não iniciar outra etapa automaticamente.

## Inspeção e escopo

Lidos integralmente AGENTS.md, CLAUDE.md, relatório de homologação 2.4B e contrato
REST operacional. Inspecionados os padrões React/Query, cliente HTTP, AuthProvider,
layout, componentes UI, proxies e harnesses de testes. Node 22.22.3 / npm 10.9.8;
stack/pacotes existentes preservados, sem dependência nova.

React 19/TypeScript/Vite, TanStack Query/Table, React Hook Form/Zod e componentes
Tailwind/shadcn existentes. Nenhum redesign, CSS global, animação ou refatoração
visual de módulos anteriores. Nenhuma mudança de contrato/backend, autenticação,
criptografia, chave, migration homologada ou integração externa.

## Funcionalidades entregues

- Menu **Perfis**, visível para Admin, Manager e Operator, integrado ao layout atual.
- `/profiles`: listagem server-side, filtros exatos de nome/plataforma/status,
  ordenação por whitelist da API, direção e paginação limitada a 100 itens/10000 páginas.
  Tabela TanStack com link para detalhes, loading, vazio, erro e acesso negado.
- Cadastro em dialog compartilhado, formulário RHF/Zod, nome/plataforma obrigatórios
  e limites de entrada do contrato. API mantém a validação de domínio. Sem enum de
  plataforma inventado, campos externos ou associação automática. Sucesso abre detalhes.
- `/profiles/:id`: metadados, estado do perfil, proxy/reserva atual, prazo e histórico.
- Consulta de alocação atual separada da página parcial do histórico: duas consultas
  filtradas Reserved/Confirmed, profileId, pageSize=1. Leituras transitórias incompatíveis
  geram aviso de conflito/atualização, sem decisão arbitrária sobre vínculo.
- Seleção paginada de proxies exclusivamente de `/proxies/available`, com pesquisa de
  host exato. Radio/seleção sem credenciais; não inferir disponibilidade pelo status Active.
- Reserva envia profileId/proxyId e Idempotency-Key UUID por operação, em memória.
  Omitido reservationExpiresAt: prazo definido e limitado pelo backend.
- Confirmação `{}` usa endpoint idempotente existente, sem replay próprio ou nova
  alocação. Guardas de envio evitam clique simultâneo; mutations retry=false.
- Liberação `{}` exige dialog de confirmação explícita e admite cancelamento sem POST.
  Não promete disponibilidade automática: proxy deve continuar válida/operacional pela API.
- Histórico paginado/filtrado: UUID da proxy, estado, criação, prazo, confirmação,
  término e motivo. Perfil encerrado mantém histórico e não oferece nova reserva.
- Invalidação de profiles/allocations/proxies após sucesso ou falha de mutação;
  alocação atual/histórico atualizam a cada 15s. Cache isolado por sessão existente.
- Prazo real mostrado em horário local. Timer no limite do prazo/foco atualiza indicação
  de vencimento sem mutação. API revalida tempo e exclusividade; nenhuma ação automática.
- HTTP 409, 400/CSRF, 401, 403, 404, 429 e 500/503 têm mensagens sanitizadas. Falha
  incerta de cadastro bloqueia novo envio no mesmo formulário e orienta consultar lista.
- Nenhuma senha/token é renderizada; usuário de proxy permanece `***`. Sem storage,
  logs sensíveis, exportação de credenciais ou mecanismos novos de autenticação.

Regras de negócio continuam no backend: exclusividade, recuperação, transação,
disponibilidade, expiração, reutilização e auditoria. Cliente só orienta e envia
ações explícitas. Guia: [operational-profiles.md](ui/operational-profiles.md).

## Contratos consumidos

GET/POST `/api/v1/profiles`, GET `/api/v1/profiles/{id}`,
GET `/api/v1/profiles/{id}/allocations`, GET `/api/v1/allocations` filtrado,
POST `/api/v1/allocations/reserve`, POST `/api/v1/allocations/{id}/confirm`,
POST `/api/v1/allocations/{id}/release`, GET `/api/v1/proxies/available` e
GET `/api/v1/proxies/{id}`. Todos usam cliente HTTP/CSRF/sessão existentes.

Admin/Manager cadastram/reservam/confirmam/liberam; Operator consulta. Papéis
desconhecidos/Integration recebem acesso negado e não disparam queries da feature.
Backend continua autoridade final em qualquer papel ou estado desatualizado.

## Arquivos alterados/criados

Frontend novo em `src/central-contingencia-web/src/features/profiles/`:
contracts.ts, api.ts, ProfilesPage.tsx, ProfileForm.tsx, ProfileDetailsPage.tsx,
ReserveProxyForm.tsx, AllocationHistory.tsx, OperationalPagination.tsx e
ProfilesPage.test.tsx. Modificados App.tsx (rotas lazy) e AuthenticatedLayout.tsx
(link adicional), sem refatorar páginas existentes.

Scripts: Verify-ProfileUi.cjs e Test-ProfileUiIntegration.ps1. Documentação:
este relatório, docs/ui/operational-profiles.md, AGENTS.md, CLAUDE.md e correção
da nota desatualizada sobre migrations em docs/api/operational-profiles-allocations.md.
Nenhum código-fonte backend ou mecanismo criptográfico foi alterado.

## Testes e builds executados

| Verificação final | Resultado |
| --- | --- |
| Frontend lint | Aprovado |
| TypeScript estrito/typecheck | Aprovado |
| Vitest completo | **128 aprovados**, 5 arquivos, zero falhas |
| Casos novos de componentes/fluxos | 24 aprovados |
| Frontend build | Aprovado; dois avisos existentes Rollup em comentários de Zod |
| .NET unitários completos | 200 aprovados, zero falhas/ignorados |
| .NET integração completos | 161 aprovados, zero falhas/ignorados |
| Build .NET completo | Aprovado, zero erros/avisos |
| Build harness BrowserFixture existente | Aprovado, zero erros/avisos |
| Playwright Chrome com mocks | 15 verificações aprovadas |
| Playwright Chrome com API/PostgreSQL descartáveis | 8 verificações aprovadas |
| Erros de página/vazamentos detectados no Chrome | Zero / zero |
| Comparação homologada antes/depois | Dados/estruturas das 16 tabelas preservados |
| DPAPI/keyring antes/depois | Bytes preservados |
| Bancos/roles descartáveis remanescentes | Zero |
| Disponibilidade final | API /health=200 Healthy; frontend /profiles=200 |

Vitest cobre filtros, ordenação/paginação, cadastro/validação/detalhes, payloads reais,
UUID/CSRF, reserva/confirmação, liberação/cancelamento/reutilização/histórico, prazo
vencido e timer, conflitos/erros HTTP, cache atualizado, papel desconhecido, Operator,
Admin/Manager, perfil Closed, sessão expirada, dupla confirmação, resultado incerto
de cadastro, ausência de storage/credenciais e leituras inconsistentes da alocação.

A suíte backend completa de **361 testes** preservou autenticação, criptografia,
proxies/CSV e garantias transacionais de alocações. Fixtures usam somente PostgreSQL
descartável e dados sintéticos, nunca o banco homologado para escrita.

Chrome com mocks valida listagem, filtros, paginação, cadastro, detalhes, reserva,
confirmação, cancelamento/liberação, reutilização, 409 sem retry, expiração, erro de
serviço, permissões Operator/Manager e mobile 390x844 sem overflow da página.

Chrome com API real usa Vite temporário 5174 → API 7091 → banco/role `p6_ui_test_<UUID>`.
Harness existente garante role sem INSERT na auditoria homologada; segredos gerados
em memória. Fluxo real: login, proxy sintética preparada via API, perfil cadastrado
pela interface, reserva, confirmação, liberação com cancelamento prévio, histórico,
reutilização e cookie HttpOnly/Secure/SameSite=Strict. Dados, servidor, banco e role
descartáveis removidos no finally. Configurações temporárias não afetam API principal.

Logs da instância descartável e respostas/console/DOM de Chrome foram conferidos
contra credenciais/chave sintéticas. Não foram capturados HAR/traces ou dumps de
payloads. Formulário de login não foi fotografado com a senha preenchida. Browser
contexts isolados, sem acesso ao perfil pessoal do Chrome.

Comandos executados:

```powershell
# Dentro de src/central-contingencia-web:
npm run lint
npm run typecheck
npm test
npm run build

# Na raiz:
node scripts/Verify-ProfileUi.cjs
./scripts/Test-ProfileUiIntegration.ps1
. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES=$env:ConnectionStrings__DefaultConnection
try {
  dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-24c-check/
} finally { Remove-Item Env:CENTRAL_TEST_POSTGRES }
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-24c-check/
```

Ocorrências resolvidas durante validação: testes passaram a aguardar atualização
de queries antes de localizar paginação; seletor de prazo e seletor Chrome de estado
foram delimitados para evitar ambiguidades com cabeçalhos/opções do filtro. Removida
opção TypeScript inválida de seletor de teste. Uma chamada npm foi feita na raiz sem
package.json e repetida no diretório frontend correto. Resultados finais acima.
Nenhum teste foi removido ou ignorado para obter aprovação.

## Capturas e evidências

Arquivos em `.local/verification-2.4c/`, ignorados pelo Git, com identificador de
teste e dados sintéticos. Capturas desktop/mobile foram inspecionadas visualmente.
Layout existente preservado; tabela histórica tem rolagem horizontal interna em mobile.

- [Listagem desktop mockada](../.local/verification-2.4c/mocks-listagem-desktop.png).
- [Reserva desktop com API descartável](../.local/verification-2.4c/api-descartavel-reserva-desktop.png).
- [Confirmação da liberação real](../.local/verification-2.4c/api-descartavel-confirmacao-liberacao.png).
- [Conflito 409](../.local/verification-2.4c/mocks-conflito.png).
- [Detalhes mobile e prazo vencido](../.local/verification-2.4c/mocks-detalhes-mobile.png).
- [Consulta Operator](../.local/verification-2.4c/mocks-operator-consulta.png).

Resultados sanitizados: mocks-results.json e api-descartavel-results.json no mesmo
diretório. Nenhuma captura contém credencial persistida ou registro real da empresa.

## Preservação e limites

Snapshot inicial `.local/24c-before.txt` e sidecar de estruturas comparados ao terminar,
com todos os dados/estruturas homologados idênticos. Nenhum login automatizado foi feito
na homologação, pois geraria auditoria. Chave DPAPI e duas chaves do keyring continuam
iguais às cópias de backup anteriores. Seis migrations homologadas não foram reaplicadas.
Fixtures de testes preparam schema somente nos bancos novos descartáveis.

Não foram implementados edição/encerramento de perfil na interface, pois a lista
autorizada pediu cadastro/consulta e gestão de alocações. APIs anteriores permanecem
existentes. Plataforma segue texto livre validado; sem n8n/AdsPower/IDs externos.

Histórico apresenta UUID da proxy conforme contrato, sem presumir host atual de todos
os registros nem fazer consultas N+1. Consulta atual usa duas leituras independentes
e pode exigir refresh durante transições concorrentes. Relógio local só orienta
indicação de prazo; decisão de negócio sempre do servidor. Contagens não são inferidas
da página parcial. Reserve/release têm 409 em retry, como contrato homologado;
interface não repete mutations automaticamente.

Certificado HTTPS local é de desenvolvimento, aceito pelo harness. Validação
automática não equivale a homologação manual, produção, benchmark ou teste externo.
API local existente foi mantida em execução; Vite principal permaneceu disponível.
Nenhuma migration, importação real, commit, deploy ou próxima etapa foi executada
na homologação.

## Avaliação manual

Abrir **https://localhost:5173/profiles**, ou clicar **Perfis** no menu, com seu usuário
de homologação existente. Detalhes em `/profiles/{UUID}` ao clicar no nome da listagem.
Interface pode estar vazia, pois todos os registros gerados pelos testes foram
descartados. Não foram inseridos dados demonstrativos no banco homologado.

**Etapa implementada e validada automaticamente, aguardando sua homologação manual
antes de ser considerada encerrada.** Não iniciar outra etapa automaticamente.
