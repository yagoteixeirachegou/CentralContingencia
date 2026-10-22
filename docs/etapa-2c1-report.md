# Etapa 2C.1 — Homologação HTTPS e segurança pré-deploy (relatório)

Data: 21/10/2026. Sem deploy, sem migration aplicada em banco existente, sem credenciais reais, sem Claude/AdsPower, **sem desabilitar TLS** em nenhum momento.
Decisões: ADR 0019. Procedimento reutilizável: `docs/HTTPS_HOMOLOGATION.md`. Leitura prévia: relatório 2C, ADR 0018, CLAUDE.md, RAILWAY_DEPLOYMENT.md.

## 0. Correção de um erro do relatório anterior
O relatório 2C afirmou que o certificado de desenvolvimento "não é confiável neste Windows". **Isso não estava verificado e era falso.** Verificação desta etapa: `dotnet dev-certs https --check --trust` informa certificado confiável
(`0E4C8EAC8D7F9674B14C29E1314882ED0896BBE2`, `CN=localhost`, válido até 18/05/2027), presente em `Cert:\CurrentUser\Root`; `curl https://localhost:7191/health` sem `-k` responde 200 (`ssl_verify=0`); e o worker autentica por HTTPS.
A causa real da falha de handshake da 2C não foi identificada (não reproduziu em 6 execuções); passei a registrar a causa técnica segura nos logs para diagnosticar casos futuros. O relatório 2C foi anotado com essa correção.
**Nenhuma alteração da confiança do sistema foi necessária nem feita**; por isso não houve pedido de autorização.

## 1. Alterações (resumo)
| # | Tarefa | O que mudou |
|---|---|---|
| 1 | Confiança TLS | Verificação somente leitura (acima). Novo `Worker:Api:TrustedCaFile`: confia numa CA privada **só no processo do worker**, mantendo cadeia, validade e nome do host (`TlsTrust`). Teste negativo real: sem a CA o handshake falha com `AuthenticationException` |
| 3 | Rate limit de credenciais inválidas | `AuthFailureThrottle` nos esquemas do executor e da chave do n8n; 429 + `Retry-After` sem consultar o banco; IP real só via proxy confiável (X-Forwarded-For de origem não confiável é ignorado); IPv6 por /64; memória limitada |
| 3b | **Defeito encontrado na homologação** | O limite global de 120 req/min **por IP** também valia para o worker autenticado: pessoas/scripts no mesmo IP faziam o worker receber `429 Retry-After: 60`, falhar heartbeats e **perder o lease** (reproduzido em processos reais). Agora o canal do agente (Bearer) não divide esse limite (tem cota por executor + freio); `Retry-After` do limitador reflete a janela real |
| 4 | Claim idempotente | `Idempotency-Key` opcional no claim; chave gravada em `WorkTask.ClaimKey`; repetir a chave devolve só as tarefas que o MESMO executor ainda segura (lease vigente), sem nova tentativa/evento e sem alterar lease; outro executor nunca recebe por chave. Worker reutiliza a chave após falha indeterminada |
| 5 | Recuperação de leases | Intervalo validado na partida; prazo de 30 s por rodada; laço resiliente; **provado sem nenhum tráfego HTTP** |
| 6 | Migrations | Nova `20261021230549_AddClaimKey` (aditiva); testes da sequência completa, da simulação de upgrade e do script idempotente |
| — | Diagnóstico | `WorkQueueException.Detail` (ex. `http_429`, `HttpRequestException>AuthenticationException`) nos logs do worker |
Arquivos novos: `Api/Integration/AuthFailureThrottle.cs`, `Worker/TlsTrust.cs`, `docs/HTTPS_HOMOLOGATION.md`, ADR 0019, `docs/security/etapa-2c1-claimkey-migration.sql`, migration `AddClaimKey`, testes (`HardeningTests` unit e integração).
Alterados: `IntegrationKeyAuthentication.cs`, `ExecutorAuthentication.cs`, `AgentEndpoints.cs`, `Program.cs` (freio, limitador global/`Retry-After`, validação do intervalo de recuperação), `WorkQueueRecoveryService.cs`, `WorkTask.cs`, `EfWorkTaskStore.cs`, `WorkTaskConfiguration.cs`, `WorkQueueContracts.cs`, `WorkTaskService.cs`, `ExecutorServices.cs`, `HttpWorkQueueClient.cs`, `WorkerService.cs`, `WorkerComposition.cs`, `WorkerOptions.cs`, projeto de testes unitários (referência à Api), `.env.example`, `CLAUDE.md`, `README.md`, `RAILWAY_DEPLOYMENT.md`, relatório 2C (correção).

