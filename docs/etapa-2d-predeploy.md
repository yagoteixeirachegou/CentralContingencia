# Etapa 2D — Fase 1: preparação para o primeiro staging no Railway (sem deploy)

Data: 21/10/2026. **Nada foi criado no Railway, nenhum deploy foi feito, nenhuma migration foi aplicada em banco existente.** Tudo o que está abaixo foi verificado localmente (imagem Docker real, PostgreSQL 17 descartável) ou pesquisado na documentação oficial; o que depende do comportamento real do Railway está marcado como **não verificado**.
Leitura prévia: `etapa-2c1-report.md`, `RAILWAY_DEPLOYMENT.md`, `HTTPS_HOMOLOGATION.md`, ADR 0019, `CLAUDE.md`.

## 0. Resumo executivo
- **Pronto para a Fase 2** do ponto de vista de código, imagem, migrations, Data Protection, rollback e testes — **com 3 bloqueios humanos** (seção 10): decisão sobre a confiança do proxy do Railway, repositório Git (hoje não existe) e conta/plano/domínio do Railway.
- **Achado mais importante:** a faixa de IP do proxy do Railway **não pode ser configurada com evidência**. A documentação oficial não lista faixas de origem; relatos de suporte na comunidade dizem que não há faixas documentadas nem estáveis e que o tratamento de `X-Forwarded-For`/`X-Real-IP` é inconsistente. Implementei uma **sonda de rede** (seção 5) para medir isso em staging antes de confiar em qualquer cabeçalho. Enquanto não se medir, a API falha de forma segura (recusa HTTPS declarado por origem não confiável).
- **Achados corrigidos nesta fase:** (1) o health check do Railway usa o host `healthcheck.railway.app`, que precisa constar em `AllowedHosts` (senão HTTP 400 e deploy reprovado); (2) o health check não pode ser redirecionado para HTTPS (307 reprovaria o deploy) — `/health*` agora é isento do redirecionamento; (3) o `ENTRYPOINT` com `sh -c` era frágil porque o start/pré-deploy command do Railway **substitui o ENTRYPOINT em forma exec e não expande variáveis** — a API agora lê `PORT` sozinha e o `ENTRYPOINT` é `["dotnet","CentralContingencia.Api.dll"]`; (4) faltava um jeito controlado de aplicar migrations dentro do ambiente do Railway — criei `--migrate` com plano, confirmação do alvo e trava contra scripts destrutivos.

## 1. Configuração final proposta para o Railway
Um projeto **`central-contingencia-staging`** (ambiente `staging`), região a decidir, com **2 serviços** e **1 domínio**:
| Serviço | Origem | Observações |
|---|---|---|
| `postgres` | Template PostgreSQL do Railway (imagem com SSL) | Privado por padrão; **sem Public Access** salvo janela de manutenção; backups agendados (seção 8) |
| `central-api` | `Dockerfile` da raiz (API + frontend) | **1 réplica** (o freio de credenciais e o limitador contam por processo); domínio público gerado `*.up.railway.app`; rede privada para o Postgres |
Arquivo `railway.json` (config as code, já no repositório): builder `DOCKERFILE`, `healthcheckPath=/health/ready`, `healthcheckTimeout=300`, `restartPolicyType=ON_FAILURE` (5 tentativas). **Não** contém `preDeployCommand` de propósito (seção 4). O worker **não** roda no Railway.
`Dockerfile` (revisado): 3 estágios; usuário não-root; `ASPNETCORE_ENVIRONMENT=Production`; `ENTRYPOINT ["dotnet","CentralContingencia.Api.dll"]`; sem `global.json`; `.dockerignore` exclui `.env*`, `.local`, `*.pfx`, `*.key`, `*.dpapi`, `tests`, `docs`, `scripts`.

