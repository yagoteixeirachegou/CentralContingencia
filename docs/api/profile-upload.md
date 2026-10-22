# Envio de TXT pelo site (ingestão de perfis)

Tela `/proxies/ingestion` (botão "Novos perfis (TXT)" no grid). Papéis: **Admin** e **Manager**. Cookie + CSRF como o resto da API humana.

## Fluxo

Navegador → `POST /api/v1/profile-ingestions/submit` (multipart, campo `files`) → Central (memória) → webhook do n8n
(`N8nIngestion:WebhookUrl`, cabeçalho `X-Central-Key`) → reserva de proxies, criação no AdsPower, confirmação → relatório JSON → Central
filtra os campos conhecidos → navegador.

## Endpoints

### `GET /api/v1/profile-ingestions/status`
`{ configured, inProgress, availableProxies, nextProfileNumber, nextProfileName, group{id,name,mode}, maxFiles, maxFileBytes }`.
Sem credenciais. 503 se o AdsPower não responder (a numeração depende dele).

### `POST /api/v1/profile-ingestions/submit`
Somente `.txt` (nome até 150 caracteres, sem repetição, sem arquivo vazio), até `MaxFiles` (20) de `MaxFileBytes` (512 KB).
Respostas: 200 relatório; 400 arquivos inválidos/CSRF; 409 já há envio em andamento; 413 limites; 415; 429;
502 n8n recusou/respondeu algo inesperado; 503 n8n fora do ar ou não configurado; 504 n8n demorou (`TimeoutSeconds`, 300).
Sem reenvio automático: em 504, conferir o AdsPower e o grid antes de reenviar (reservas não usadas voltam em 15 minutos).

Relatório (campos fixos, textos limitados): `title, message, groupName, environment, failed, summary{requested,reserved,created,adsPowerFailures,unfilledForLackOfProxy,filesForReview}, created[], failures[], unfilled[], review[], reservationsUndone`.

## Segurança

- Os TXT têm login, senha, 2FA e cookies. Ficam só em memória (buffers zerados ao final); nada em disco, banco, log ou storage do navegador.
- A "auditoria" é uma linha de log estruturada sem conteúdo: ator, nº de arquivos, bytes, contagens do resultado e correlação. Não há tabela nova (sem migration).
- `WebhookUrl` só aceita localhost, 127.0.0.1 ou host.docker.internal; a chave vem de variável de ambiente (DPAPI em `.local/secrets`).
- O fluxo do site no n8n desliga o armazenamento de execuções (sucesso e erro) para que as credenciais não fiquem no histórico do n8n. Para depurar, ligue temporariamente e apague depois.
- A senha da proxy continua só no n8n (resposta da reserva); nunca chega ao navegador.