## 2. Resultados de testes (21/10/2026, PostgreSQL 17 descartável em Docker)
| Verificação | Resultado |
|---|---|
| `dotnet build CentralContingencia.slnx` | 0 erros; 2 avisos NU1510 (conhecidos) |
| Testes unitários | **509/509** (eram 490; +19: freio, TLS/CA, ClaimKey, recuperação, chave de claim no worker) |
| Testes de integração | **390/390** (eram 366; +24) — inclui toda a regressão das Etapas 2A, 2B e 2C |
| Frontend `vitest --maxWorkers=2` | **224/224** (sem alterações no frontend nesta etapa) |
| `docker build` da API | OK |
Alguns testes novos falharam na primeira execução por premissas minhas erradas (validade da CA de teste, ordem de entrega do claim, precisão de microssegundos do PostgreSQL, ordem entre autorização e limitador) e foram corrigidos no teste. O único defeito real do produto (item 3b, limite global) apareceu no teste com processos reais, não nos testes automatizados, e foi corrigido no código e coberto por teste.
Cobertura nova: freio por cliente/escopo/janela, atrás de proxy confiável e contra X-Forwarded-For forjado, sem consultar o banco, chave do n8n; limite global não derruba o worker; claim com chave (mesma chave, outro executor, lease vencido, tarefa concluída, concorrência, chave inválida, perda real da resposta com worker real);
recuperação sem tráfego e com falhas/travamento; intervalo inválido impede a partida; sequência de 16 migrations uma a uma; modelo EF = migrations (`HasPendingModelChanges` falso); upgrade a partir de `…AddProfileHealthChecks` com dados sintéticos preservados (usuário, health check) e objetos novos funcionando; script de upgrade sem `DROP/ALTER COLUMN/TRUNCATE/UPDATE`; script idempotente aplicado duas vezes.

## 3. Teste HTTPS entre processos (evidência)
Ambiente: API (`dotnet …Api.dll`, Kestrel) e worker (`dotnet …Worker.dll`, modo Remote, **sem string de conexão**) em processos separados; PostgreSQL 17 descartável com as 16 migrations (via script idempotente); Admin e credenciais sintéticos; prazos reduzidos (lease 8 s, recuperação 2 s).
| Cenário | Resultado observado |
|---|---|
| A — certificado de desenvolvimento (confiável no sistema) | Worker autenticou: *"Executor e2e-worker-1 … autenticado; lease 8s, heartbeat de tarefa 2s"*. Tarefas: sucesso (1 tentativa), `FailUntilAttempt` (2 tentativas, `Succeeded`), `FailPermanent` (`Failed`, `simulated_permanent`), `FailTransient` (3 tentativas → `DeadLetter`), longa cancelada (auditoria `Created > Claimed > CancelRequested > Cancelled`) |
| B1 — `Stop-Process -Force` no worker durante tarefa, reinício antes do lease | Auditoria `Created > Claimed > Released > Claimed > Succeeded`; **1 tentativa** (a liberação não consumiu) |
| B2 — worker morto por mais que o lease | Tarefa voltou a `Pending` com `lease_expired` **sem nenhum worker e sem tráfego de usuários** (recuperação da API); worker novo concluiu: `Created > Claimed > LeaseExpired > Claimed > Succeeded` (2 tentativas) |
| B2 (1ª execução) | **Falhou e revelou o defeito 3b** (worker com `429 Retry-After 60` por causa do limite global por IP; lease perdido). Reexecutado após a correção: passou |
| C — revogação com o worker rodando | Executor `Revoked`; worker permaneceu vivo, recebeu `401`, aplicou backoff (4–5 s) e **não reservou** a tarefa criada depois (continuou `Pending`) |
| D2 — CA privada, worker **sem** `TrustedCaFile` | Falhou o handshake: `Unavailable/HttpRequestException>AuthenticationException` (validação TLS ativa; curl sem CA também recusou: 000) |
| D1 — mesma API, worker **com** `TrustedCaFile` (CA gerada com openssl, só no processo) | Autenticou e executou a tarefa (`Succeeded`, 1 tentativa, executor `e2e-worker-ca`); sem alterar o repositório de certificados do Windows |
Higiene: `grep` de `ce1.` e `Bearer ` nos 9 logs (API e workers) = 0. Certificados, chaves e credenciais temporários foram apagados; contêiner e imagens removidos.
Não executado: nenhum processo em máquina Windows/VM separada nem atrás do proxy real da Railway (somente loopback); o worker não foi instalado como serviço do Windows.

