# Etapa 2.4C.2 — Grid Operacional Unificado

Data: 12/10/2026. Implementada e validada tecnicamente; **aguarda homologação manual do usuário**.

## Implementação

`https://localhost:5173/proxies` é a tela operacional principal. Consome exclusivamente
`GET /api/v1/proxies/operational-grid` para montar a listagem, sem juntar páginas de
proxies, perfis ou histórico no navegador. TanStack Table usa paginação, filtros e
ordenação manuais; TanStack Query consulta com cancelamento e cache por parâmetros.

As nove colunas são Perfil, Plataforma 1, Plataforma 2, Proxy, Porta, Região,
Vencimento, Disponibilidade e Ações. Campos do perfil ficam vazios quando não há
vínculo. Atribuições usam etiquetas discretas. O status operacional aparece abaixo
do host, separado da disponibilidade. Perfis vinculados a proxies vencidas ou
suspensas continuam visíveis. Disponibilidade é exibida exatamente como recebida;
o indicador de vencimento usa o instante `evaluatedAt` retornado pela API.

Filtros combináveis: perfil/região parciais, plataforma (qualquer atribuição),
disponibilidade e vencimento ascendente/descendente. Buscas têm debounce de 350 ms.
Página, tamanho (20/50/100) e filtros ficam na URL; navegação para detalhes,
administração e importação preserva o retorno ao grid filtrado. Filtros alterados
reiniciam a página. O total vem do servidor, sem estimativas a partir da página.

Admin/Manager têm cadastro/edição de proxies e reserva a partir da linha disponível.
Operator consulta detalhes e histórico. CSV permanece exclusivo Admin. A reserva
usa os contratos existentes, CSRF e chave de idempotência em memória, seguindo para
os detalhes do perfil. Confirmação, liberação com confirmação explícita e histórico
foram preservados. Mutações invalidam consultas de perfis/proxies. Atualização a cada
30 segundos e no prazo de reserva solicita novamente a disponibilidade ao backend.

Não há segunda listagem no menu: `/profiles` permanece administração secundária,
incluindo perfis sem proxy. Cadastro aceita duas atribuições opcionais; não exige
preenchimento inventado da plataforma textual legada. Cadastro/edição de proxies e
importação CSV continuam disponíveis. Componentes/layout existentes preservados.

## Segurança e preservação

Cliente HTTP, cookies, autorização, CSRF e mensagens sanitizadas existentes foram
reutilizados. O contrato de resposta do grid é validado com Zod e campos desconhecidos
são descartados antes do cache. Nenhuma senha, token ou credencial é renderizada.
Arquivo/prova CSV continuam somente em memória. Não foi acrescentado armazenamento
em localStorage/sessionStorage nem logging de conteúdo sensível.

Nenhuma alteração no backend, domínio, migrations ou chaves. Nenhuma importação ou
escrita de teste na homologação. Snapshot anterior `.local/24c2-before.txt` e estrutura
correspondente foram comparados integralmente pelo script existente: **16 tabelas,
contagens, fingerprints e estruturas preservados**, incluindo **131 proxies**.
Nenhum perfil/alocação real criado. Sem commits ou deploys.

## Arquivos

- Nova feature `src/central-contingencia-web/src/features/proxies/grid/`: contratos,
  cliente validado, filtros, tabela, seleção de perfil para reserva, navegação segura
  de retorno e testes do grid.
- `ProxiesPage.tsx` substituída pelo grid; antiga `ProxyTable.tsx` removida.
- `AuthenticatedLayout.tsx` e `App.tsx`: entrada operacional única.
- `ProfilesPage.tsx`, `ProfileDetailsPage.tsx`, `ProfileForm.tsx`, contratos de perfis
  e `ProxyImportPage.tsx`: administração secundária, atribuições e retorno filtrado.