## 2. Revisão de inicialização, porta e health checks (verificado na imagem)
| Item | Evidência |
|---|---|
| Porta | `PORT` (injetada pelo Railway em tempo de execução) é lida pela API: log `Now listening on: http://[::]:9321` com `PORT=9321`. Usa `http://*:PORT` (dual-stack com fallback IPv4). Valor inválido impede a partida. `ASPNETCORE_URLS` explícita vence |
| Health check | `GET /health/ready` com `Host: healthcheck.railway.app`, HTTP puro, **sem** `X-Forwarded-Proto` → 200 `Ready` (após migrations). `/health` = liveness. Host fora da lista → 400 |
| Redirecionamento HTTPS | `/health*` isento; o resto atrás do proxy não redireciona porque `X-Forwarded-Proto: https` de peer confiável marca a requisição como HTTPS |
| Inicialização | Configuração insegura/ausente impede a partida (`ProductionConfiguration`); migrations nunca rodam sozinhas; recuperação de leases inicia com a API (15 s) |
| Doc Railway (oficial) | Health check só no deploy ("não monitora depois de ativo"); timeout padrão 300 s; deploy reprovado mantém o anterior; limite de 15 min por requisição e 32 KB de cabeçalhos; TLS 1.2/1.3 na borda; `X-Forwarded-Proto` sempre `https` |
**Não verificado:** como o Railway trata `PORT` com a imagem real; se o health check envia `X-Forwarded-Proto`; se a rede privada exige escuta IPv6 (documentação silencia; `http://*` cobre IPv4 e IPv6).

## 3. Variáveis de ambiente do serviço `central-api`
S = segredo (cofre/variável protegida do Railway; nunca no repositório). Valores aqui são apenas exemplos de formato.
| Variável | S | Valor / origem |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | S | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};SSL Mode=Prefer` (rede privada; referência de variável do serviço Postgres — Npgsql não lê `DATABASE_URL`) |
| `AllowedHosts` | | `<dominio-do-staging>;healthcheck.railway.app` (**obrigatório incluir o segundo**) |
| `ReverseProxy__Enabled` | | `true` |
| `ReverseProxy__KnownNetworks__0` (ou `KnownProxies__0`) | | **Pendente de decisão/medição** (seções 5 e 10) |
| `DataProtection__Store` | | `Database` |
| `DataProtection__CertificateBase64` / `__CertificatePassword` | S | PFX RSA 3072 gerado para este ambiente (≈3–4 KB em Base64) e senha |
| `ProxyEncryption__ActiveKeyId`, `ProxyEncryption__Keys__<id>` | S | Chave AES-256 **nova** de staging (nunca a de desenvolvimento) |
| `LocalIntegrations__Enabled` | | `false` (padrão fora de Development) |
| `Security__AuthFailureLimit`, `__AuthFailureWindowSeconds`, `__ExecutorPermitLimit` | | padrões 10 / 60 / 600 |
| `WorkQueue__RecoveryIntervalSeconds` | | `15` (padrão); `WorkQueue__LeaseSeconds` 60 |
| `Diagnostics__NetworkProbeToken` | S | **Temporária**: 24+ caracteres aleatórios, só durante a medição da seção 5; remover depois |
| `CENTRAL_MIGRATE_CONFIRM` | | só durante um deploy com migrations (seção 4) |
| `CENTRAL_ADMIN_EMAIL` / `CENTRAL_ADMIN_PASSWORD` | S | só durante o `--provision-admin` inicial; remover em seguida |
Não definir: `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, `ASPNETCORE_URLS` (a API usa `PORT`), `ConnectionStrings__*` no worker.

