# Fluxo de ingestão no n8n usando a Central

Arquivo: `P6 - 01 - INGESTAO DE PERFIL (API Central).json` (importar no n8n). Gerado a partir de `P6 - 01 - INGESTAO DE PERFIL`:
formulário, leitura do TXT, `FORMATA PERFIS` e as chamadas ao AdsPower ficam iguais. Saem todos os nós do Google Sheets
(`Get row(s) in sheet` ×3, `ANALISAR SLOTS`, `BUSCAR PROXY`, `VALIDAR SLOT ANTES`, `REVALIDAR LINHA`, `VALIDAR ESCRITA FINAL`, `If`,
`Update row in sheet`, `PULAR ESCRITA`).

```
Formulário → TXT → FORMATA PERFIS → CLASSIFICAR PERFIS → RESERVAR PROXIES (API)
   → MONTA LOTE PROXY → PREPARAR ADSPOWER → VERIFICAR ADSPOWER → VERIFICAR DUPLICATAS → Loop
        por perfil:  OBTER PAYLOAD → CRIAR PERFIL ADSPOWER → REGISTRAR CRIAÇÃO → CRIAÇÃO OK?
                       sim → CONFIRMAR NA API         não (ou erro de conexão) → PREPARAR LIBERAÇÃO → LIBERAR NA API
   fim do loop → RELATÓRIO FINAL → Resposta ao usuário
Falha em MONTA LOTE PROXY / PREPARAR ADSPOWER / VERIFICAR ADSPOWER / VERIFICAR DUPLICATAS (saída de erro)
   → PREPARAR LIBERAÇÃO DO LOTE → LIBERAR LOTE NA API → RELATÓRIO DE FALHA → Resposta ao usuário
```

Se o fluxo falhar depois de reservar e antes de criar perfis, `LIBERAR LOTE NA API` desfaz o lote inteiro
(`POST …/batches/{batchId}/release`): as proxies voltam a ficar livres e a numeração não tem buracos.

## Preparação (uma vez)

1. Gerar a chave: `.\scripts\New-LocalIntegrationKey.ps1` (copie a chave mostrada; só o hash fica guardado).
2. n8n → Credentials → *Header Auth*: Name `Authorization`, Value `Bearer <chave>`; nome da credencial: **Central de Contingência - chave do n8n**.
3. Importar o JSON e escolher essa credencial nos nós `RESERVAR PROXIES (API)`, `CONFIRMAR NA API`, `LIBERAR NA API` e `LIBERAR LOTE NA API`.
4. Reiniciar a API da Central carregando a chave e escutando para o Docker (`scripts/Set-LocalIntegration.ps1`;
   `ASPNETCORE_URLS=https://0.0.0.0:7090`). O endereço está em `CLASSIFICAR PERFIS` (`API_BASE`): `https://host.docker.internal:7090`.
   Os três nós HTTP já vêm com "Ignore SSL Issues" (certificado de desenvolvimento).
5. **Chave do AdsPower** (obrigatória quando a verificação de segurança do AdsPower está ligada; sem ela a API local responde `Require api-key`):
   crie uma segunda credencial *Header Auth* (Name `Authorization`, Value `Bearer <chave do AdsPower>`), com o nome
   **AdsPower - chave da API**, e selecione-a nos nós `VERIFICAR ADSPOWER` e `CRIAR PERFIL ADSPOWER`. Os endereços ficam como no original
   (`host.docker.internal:50325`).

## O que o usuário recebe

A página final do formulário mostra, por exemplo: *"Criados 3 de 5 perfil(is) válido(s). 2 não foi(ram) preenchido(s) por falta de
proxy disponível: d.txt, e.txt. 1 arquivo(s) precisam de revisão manual: f.txt."* O JSON completo da execução traz `criados`,
`falhas`, `naoPreenchidos` (arquivo, posição, motivo `NoProxyAvailable`) e `revisaoManual`.
Se a opção do formulário ("Respond When") não estiver em *Workflow Finishes*, ajuste manualmente; o relatório continua na execução.

## Grupo do AdsPower (teste e produção)

O fluxo **não tem grupo fixo**: a Central informa, em cada reserva, o grupo onde numerar e criar (`group.id`, `group.name`, `group.mode`), e os nós
de duplicatas, payload e criação usam esse valor. Em modo **Test** é `FB Teste | YAGO`; em **Production**, `03 ESTEIRA DE PERFIL`. A troca é feita na
Central (`ProfileIngestion:Mode`, padrão `Test`), nunca no n8n. O relatório final informa o grupo e o ambiente usados.

## Comportamento

- Perfis sem login/senha do Facebook vão para revisão e **não consomem número nem proxy**.
- Sem nenhuma proxy: a execução falha com a lista dos arquivos e o motivo.
- Falha do AdsPower em um perfil: a reserva dele é liberada (a proxy volta a ficar livre e o número é reaproveitado); os demais seguem.
- Se o n8n parar no meio, as reservas vencem sozinhas em 15 minutos.
- Repetir um nó HTTP com a mesma chave de idempotência é seguro; reexecutar o fluxo inteiro gera uma nova chave.
- Os TXT (logins, senhas, 2FA, cookies) nunca são enviados à Central: só a quantidade de perfis, o id e o número de série do AdsPower.
- A resposta da reserva contém a senha das proxies reservadas: no n8n, desative "Save successful executions" ou limite a retenção.

Teste sem n8n: `.local/n8n-build/smoke.cjs` roda os nós de código com dados sintéticos (não faz parte do produto).

## Fluxo acionado pelo site (P6 - 02 - INGESTAO DE PERFIL (SITE))

Mesmo miolo, mas a entrada é um **Webhook** (os TXT vêm da Central) e a saída é um JSON para a Central. Os arquivos não passam pelo formulário do n8n.

1. `scripts/New-LocalN8nWebhookKey.ps1` — gera a chave (mostrada uma vez) e a guarda protegida em `.local/secrets`.
2. n8n → Credentials → *Header Auth*: Name `X-Central-Key`, Value = a chave; nome **Central de Contingência - webhook do site**.
3. Importar `P6 - 02 - INGESTAO DE PERFIL (SITE).json`; escolher as credenciais nos nós (webhook, `RESERVAR PROXIES (API)`, `CONFIRMAR NA API`, `LIBERAR NA API`, `LIBERAR LOTE NA API`, AdsPower) e **ativar** o fluxo (URL de produção `/webhook/p6-ingestao-perfil`).
4. `scripts/Start-LocalApiFull.ps1` — sobe a API com banco, criptografia, AdsPower, chave do n8n e webhook (porta 7090).
5. Site: `https://localhost:5173` (`npm run dev` em src/central-contingencia-web) → HTTP - FACEBOOK → **Novos perfis (TXT)**.

O fluxo do site não guarda execuções no n8n (as credenciais dos TXT não ficam no histórico). O fluxo com formulário continua disponível.
