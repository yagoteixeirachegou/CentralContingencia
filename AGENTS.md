# AGENTS.md — Central de Contingência

## Instruções obrigatórias

Leia integralmente o arquivo `CLAUDE.md` antes de qualquer alteração.

O `CLAUDE.md` é a fonte de verdade para:
- Stack tecnológica
- DDD e Clean Architecture
- Organização da solução
- Segurança e autenticação
- Modelagem de domínio
- Padrões de desenvolvimento
- Testes e documentação

## Regras de execução

1. Inspecione o repositório antes de modificar arquivos.
2. Trabalhe incrementalmente, uma etapa por vez.
3. Não implemente funcionalidades fora do escopo solicitado.
4. Não execute operações em produção.
5. Não realize commits ou deploys sem autorização.
6. Execute builds e testes após as alterações.
7. Informe os resultados reais, erros e pendências.
8. Responda em português brasileiro.

## Prioridades e preservação da interface

Priorizar funcionalidades, regras de negócio, segurança, integrações e testes,
sempre dentro do escopo autorizado para cada etapa.

Manter o layout visual atual. Não realizar redesign, alterações cosméticas,
animações ou refatorações visuais sem solicitação explícita do usuário.
Mudanças na interface são permitidas quando necessárias para disponibilizar novas
funcionalidades ou corrigir problemas de usabilidade, seguindo os padrões existentes.

Preservar DDD, Clean Architecture e Clean Code em todas as próximas etapas.

## Etapa atual

Etapa 2.4C.3 — Recuperação controlada de perfis e atribuições, autorizada pelo usuário.

Preservar a autenticação homologada. GET/listagem/detalhe/disponíveis e POST/PATCH
de proxies estão autorizados, com casos de uso, políticas e auditoria sanitizada.
A migration da Etapa 2.1 foi autorizada para homologação local após backup/revisão.
Frontend de proxies autorizado, consumindo somente a API existente. Preservar DDD,
Clean Architecture, autenticação por cookies e CSRF; nenhuma alteração de contrato.
Não gravar dados no banco homologado sem nova aprovação. Testes de escrita somente
com mocks ou bancos descartáveis; outras migrations homologadas exigem aprovação explícita.
Não iniciar integrações externas, importação de dados reais, commits ou deploys.

Pré-visualização POST /api/v1/proxies/import/preview autorizada, exclusiva Admin
com CSRF, limites de upload/linhas e resposta paginada/mascarada. Reutilizar regras
de domínio. Confirmação POST /api/v1/proxies/import/commit autorizada: reenvio do
CSV com prova protegida, revalidação, região explícita, idempotência, transação,
AES-256-GCM e auditoria sanitizada; nunca sobrescrever proxies existentes.
Não persistir CSV original ou credenciais em claro. Frontend de prévia/confirmação
autorizado, exclusivo Admin e consumindo endpoints existentes, com arquivo/prova
somente em memória e confirmação explícita. Preservar layout e autenticação/CSRF.
Sem integrações, mudanças cosméticas, migrations ou escritas homologadas.
Testes de escrita somente em bancos descartáveis e com dados sintéticos.
Consulte docs/decisions/0006-proxy-csv-preview.md e docs/etapa-2.3a-report.md.
Consulte também docs/decisions/0007-controlled-csv-import.md e docs/etapa-2.3b-report.md.
A migration 2.3B já foi aplicada e homologada; não reaplicá-la. Consulte também
docs/etapa-2.3b-homologacao-report.md e docs/etapa-2.3c-report.md.
A Etapa 2.3C exige avaliação manual do usuário antes de ser considerada homologada.
Não avançar automaticamente para outras etapas.

Compatibilidade CSV aprovada: datas sem ano permanecem rejeitadas; ano deve estar
em cada data no CSV. Coluna duplicada reconhecida inteiramente vazia pode ser
ignorada somente sem ambiguidade. Ignorar linhas sem host/porta/usuário/senha exige
opção explícita, vinculada à prova, com aviso e resultado auditado. Não ignorar
linhas parcialmente preenchidas. Nenhuma migration/escrita homologada autorizada.

