# ADR 0015 — Aquecimento como verificação de impedimentos

Data: 16/10/2026. Status: aceita (usuário autorizou o escopo restrito).

## Contexto
Antes de entregar perfis a clientes para tráfego pago, a equipe verifica se há impedimentos
(CAPTCHA, checkpoint etc.). O gestor chama o processo de "aquecimento" na comunicação com o cliente.

## Decisão
- O nome "Aquecimento" permanece em menus e textos voltados a pessoas. Código, tabelas e rotas
  técnicas usam "HealthCheck"/"warmup" apenas onde já exposto na API.
- A operação é verificação somente leitura, uma por perfil por dia UTC, esteira de 7 dias.
  Sem sessão cronometrada nem interação. O agente nunca contorna nem repete diante de impedimento.
- Resultado gravado em profiles.HealthChecks (perfil do AdsPower, nome, grupo, dia da esteira, resultado,
  data/hora UTC, ator). Índices únicos (perfil+dia, perfil+data UTC) garantem a regra no PostgreSQL.
- Endpoints: POST /api/v1/warmup/checks (Admin/Manager, CSRF), POST /api/v1/warmup/progress e
  GET /api/v1/warmup/profiles/{id}/checks (leitura, Operator incluso).
- Não grava cookies, capturas de tela, conteúdo de página nem credenciais.

## Consequências
Migration aditiva gerada e não aplicada. Disparo em segundo plano e UI ficam para etapas autorizadas à parte.
O backend não valida o perfil contra o AdsPower (aceita identificadores informados por quem tem permissão).