## 4. Estratégia de migrations
Ordem: `AddDataProtectionKeys → AddWorkQueue → AddExecutors → AddClaimKey` sobre o esquema inicial (16 no total em banco novo). Todas aditivas.
- **Comando controlado** na imagem (verificado): `dotnet CentralContingencia.Api.dll --migrate [--plan]`. `--plan` só lista (não escreve). Aplicar **exige** `CENTRAL_MIGRATE_CONFIRM=<id da última migration da imagem>` (ou seja, alguém leu o plano e nomeou o alvo); script com `DROP/ALTER COLUMN/TRUNCATE/DELETE/UPDATE` é recusado sem `CENTRAL_MIGRATE_ALLOW_DESTRUCTIVE=YES` (em banco novo a trava não se aplica); saída só com ids de migration (verificado: a senha do banco não aparece). Código de saída 0 = ok/nada a fazer; 1 = recusa/falha.
- **Verificado com a imagem real em banco novo:** plano (16 pendentes) → aplicar sem confirmação recusado (nada criado) → aplicar com confirmação (16 aplicadas) → segunda execução "Nada a aplicar". Banco com 12 migrations + dados → 16 sem perda.
- **Como executar no Railway (a validar na Fase 2):** *Pre-deploy command* do serviço (executa em contêiner separado, na rede privada, com as variáveis do serviço; falha impede o deploy — documentação oficial) = `dotnet CentralContingencia.Api.dll --migrate`, com `CENTRAL_MIGRATE_CONFIRM` definida **somente** quando o plano foi revisado. Configurar na UI, não no `railway.json`, para que cada migração seja um ato deliberado. A documentação não diz se o pré-deploy substitui o `ENTRYPOINT` de uma imagem Dockerfile: o `ENTRYPOINT` em forma exec foi escolhido para funcionar nos dois casos (se os argumentos forem anexados, `--migrate` chega à API; se o comando substituir, o comando completo roda). Se falhar, o deploy simplesmente não avança.
- **Plano B manual:** rodar a mesma imagem localmente (`docker run … --migrate`) contra o Postgres do Railway exposto temporariamente por *Public Access* (TCP proxy, cobra egress), depois remover o acesso público.
- Primeiro Admin: `dotnet CentralContingencia.Api.dll --provision-admin` (mesmos meios; variáveis temporárias).

## 5. Configuração de proxy confiável (o ponto crítico)
**Evidência obtida**
- Documentação oficial (specs-and-limits): `X-Real-IP` identifica o IP do cliente; `X-Forwarded-Proto` é sempre `https`; **`X-Forwarded-For` não é descrito; faixas de IP de origem não são descritas**.
- Comunidade/suporte (não consegui abrir as discussões; só resumos de busca — **evidência fraca**): respostas conflitantes sobre se vale a entrada mais à esquerda ou à direita do `X-Forwarded-For`; relatos de que `X-Real-IP` pode trazer IP de CDN (Fastly); um funcionário teria dito que **não há faixas documentadas para os proxies** e que `100.64.0.0/10` **não é estável**.
**Consequência:** não vou fixar faixa de IP nem confiar em `X-Real-IP`. A configuração atual (`ForwardLimit=1`, só `X-Forwarded-For` e `-Proto`, só de origem listada) é a mais conservadora possível: um X-Forwarded-For forjado à esquerda é ignorado (verificado: `6.6.6.6, 203.0.113.9` → IP `203.0.113.9`).
**Medição proposta (Fase 2, antes de ligar a confiança):** com `Diagnostics__NetworkProbeToken` definido e `ReverseProxy` com uma faixa propositalmente irrelevante, `curl -H "X-Probe-Token: …" https://<dominio>/diagnostics/network` (a rota fica fora de `/api/v1`, não exige login e responde 404 sem o token) mostra `connectionRemoteIp` (o peer real que o contêiner enxerga), `host`, `scheme` e os cabeçalhos de proxy recebidos. Repetir de dois IPs diferentes e com `X-Forwarded-For` forjado pelo cliente para ver se a borda acrescenta, substitui ou preserva. Verificado localmente com a imagem: com faixa errada a sonda revela o peer (`::ffff:172.17.0.1`), `isHttps=false` e a API recusa `/api/v1` (400) — falha segura.
**Opções de decisão (humana):**
1. **A — faixa medida (recomendada para começar):** configurar `ReverseProxy__KnownNetworks__0` com a menor rede que contém os peers medidos (ex. um /16 do range observado). Risco: se o Railway mudar de faixa, a API passa a recusar `/api/v1` (400) — falha visível e segura.
2. **B — confiar no peer imediato (requer novo código e aprovação):** só HTTPS/IP via cabeçalhos quando o contêiner não for alcançável diretamente. Depende de isolamento de rede do Railway que **não** está documentado; não implementado.
3. **C — não confiar em IP de cliente:** manter `X-Forwarded-Proto` mas ignorar `X-Forwarded-For` (limites passam a contar por IP do proxy = um balde global; ataque de negação de serviço fácil). Também exigiria código novo; não implementado.
Os testes de 11 tentativas inválidas e de IP real (`HTTPS_HOMOLOGATION.md`, seção 4) devem passar na Fase 2 para dar a configuração por aprovada.