Etapa 2.3C.1 autorizada: a decisão anterior sobre datas sem ano é substituída pela
opção explícita referenceYear, inicialmente ausente. Aplicar somente a dd/MM;
preservar datas completas, validar calendário e vencimento no backend e vincular
o ano à prova/idempotência. Sem opção, dd/MM continua inválido. Preservar região
complementar e opção de ignorar linhas sem conexão. Sem migrations ou escritas
homologadas; validação manual do usuário ainda necessária.
Resultados e limitações em docs/etapa-2.3c.1-report.md.

Etapa 2.4A: implementar somente domínio e testes unitários de perfis/alocações.
Uma alocação ativa por proxy e por perfil; preservar histórico; encerramento do
perfil finaliza o vínculo, sem mudar o status da proxy. Não implementar endpoints,
EF/migrations, frontend ou integrações. Nenhuma escrita homologada autorizada.
Decisões na ADR 0008 e resultados em docs/etapa-2.4a-report.md. Não avançar de etapa.

Etapa 2.4B.1 autoriza contratos Application, mapeamentos/repositórios EF,
transações, índices únicos parciais, migration gerada/revisada e testes descartáveis.
Não aplicar a migration na homologação, modificar proxies existentes, criar endpoints,
alterar frontend ou chaves. Consulte ADR 0009 e docs/etapa-2.4b1-report.md.
Não iniciar Etapa 2.4B.2 automaticamente; sem commits/deploys/dados reais.

Etapa 2.4B.2 autoriza Application e endpoints, auditoria atômica e disponibilidade
considerando vínculos ativos. Admin/Manager escrevem; Operator consulta. Preservar
cookies/CSRF/AES/CSV. Auditoria adicional gerada/revisada, testada em bancos descartáveis.
Sem migrations/escritas homologadas, frontend, integrações, commits ou deploys.
Não reiniciar API homologada com schema pendente. Consulte docs/etapa-2.4b2-report.md
e docs/api/operational-profiles-allocations.md. Aguardar revisão; não avançar de etapa.

Etapa 2.4B.3 autoriza TTL configurável (15 minutos), recuperação transacional sob
demanda, expiração auditada, confirmação idempotente e falhas de auditoria HTTP 500
com rollback. GETs permanecem sem escrita; índices/histórico e AES/DPAPI preservados.
Sem background desnecessário, frontend, integrações ou escritas/migrations homologadas.
Testes sintéticos em bancos descartáveis. Aguardar revisão antes da homologação.
Consulte docs/etapa-2.4b3-report.md e ADR 0010. Sem commits/deploys/próxima etapa.

Etapa 2.4B homologada localmente; as duas migrations já foram aplicadas, não repetir.
Etapa 2.4C autoriza interface React de perfis e alocações, exclusivamente usando
contratos existentes, layout/componentes/cliente HTTP/cookies/CSRF compartilhados.
Admin/Manager cadastram e gerenciam alocações; Operator consulta. Alocação atual
não deve ser inferida da página parcial do histórico. Disponibilidade/prazo/exclusividade
continuam sob autoridade da API. Liberação exige confirmação explícita.
Escritas de teste somente em mocks/bancos descartáveis; nenhuma escrita operacional
homologada, alteração de chave, integração, commit ou deploy. Aguardar homologação
manual do usuário. Consulte docs/etapa-2.4c-report.md e docs/ui/operational-profiles.md.
Não iniciar próxima etapa automaticamente.

Etapa 2.4C.1 autoriza duas atribuições opcionais do catálogo em OperationalProfile,
contratos de perfis compatíveis com platform legado e consulta específica paginada
por proxy, com filtros combinados e disponibilidade calculada no backend.
Migration somente gerada/revisada; NÃO aplicar na homologação ou iniciar a nova API
contra schema pendente. Preservar as 131 proxies, histórico, Identity, AES/DPAPI/keyring.
Testes de escrita exclusivamente descartáveis; sem recuperação CSV, frontend,
integrações, commits/deploys. Aguardar aprovação para aplicar migration.
Consulte docs/etapa-2.4c1-report.md e docs/api/operational-grid.md.

