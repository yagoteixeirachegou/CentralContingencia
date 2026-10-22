# Etapa 2.5 — Ingestão de perfis pelo n8n (API da Central)

Data: 12/10/2026. Contrato: [docs/api/profile-ingestion.md](api/profile-ingestion.md). Decisão: [ADR 0013](decisions/0013-profile-ingestion-integration.md).
Fluxo n8n: [docs/n8n/README.md](n8n/README.md).

## Entregue

- **Domínio:** `ProfileNumber` (Perfil N), `ProfileIngestionBatch`, vínculo `AdsPowerId`/`AdsPowerSerialNumber` e marca `IngestionBatchId` no perfil.
- **Aplicação:** `ProfileIngestionService` (entrega parcial, senha só dos itens com reserva válida, status Complete/Partial/Empty).
- **Persistência:** `EfIngestionStore` (reserva em lote atômica, serializada com as demais alocações; ordem da planilha pela linha importada;
  numeração = maior "Perfil N" ativo ou já confirmado + 1; recuperação de reservas vencidas; confirmar/liberar com auditoria).
- **API:** `GET availability`, `POST reserve`, `POST {id}/confirm`, `POST {id}/release`, com chave de serviço (`IntegrationKey`, só hash na
  configuração, escopo `profile-ingestion`, sem acesso à API das pessoas, rate limit próprio, sem CSRF nesses endpoints).
- **Migration** `20261012230807_AddProfileIngestion` (aditiva; `Down` recusa apagar vínculos/lotes). SQL: `docs/security/etapa-2.5-profile-ingestion-migration.sql`.
- **n8n:** fluxo sem Google Sheets (`docs/n8n/…(API Central).json`), relatório final para o usuário; scripts `New-LocalIntegrationKey.ps1` e `Set-LocalIntegration.ps1`.

## Verificação

- .NET: **293 unitários + 204 de integração**, todos aprovados (15 de ingestão com PostgreSQL descartável por teste: exclusividade, parcial,
  ordem da planilha, idempotência, vencimento, concorrência, 404 entre credenciais, 400 em entradas inválidas, proteção do `Down`).
- Fluxo n8n: `.local/n8n-build/smoke.cjs` (cenário parcial com falha no AdsPower e arquivo para revisão).
- Homologação local: backup `.local/backups/etapa-2.5-ingestion-20261012T232202Z` (120.248 bytes, 18 tabelas, decodificação íntegra);
  migration aplicada (10 migrations); **131 proxies com o mesmo fingerprint**, perfis/alocações 130/130 preservados.
- Ponta a ponta na API real com chave temporária (descartada): pediu 5, 3 livres → Partial, 3 perfis (1598–1600) com conexão completa e 2 pendentes
  `NoProxyAvailable`; repetição idempotente; liberação devolveu 3 livres e próximo 1598. O container do n8n alcança `https://host.docker.internal:7090`.

## Revisão de 13/10/2026 — numeração por grupo do AdsPower

Pedido do usuário: numerar e criar **somente no grupo de ingestão**. Leitura real (somente consulta): `03 ESTEIRA DE PERFIL` (id 10477650) tem 22 perfis,
1202–1243, próximo **1244** (a Central dava 1598 por usar todos os perfis). Teste: `FB Teste | YAGO` (id 10906211), próximo **1251**. Implementado: leitura do grupo no
AdsPower na reserva (piso), modo `Test`/`Production` (`ProfileIngestion`), grupo gravado no lote, numeração da Central restrita ao mesmo grupo, falha segura sem AdsPower,
fluxo n8n sem grupo fixo. Migration `20261013105059_AddIngestionBatchGroup` aplicada localmente após backup `.local/backups/etapa-2.5-ingestion-group-20261013T105650Z`
(126.471 bytes, 19 tabelas); 11 migrations; 131 proxies com o mesmo fingerprint. Testes: **312 unitários + 211 de integração = 523**, todos aprovados.
Validação ao vivo: teste → Perfil 1251; produção (instância só de leitura) → Perfil 1244; reserva real em teste e liberação voltaram a 1251.
Colisões de nome com outros grupos são aceitas (ex.: Perfil 1250 existe em YAGO e em `GG | …`).

## Pendente (depende do usuário)

1. Gerar a chave: `.\scripts\New-LocalIntegrationKey.ps1`, criar a credencial no n8n e reiniciar a API carregando a chave.
2. Importar o fluxo no n8n e testar com 1–2 TXT sintéticos no grupo de testes do AdsPower.
3. Firewall do Windows: manter a porta 7090 restrita à rede privada (a API escuta em `0.0.0.0` para o Docker).
4. Trocar a chave do AdsPower exposta anteriormente.