## 6. Persistência do Data Protection
Anel de chaves em `security."DataProtectionKeys"`, cifrado pelo certificado do ambiente. **Verificado com a imagem real:** sessão emitida pelo contêiner A válida no contêiner B e depois no C (mesmo banco e certificado; anel permaneceu com 1 chave); cookie adulterado → 401; restauração do banco em outro banco + novo contêiner → **sessão antiga continua válida** (o anel foi restaurado junto). Rotação de certificado: `PreviousCertificate*`. Perder o certificado só invalida sessões/CSRF/provas de importação, não dados de negócio. O certificado precisa ser guardado fora do Railway também (cofre pessoal), pois sem ele o anel restaurado não é legível.

## 7. Checklist de segurança (staging)
- [x] Segredos fora do repositório: varredura de chaves privadas, credenciais `ce1.`, `Password=` e `Bearer` literais → nada encontrado em arquivos versionáveis. `.env` (apenas as 4 variáveis do Postgres local) e `.local/` (backups de banco e segredos DPAPI **reais** do desenvolvimento) estão fora do Git por `.gitignore`/`.dockerignore`.
- [x] `.gitignore` reforçado: `*.dump`, `*.dpapi`, `*.p12`, `.railway/` (já cobria `.env`, `.local/`, `*.pfx`, `*.key`).
- [ ] **O diretório não é um repositório Git.** `.local/` contém 9 dumps/backups de banco e arquivos `.dpapi`. Qualquer upload do diretório inteiro (`railway up` sem Git, zip) arriscaria publicá-los. Exigir `git init` + GitHub **privado** e deploy a partir do repositório (bloqueio 2).
- [x] Cookies `__Host-` Secure/HttpOnly/SameSite=Strict/Path=/ sem `Domain`; HSTS presente em produção; CSRF obrigatório no login (sem token → 400) — testado atrás de proxy simulado com host público.
- [x] Host filtering ativo (`AllowedHosts` sem `*`); host desconhecido → 400.
- [x] Freio de credenciais inválidas e cota do executor atrás do proxy confiável (11ª tentativa → 429; outro cliente livre) — **verificado com a imagem**.
- [x] Nenhuma exposição pública do Postgres; sem `LocalIntegrations`; TLS nunca desabilitado.
- [x] Sonda de rede: desligada sem token; 404 com token errado; sem cookie/Authorization no retorno.
- [ ] Antes do deploy: gerar PFX e chave AES novos; senha forte do Postgres gerada pelo Railway; remover `Diagnostics__NetworkProbeToken` e `CENTRAL_ADMIN_*` após uso.
- [ ] Revogar o executor de teste ao final; trocar a chave do AdsPower exposta antes de qualquer uso (fora desta etapa).

## 8. Backup, restauração e plano de rollback
**Backup.** Railway: backups de volume (diário 6 dias, semanal 27, mensal 89; manual a qualquer momento; incrementais). Restauração cria **novo volume** com o carimbo do backup, mantém o original desmontado e exige "Deploy" na tela; só no mesmo projeto/ambiente; a própria documentação diz que o recurso está **"em desenvolvimento"** e não informa requisito de plano nem downtime. **Não verificado:** disponibilidade no plano que será usado. Complemento independente: `pg_dump -Fc` antes de cada migração (via contêiner/psql na rede privada ou Public Access temporário).
**Ensaio local (verificado):** banco com 12 migrations + dados → `pg_dump -Fc` → `--migrate` (16 migrations) → `pg_restore` em banco novo → 12 migrations, dados presentes, sem o schema `work`, e `--migrate --plan` acusa as 4 pendentes. Restauração do banco de staging completo → API sobe `Ready` e a sessão antiga continua válida.
**Rollback de aplicação:** *Rollback/Redeploy* do deployment anterior no Railway (imagem anterior). As migrations são **aditivas e anuláveis** (tabelas/colunas novas), então a versão N-1 funciona com o esquema N; a regra de projeto passa a ser *expand/contract*: nunca remover/renomear/tornar obrigatório na mesma versão em que o código deixa de usar.
**Rollback de dados:** restaurar o backup anterior à migração (novo volume/banco) e redeployar a versão anterior; não usar `Down` de migration. Perde-se o que foi escrito depois do backup — em staging é aceitável.
**Falha no pré-deploy/health check:** o Railway não ativa o novo deployment e mantém o anterior; corrigir e refazer. Falha no meio de `--migrate`: restaurar o backup (migrations rodam em transação por migration no PostgreSQL; ainda assim, backup antes).