Homologação 2.4C.1 autorizada e concluída localmente em 12/10/2026:
20261012102737_AddProfileAssignments já aplicada e validada; NÃO repetir.
131 proxies, dados anteriores e chaves preservados, sem perfis/alocações homologados.
API reiniciada e smoke readonly/descartável aprovado. Consulte
docs/etapa-2.4c1-homologacao-report.md. Aguardar revisão do usuário; frontend
unificado dependia de autorização posterior. Sem commits/deploys/integrações.

Etapa 2.4C.2 autorizada posteriormente: /proxies é o grid operacional único,
consumindo operational-grid, com filtros/paginação no servidor e disponibilidade
exclusivamente da API. Perfis e CSV permanecem rotas secundárias; preservar ações,
cookies/CSRF, histórico e permissões. Sem backend/migrations/escritas homologadas,
integrações, commits/deploys. Testes de escrita somente mocks/descartáveis.
Consulte docs/etapa-2.4c2-report.md. Aguardar homologação manual; não iniciar 2.4C.3.

Etapa 2.4C.3 autorizada explicitamente em seguida: prévia/commit Admin de recuperação
de perfis a partir de CSV reenviado, sem recriar/alterar proxies ou credenciais.
Correspondência somente host canônico + porta com exatamente um resultado; nomes
repetidos/ambíguos, vínculos conflitantes e proxies indisponíveis bloqueiam o lote.
PLATAFORMA duplicada até duas ocorrências corresponde aos slots por posição;
valores vazios não são preenchidos. Catálogo e domínio existentes são autoridade.
Prova vincula arquivo/opções/plano; revalidar sob transação, idempotência e auditoria.
Associação recuperada é atual, sem inventar datas históricas. Sem frontend novo.
Migration AddProfileRecoveryReceipts somente gerada/revisada e testada em bancos
descartáveis: não aplicar nem reiniciar nova API contra schema pendente.
Nenhuma recuperação real, CSV real, escrita homologada, mudança de chave, integração,
commit ou deploy autorizado. Consulte docs/etapa-2.4c3-report.md e ADR 0012.
Aguardar autorização explícita antes de homologar migration ou executar recuperação.

Homologação 2.4C.3 retomada e migration 20261012131756_AddProfileRecoveryReceipts
JÁ APLICADA localmente. Não reaplicar. Estruturas/16 tabelas anteriores/131 proxies
e chaves preservadas; API nova em execução. 416 testes .NET aprovados.
Smoke auxiliar autenticado interrompido por CS0119 (File() sombreia System.IO.File).
Não retomar automaticamente: aguardar revisão. Homologação funcional integral
pendente, recuperação real continua sem autorização. Consulte
docs/etapa-2.4c3-homologacao-report.md. Sem integrações/commits/deploys.

Retomada final autorizada e concluída: conflito do harness corrigido com
System.IO.File explícito. Smoke HTTPS/Admin/CSRF/rejeições, 416 testes e integridade
aprovados; homologação técnica 2.4C.3 concluída, aguardando revisão. Pronta para
prévia via /api/v1/profiles/recovery/preview; original ainda não enviado. Recuperação
real/commit sem autorização. Não repetir migration ou regenerar chaves.

Recuperação 2.4C.3 — cópia filtrada preparada em 12/10/2026: linhas 22–23 (Perfil 1218,
pendência operacional, sem vínculo escolhido) e 131 (conexão vazia) excluídas somente
da cópia Downloads\HTTP - FACEBOOK UNIFICADA - recuperacao-129.csv (SHA-256 30DF1F96…).
Prévia read-only: 129 propostas, 0 conflitos/inválidos/ignorados, 129 pareamentos
idênticos ao original. INFOPRODUTO ainda pendente: aplicar migration, reiniciar API e
confirmar exigem autorização explícita. Consulte docs/etapa-2.4c3-filtered-preview-report.md.
Recuperação 2.4C.3 CONCLUÍDA em 12/10/2026 (autorizada): migration INFOPRODUTO aplicada
(checks corrigidos após incidente de encoding), API reiniciada, recoveryId ce98b7ed…:
129 perfis + 129 alocações Confirmed, 131 proxies inalteradas, Perfil 1218 pendente.
Não repetir migration nem commit. Ver docs/etapa-2.4c3-filtered-preview-report.md.

