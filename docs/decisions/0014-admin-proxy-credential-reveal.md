# ADR 0014 — Revelação sob demanda das credenciais de proxy (Admin)

Data: 2026-10-14. Status: aceita pelo responsável do projeto.

## Contexto
A regra de segurança nº 4 proibia devolver a senha de proxy descriptografada em qualquer endpoint. Na operação, o Admin precisa
consultar usuário e senha ao abrir um perfil ou uma proxy (e copiar `host:porta:usuário:senha`) enquanto a planilha ainda existe.

## Decisão
- Novo endpoint `POST /api/v1/proxies/{id}/credentials/reveal`: somente `Admin`, CSRF obrigatório, rate limiting `sensitive`,
  `Cache-Control: no-store`, **uma proxy por chamada**. Devolve host, porta, usuário e senha.
- O serviço descriptografa em memória e **grava o evento de auditoria `Revealed` (ator, proxy, correlation ID, UTC) antes de devolver**.
  Falha de auditoria impede a resposta. O valor da senha nunca vai para auditoria nem logs; `ProxyCredentials.ToString()` não imprime dados.
- Listagens, detalhe (`GET /proxies/{id}`), grid operacional, exportações e MCP **continuam mascarados**. A regra nº 4 segue valendo
  para tudo, exceto este endpoint.
- Interface: `ProxyCredentialsPanel` mostra pontos; o olho consulta o endpoint; "Copiar tudo" copia `host:porta:usuário:senha`.
  O valor vive só em memória do componente e some ao ocultar, após 30 s, ao trocar de proxy ou ao fechar.
- Sem migration: a ação de auditoria é gravada como texto (`Revealed`, 8 caracteres, limite de coluna 16).
- A criação de perfis no AdsPower continua recebendo as credenciais das proxies reservadas pelo endpoint de ingestão (ADR 0013).

## Riscos aceitos
Qualquer Admin com sessão aberta pode ver a senha; o rastro fica na auditoria. Senha compartilhada entre proxies amplia o impacto de
uma exposição. Reavaliar (MFA administrativo, revelação por tempo limitado) antes de produção.
