# ADR 0005 — Interface administrativa de proxies

Status: aceita para Etapa 2.2B, 11/10/2026.

## Contrato e responsabilidades

UI organizada em features/proxies: contratos HTTP, adapter, validação/payload de
formulário, formulário, tabela e página. Mantém a arquitetura .NET e as APIs da
Etapa 2.2A. A API é autoridade final para autorização, validação e disponibilidade.
Admin/Manager veem cadastro/edição; Operator somente consulta; papel desconhecido
não dispara consultas. 403 continua negando acesso mesmo com ações visíveis.

TanStack Table usa manualPagination/manualSorting; a lista utiliza paginação,
filtros exatos e enumerações de ordenação existentes. Não simula busca parcial,
exclusão, reserva, conectividade ou importação. O GET /available atende o filtro
de disponíveis e os totais globais. As três consultas de totais solicitam pageSize=1
e usam total, sem extrapolar items. São snapshots independentes, sem consistência
transacional garantida. Vencidos usa expiresBefore com corte UTC do navegador;
o horário de consulta e a natureza independente são informados. available é o
booleano do servidor; nunca é deduzido de Active. Atualização periódica de 60s,
atualização manual e invalidação após escrita; falha de indicadores exibe “—”.

## Sessão e credenciais

Reutiliza api(), cookies seguros e obtenção de CSRF antes de cada POST/PATCH.
Sem JWT, CORS novo, duplicação de AuthProvider ou persistência de tokens.
201 com JSON é lido; 201 vazio e 204 seguem suportados. AbortSignal permite
cancelamento de consultas. Não lê payloads ProblemDetails para exibir possíveis
valores sensíveis; usa mensagens locais por código HTTP.

QueryClient é criado por identidade/papéis; cancela e limpa queries/mutations na
saída, expiração ou troca da sessão/papéis. Sem persistência, devtools de cache ou
logging de valores. Credenciais novas permanecem na memória para envio: variáveis
de mutation/formulário e campos de senha/usuário são limpos ao concluir uma tentativa
no servidor. Mutações não repetem automaticamente e têm gcTime=0. Falha de rede
orienta conferir a lista antes de repetir, pois POST não é idempotente.

GETs não contêm senha nem usuário em claro. Na edição, ambos os campos começam
vazios; vazio significa omitir/preservar. Somente campos alterados entram no PATCH.
O vencimento intacto é omitido, preservando segundos/microssegundos e permitindo
alterar outros campos de um proxy já vencido. Novo vencimento deve ser futuro.
Valores de datetime-local são convertidos para ISO UTC. O formulário não recupera
senhas persistidas, não oferece revelação ou exportação e não registra payloads.

## UX e evidências

Tailwind e componentes UI existentes; Dialog baseado em Radix, foco contido,
Escape/fechamento e restauração explícita ao controle que o abriu. Labels, descrições,
aria-invalid, alert/status e tabela semântica; scroll horizontal confinado à tabela.
Formulários responsivos e campos desabilitados durante o envio. Página carregada
por lazy import para separar seu bundle da autenticação.

Vitest/Testing Library cobrem contratos/formulários e fluxos; Playwright Core usa
Chrome instalado. Mocks somente no runner e identificados nas capturas. Integração
real usa API/Vite, banco e role descartáveis; chaves e senhas aleatórias em memória,
sem persistir estado autenticado/HAR/traces com payloads sensíveis. O harness valida
nomes UUID, destino local e ausência de INSERT da role na auditoria homologada.
Limitações e incidente de auditoria ocorrido antes de corrigir o isolamento constam
no relatório da Etapa 2.2B. Nenhum novo contrato, migration ou código de domínio/API.
