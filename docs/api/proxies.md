# API de proxies — Etapa 2.2A

Base local: https://localhost:7090/api/v1/proxies. Sem frontend de proxies.
Pré-requisitos: migrations de persistência e auditoria aplicadas com aprovação;
chave de proxies configurada para criação/troca de senha. Leia ADR 0004 e
docs/security/proxy-encryption.md. Não reprovisionar o Admin existente.

| Método/rota | Papéis | Sucesso |
| --- | --- | --- |
| GET /proxies | Admin, Manager, Operator | 200 ProxyPage |
| GET /proxies/{UUID} | Admin, Manager, Operator | 200 ProxyResponse |
| GET /proxies/available | Admin, Manager, Operator | 200 ProxyPage |
| POST /proxies | Admin, Manager | 201 ProxyResponse + Location |
| PATCH /proxies/{UUID} | Admin, Manager | 200 ProxyResponse |

Sessão Identity em cookie seguro; primeiro obtenha GET /api/v1/auth/csrf e envie
X-CSRF-TOKEN em POST/PATCH. Não enviar senha em query/URL. HTTPS obrigatório.
Não há DELETE, importação, integração ou revelação de senha.

## Entrada

POST: host (DNS ASCII/IP sem URL), port (1–65535), username (obrigatório, até 128),
password (obrigatória, 1–1024 bytes UTF-8), region (obrigatória, até 100),
expiresAt (obrigatório, futuro, ISO-8601 com Z/offset). status opcional,
padrão Disabled; aceita exatamente Active/Suspended/Disabled.

PATCH: um ou mais desses mesmos campos, todos opcionais. Ausentes preservam valor;
null, campos desconhecidos, nomes duplicados e objeto vazio são inválidos.
Senha somente é recifrada se password for enviado. Nova validade deve ser futura;
registro vencido pode mudar estado sem renovar sua validade.
Texto claro existe somente na entrada autorizada e no adapter durante a cifragem.
Não guardar requisições em logs, histórico de terminal, HAR ou arquivos versionados.

## Consulta

Parâmetros opcionais:

| Parâmetro | Regra/padrão |
| --- | --- |
| page | 1–10000, padrão 1 |
| pageSize | 1–100, padrão 20 |
| host | Correspondência exata após normalização DNS/IP |
| port | Correspondência exata, 1–65535 |
| region | Correspondência exata, inclusive capitalização |
| status | Active, Suspended ou Disabled |
| expiresBefore | Limite inclusivo de vencimento; envie ISO-8601 com offset URL encoded |
| sort | host, port, region, status, expiresAt, createdAt ou updatedAt; padrão createdAt |
| direction | asc ou desc; padrão desc |

Exemplo sem credenciais: GET /api/v1/proxies/available?pageSize=20&region=BR&sort=expiresAt&direction=asc.
UUID desempata a ordenação. available é Active, criado e ainda válido; alocações
não existem nesta etapa e conectividade não é verificada.

## Resposta e erros

ProxyPage: items, page, pageSize, total. ProxyResponse: id, host, port,
usernameMasked (***), status, region, expiresAt, createdAt, updatedAt, available.
Sem password, chaves ou envelope criptográfico. Timestamps em UTC.

Erros application/problem+json: 400 entrada/query/CSRF inválidos; 401 sessão ausente;
403 papel insuficiente; 404 UUID não encontrado; 409 conflito concorrente/único;
415 mídia incompatível; 429 excesso de chamadas; 503 criptografia indisponível.
UUID malformado na rota pode resultar em 404 por constraint de roteamento;
UUID vazio válido sintaticamente resulta em 400. Erros não ecoam credenciais.

Não repetir POST automaticamente após timeout/409: não há idempotency key nesta
etapa e repetições confirmadas criam registros diferentes. Depois de 409 em PATCH,
consultar o registro novamente antes de decidir repetir a alteração.
Contrato OpenAPI disponível em Development em /openapi/v1.json, com cookie de sessão,
CSRF inclusive PATCH, respostas tipadas e password writeOnly.

## POST /api/v1/proxies/{id}/credentials/reveal

Revela **usuário e senha de uma proxy** (ADR 0014). Somente `Admin`; exige cookie de sessão e `X-CSRF-TOKEN`; sem corpo.
Resposta `200` com `Cache-Control: no-store`: `{ "id", "host", "port", "username", "password" }`.
Erros: `401` sem sessão, `403` sem papel Admin, `404` proxy inexistente, `429` limite de tentativas, `503` criptografia indisponível.
Cada chamada bem-sucedida grava o evento de auditoria `Revealed` antes da resposta. Nenhum outro endpoint devolve a senha.