## 4. Riscos remanescentes
1. **Freio por processo e por IP**: com várias réplicas cada uma conta separado; atacante e worker legítimo atrás do mesmo NAT se afetam (429 também para credencial válida). Mitigação: IP fixo do worker/VM, `Security__AuthFailureLimit` calibrado, monitorar 429.
2. Credenciais inválidas **sem** cabeçalho `Authorization` respondem 401 sem custo (sem banco), mas não são limitadas pelo limitador global (a autorização vem antes dele).
3. Claim idempotente cobre a perda de resposta quando o worker **repete** (ou reinicia → `release`); se o worker morrer sem repetir, vale o vencimento do lease (comportamento normal, até 1 lease de atraso).
4. `TrustedCaFile` não verifica revogação (CA privada) e deve ficar restrito a homologação/VM interna; em produção use certificado público.
5. A **faixa de IP do proxy da Railway continua sem confirmação**; o freio, o rate limit e o HTTPS dependem dela. Sem confirmar, a API recusa iniciar (seguro), mas o primeiro deploy fica bloqueado.
6. Um defeito (3b) só apareceu com processos reais: mais cenários reais (VM, proxy, carga) podem revelar outros. O volume testado é pequeno e simulado.
7. Continuam: credencial do worker na memória/variável do processo; resultado de tarefa livre (≤ 4 KB); sem retenção/limpeza de tarefas e eventos; sem mTLS; repositório sem git; avisos NU1510.
8. Não há alerta automático para executor offline nem para `DeadLetter`.

## 5. Recomendação de prontidão para deploy
- **Homologação (staging) na Railway: PRONTO, condicionado** a: (a) confirmar a faixa do proxy e configurar `ReverseProxy`/`AllowedHosts`/Data Protection/chave AES conforme `RAILWAY_DEPLOYMENT.md`; (b) aplicar as migrations na ordem `AddDataProtectionKeys → AddWorkQueue → AddExecutors → AddClaimKey` em banco **novo**, com script revisado; (c) executar o roteiro da seção 4 de `docs/HTTPS_HOMOLOGATION.md` (inclui provar que a 11ª tentativa inválida dá 429 e que o IP real chega à API); (d) usar só tarefas simuladas.
- **Produção com dados reais e AgenteAdsPower: NÃO recomendado ainda.** Pendências: resultado do roteiro na Railway; teste com worker em máquina/VM separada (idealmente como serviço do Windows); controle de versão; backup/restore do PostgreSQL gerenciado; alerta de executor offline/dead-letter; retenção; revisão de segurança dos tipos de tarefa reais e do executor (sem contornar verificações de plataformas); troca da chave do AdsPower já exposta.
- Nenhum deploy foi feito. Aguardo autorização para o próximo passo.
