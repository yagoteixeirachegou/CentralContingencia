# API — Etapa 1

OpenAPI gerado em /openapi/v1.json somente em Development. Documento descreve
o cookie de sessão, header CSRF obrigatório nas mutações e payloads de requisição.
Sem Swagger UI. Todas as rotas devem ser acessadas via HTTPS.

| Método e rota | Acesso | Resultado |
| --- | --- | --- |
| GET /health | Público | 200 Healthy, verifica somente processo |
| GET /api/v1/auth/csrf | Público ou autenticado | 200 token CSRF da identidade atual |
| POST /api/v1/auth/login | Público com CSRF | 204 e cookie; 401 para credenciais/lockout |
| GET /api/v1/auth/me | Autenticado | 200 id/email/roles; 401 sem sessão válida |
| POST /api/v1/auth/logout | Autenticado com CSRF | 204, revoga todas as sessões |
| POST /api/v1/auth/change-password | Autenticado com CSRF | 204 e novo login obrigatório |
| POST /api/v1/users | Admin com CSRF | 201, cria conta interna |

Login: { email, password }. Alteração: { currentPassword, newPassword }.
Criação: { email, password, role }. Papéis: Admin, Manager e Operator.
Criação inválida/duplicada e troca inválida retornam 400. A API valida novamente
os campos e requisitos de senha; UI não é fonte de autorização.

Mutações exigem cookie antiforgery e X-CSRF-TOKEN compatíveis com identidade atual.
CSRF inválido retorna 400. Falta de sessão retorna 401, acesso negado 403 e limite
de requisições 429 com Retry-After. Erros são ProblemDetails, sem segredos.
Respostas /api/v1 são no-store. Não há registro público, JWT ou integrações.
Proxies possuem GET/listagem/detalhe/disponíveis e POST/PATCH, conforme
[contrato próprio](proxies.md), consumido pela interface da Etapa 2.2B sem alterações.
Requisições /api/v1 recebidas via HTTP são recusadas com 400 antes do processamento.

Etapa 2.3A: [POST /api/v1/proxies/import/preview](proxy-csv-preview.md), exclusivo
Admin com CSRF, recebe multipart em memória e retorna prévia paginada sem persistir.

Etapa 2.3B: [POST /api/v1/proxies/import/commit](proxy-csv-import.md), exclusivo
Admin com CSRF, prova protegida, revalidação, transação e idempotência.

Etapa 2.4C.1: [grid operacional e atribuições opcionais](operational-grid.md).
GET /api/v1/proxies/operational-grid; migration aplicada e validada localmente em
12/10/2026. Não reaplicar. [Relatório de homologação](../etapa-2.4c1-homologacao-report.md).

Etapa 2.4C.3: [recuperação controlada de perfis](profile-recovery.md).
POST /api/v1/profiles/recovery/preview e /commit. Admin/CSRF, revalidação de plano,
transação e idempotência; não recria proxies. Migration e recuperação homologadas
pendentes de autorização separada.