Ajuste 12/10/2026: a aba ativa passou a se chamar apenas "HTTP - FACEBOOK" (o sufixo
UNIFICADA existia por causa de abas antigas de Facebook). Rota /proxies inalterada.
Abas futuras no menu (Em breve): HTTP - TIKTOK,
HTTP - GOOGLE ADS, NUMEROS - OPERAÇAO, NUMEROS - FACEBOOK; estrutura de dados a definir.
"HTTP - FACEBOOK (ATUAL PROXIES)" e a antiga "HTTP - FACEBOOK UNIFICADA" sao a mesma aba: unica entrada HTTP - FACEBOOK.

Etapa 2.7 — Modo escuro e novo login (12/10/2026, frontend apenas): tema claro/escuro
por variáveis de cor em src/styles.css (:root.dark), segue o sistema e guarda a escolha
no navegador (central-theme); botão no cabeçalho e no login. Login em tela dividida com
ilustração decorativa (sem dados reais). Cores que mudam de papel usam tokens
(bg-surface, text-link, bg-ink); não usar bg-white/text-sky-800 em telas novas.

Etapa 2.8 — Dashboard AdsPower (12/10/2026, leitura apenas): autorizada a leitura da API local
do AdsPower (127.0.0.1:50325) para o Dashboard (/dashboard): GET /api/v1/adspower/dashboard e
POST .../refresh. Sem migration e sem persistir perfis; resultado em memória. username/password/
remark/IP da proxy do AdsPower são descartados na leitura. Chave via scripts/Save-LocalAdsPowerKey.ps1
(DPAPI em .local/secrets) e scripts/Set-LocalAdsPower.ps1; nunca em arquivos do repositório; a chave
foi exposta em conversas e deve ser trocada. Escopo: perfis por grupo e tag, busca/ordenação no navegador, data de criação do perfil; sem ativo/inativo; criador não existe na API.
Sem escrita no AdsPower. Ver docs/api/adspower-dashboard.md.

Etapa 2.5 — Ingestão de perfis pelo n8n (12/10/2026): API /api/v1/integration/profile-ingestions
(availability, reserve, confirm, release) com chave de serviço (hash na configuração). Migration
20261012230807_AddProfileIngestion JÁ APLICADA localmente (10 migrations); não reaplicar. Senha de proxy
é devolvida somente na reserva, só das proxies do lote (decisão do usuário). Entrega parcial com motivo
NoProxyAvailable. API escuta em 0.0.0.0:7090 para o Docker do n8n. Ver docs/etapa-2.5-ingestion-report.md,
docs/api/profile-ingestion.md e docs/n8n/README.md. Sem Google Sheets no fluxo novo.

Revisão 13/10/2026 da ingestão: numeração e criação só no grupo do AdsPower do modo ativo (Test = "FB Teste | YAGO", Production = "03 ESTEIRA DE PERFIL"),
próximo = maior "Perfil N" DO GRUPO + 1 (lido no AdsPower), sem olhar outros grupos nem perfis da Central; nomes repetidos entre grupos são aceitos. Migration
20261013105059_AddIngestionBatchGroup JÁ APLICADA localmente (11 migrations); não reaplicar. Padrão é Test; produção exige ProfileIngestion:Mode=Production.


Etapa 2.9 — Envio de TXT pelo site (13/10/2026): tela /proxies/ingestion (Admin/Manager) envia TXT à Central (POST /api/v1/profile-ingestions/submit, GET .../status) que repassa só pela memória ao webhook do n8n (fluxo "P6 - 02 - INGESTAO DE PERFIL (SITE)", X-Central-Key, N8nIngestion:WebhookUrl/WebhookKey via scripts/New-LocalN8nWebhookKey.ps1 e Set-LocalN8nWebhook.ps1). Arquivos nunca gravados nem logados; um envio por vez; relatório filtrado por lista de campos. Liberação do lote inteiro: POST /api/v1/integration/profile-ingestions/batches/{id}/release. Sem migration. Chave do AdsPower: o usuário trocará ao final da validação. Ver docs/api/profile-upload.md e docs/n8n/README.md.