- Testes existentes de proxies, perfis e CSV ajustados e preservados.
- `scripts/Verify-OperationalGridUi.cjs`: Chrome real com API totalmente interceptada.
- AGENTS.md, CLAUDE.md e guia de perfis atualizados para refletir a etapa autorizada.

## Validação

| Verificação | Resultado |
|---|---|
| `npm run lint` | Aprovado |
| `npm run typecheck` | Aprovado |
| `npm test -- --run --maxWorkers=1` | 145 testes, 6 arquivos, todos aprovados |
| `npm run build` | Aprovado, Vite 7.3.7 |
| Chrome via Playwright | 11 verificações aprovadas, mocks sintéticos |
| Comparação PostgreSQL em leitura | Dados e estruturas preservados |
| HTTPS frontend `/proxies` | HTTP 200 |
| HTTPS API `/health` | HTTP 200 |

Os testes cobrem filtros combinados/debounce, busca parcial, limpeza, URL, ordenação,
paginação/total global, quatro disponibilidades, perfil ausente, vencido vinculado,
atribuições, ausência de consultas N+1, permissões, falhas HTTP, contratos malformados,
omissão de credenciais, navegação e reserva com CSRF/conflito. Testes existentes de
criação/edição de proxies e prévia/confirmação CSV continuam passando. Quatro novos
casos verificam cadastro de perfil sem, com uma ou com duas atribuições.

Chrome exercitou 40 linhas sintéticas, filtros/retorno/reload, paginação, reserva,
confirmação, cancelamento da liberação, liberação/reutilização e histórico, conflito
409, cadastro de perfil, viewport móvel, recuperação de erro e Manager/Operator.
Sem erros JavaScript, vazamentos observados no DOM/console ou armazenamento local.
Todas as requisições `/api/` foram interceptadas; operações de escrita ficaram nos mocks.

Logs locais: `.local/24c2-tests-final.log`; evidências Chrome:
`.local/verification-2.4c2/results.json`. Capturas sintéticas inspecionadas:

- [Grid desktop](../.local/verification-2.4c2/01-grid-desktop.png)
- [Filtros combinados](../.local/verification-2.4c2/02-grid-filtrado.png)
- [Reserva e detalhes](../.local/verification-2.4c2/03-reserva-detalhes.png)
- [Conflito](../.local/verification-2.4c2/04-conflito.png)
- [Mobile](../.local/verification-2.4c2/05-grid-mobile.png)
- [Operator](../.local/verification-2.4c2/06-operator.png)

Capturas e logs permanecem locais em `.local`; não contêm registros reais.
O build emitiu somente avisos de comentários de anotação do pacote Zod removidos
pelo Rollup; não houve falha de compilação. Backend não foi alterado nem seus testes
reexecutados nesta etapa exclusivamente frontend.

## Limitações e homologação manual

A escolha do perfil na reserva reutiliza a listagem existente, cuja pesquisa por
nome é exata. Esse endpoint não filtra perfis sem vínculo; o backend revalida e pode
retornar 409. A busca parcial exigida no grid é enviada ao endpoint operacional.
Nenhum endpoint ou regra nova foi inventado para esconder essa limitação.

Não houve benchmark com milhares de registros reais: grid mantém paginação no
servidor e testes verificam total global de 2.000 sem carregar tudo. Mobile mantém
rolagem horizontal dentro da tabela, sem alargar a página. O catálogo frontend
corresponde ao aprovado; alterações futuras exigem sincronização do contrato.

Fluxos de escrita foram validados com mocks, não pela gravação de dados homologados.
A avaliação manual autenticada continua necessária. Frontend permanece disponível
em **https://localhost:5173/proxies**, usando a API **https://localhost:7090**.
Login utiliza a sessão/conta existente. Não é necessário criar perfis ou importar
CSV para avaliar a listagem das 131 proxies.

Próximo passo: revisão manual e aprovação do usuário. Não iniciar Etapa 2.4C.3
automaticamente nem considerar esta etapa homologada antes dessa aprovação.