## 9. Roteiro de homologação (Fase 2, resumido)
1. Git + repositório privado; projeto Railway; Postgres novo; backup manual inicial.
2. Variáveis (seção 3) **sem** `ReverseProxy__Enabled`/KnownNetworks e com `Diagnostics__NetworkProbeToken`; deploy; **medir** com a sonda (seção 5); decidir A/B/C; configurar `ReverseProxy`; remover o token.
3. Pré-deploy `--migrate --plan` (revisar) → `--migrate` com `CENTRAL_MIGRATE_CONFIRM`; `/health/ready` = Ready; `--provision-admin`.
4. Testes pelo domínio público: login/CSRF/cookies no navegador; 11 credenciais inválidas → 429 e outro IP livre; `/health` do Railway aprovado.
5. Executor de teste (tela Executores) → worker Windows por HTTPS com certificado público (sem `TrustedCaFile`), sem string de banco; roteiro da seção 3 de `HTTPS_HOMOLOGATION.md` (sucesso, retry, dead-letter, cancelamento, kill -9 com e sem lease vencido, revogação, perda de rede).
6. Reiniciar/redeployar a API e provar que sessão e leases se recuperam; ensaio de restauração de backup.
7. Registrar tudo em `docs/etapa-2d-report.md` (a criar na Fase 2); **só então** declarar o staging homologado.

## 10. Bloqueios que exigem decisão humana
1. **Confiança do proxy (seção 5):** autorizar a medição com a sonda e escolher A, B ou C (B e C exigem código novo e sua aprovação explícita).
2. **Repositório Git:** hoje não existe. Autorizar `git init`, revisão do `.gitignore` e criação de repositório **privado** (onde? GitHub/organização). Sem isso o deploy por código é arriscado (`.local/` com dumps e segredos reais).
3. **Conta/plano/domínio do Railway:** quem cria o projeto (token/`railway login` interativo é seu), plano (backups, egress, TCP proxy), região, e se usará o domínio `*.up.railway.app` ou um domínio próprio.
4. **Migrations no Railway:** aceitar o pré-deploy `--migrate` com variável de confirmação (recomendado) ou preferir o plano B manual.
5. **Custódia dos segredos de staging** (PFX do Data Protection, chave AES, senha do Admin inicial): onde guardar a cópia fora do Railway.
6. **Backups:** confirmar o plano que oferece backup de volume (a documentação não diz) e a janela de restauração aceitável; autorizar `pg_dump` manual antes de cada migração.
7. **Worker Windows de teste:** qual máquina (esta ou outra) e quem insere a credencial do executor (DPAPI).
8. Confirmar que o staging usará **apenas** `SimulatedProcessing`, sem dados reais (premissa desta etapa).

## 11. Verificações desta fase
| Verificação | Resultado |
|---|---|
| `dotnet build` | 0 erros (avisos: NU1510 conhecido e avisos de estilo xUnit em testes de etapas anteriores) |
| Testes unitários | **534/534** (eram 509; +25: porta da plataforma, detecção de SQL destrutivo) |
| Testes de integração | **401/401** (eram 390; +11: comando de migrations, simulação de domínio público atrás de proxy, sonda de rede) |
| Imagem Docker real | build OK; `--migrate --plan`/aplicar/idempotência; admin; `PORT`; health check estilo Railway; host público via proxy; login/cookies/HSTS; troca de contêiner; adulteração de cookie; freio de credenciais atrás do proxy; restauração |
| Não executado | Qualquer coisa no Railway real; `railway` CLI não está instalado; teste com worker em outra máquina; carga |
