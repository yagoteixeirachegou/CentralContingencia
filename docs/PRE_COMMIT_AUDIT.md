# Auditoria pré-commit — CentralContingencia

Data: 21/10/2026. Escopo: os 433 arquivos retornados por `git ls-files --others --exclude-standard` (nada além do `README.md` está commitado). **Nenhum `git add`, `commit`, `push`, deploy ou migration foi executado.** Este relatório não reproduz valores sensíveis.
Veredito: **nenhum segredo, credencial, cookie, token, chave privada ou string de conexão com senha foi encontrado nos arquivos elegíveis.** Há informação operacional/pessoal de baixo risco em documentação (seção 4) que exige sua decisão antes do primeiro push, principalmente se o repositório do GitHub for público.

## 1. Alterações feitas (somente regras de exclusão)
| Item | Situação |
|---|---|
| `*.tsbuildinfo` no `.gitignore` | **Já estava presente** (verificado com `git check-ignore`: `tsconfig.tsbuildinfo` é ignorado); nada a fazer |
| Novas regras de exclusão | `*.csv`, `*.xlsx`, `*.xls` (CLAUDE.md: "não salvar CSV real no Git"), `*.b64`, `*.crt`, `*.cer`, `*.der`, `*.jks` (certificados/chaves codificados), `.claude/settings.local.json` (preferências pessoais da ferramenta), `.idea/` |
| Efeito das novas regras na lista elegível | **Nenhum** (lista idêntica: 433 arquivos; nenhum arquivo necessário foi afetado) |
Nenhum arquivo de código, configuração de aplicação ou documentação foi alterado. Antes desta auditoria o `.gitignore` já cobria: `.env`/`.env.*` (exceto `.env.example`), `.local/`, `*.pfx`, `*.key`, `*.p12`, `**/*.pem`, `*.dpapi`, `*.dump`, `**/secrets/`, `.railway/`, `.data-protection/`, `*.bak`, `*.db`, `*.sqlite*`, `*.log`, `bin/obj/node_modules/dist`, `.npm-cache`, `*.tsbuildinfo`.
Observação técnica: o `.gitignore` começa com BOM UTF-8; comprovado que o Git o ignora corretamente (a primeira regra, `**/bin/`, está em vigor).

## 2. Exclusões verificadas (`git check-ignore -v`)
Ignorados como esperado: `.local/**` (inclui 9 dumps de banco, SQL e segredos DPAPI **reais de desenvolvimento**, ~810 MB), `.env`, `.env.production`, `.env.local`, `*.dpapi`, `*.dump`, `*.pfx`, `*.p12`, `*.key`, `*.pem`, `**/secrets/`, `*.db`, `*.bak`, `*.sqlite`, `*.tsbuildinfo`, `dist/`, `bin/`, `obj/`, `node_modules/`, `.npm-cache/` (~490 MB), `.railway/`.
Ignorados que existem em disco (únicos fora de `bin/obj/node_modules/dist`): `.env`, `.local/`, `.npm-cache/`, `tsconfig.tsbuildinfo`.
**Atenção:** a proteção depende de nunca usar `git add -f` nem copiar `.local/` para outra pasta versionada. `.env` contém apenas as 4 variáveis do PostgreSQL local de desenvolvimento.

## 3. Nada necessário está sendo ignorado
- `git check-ignore` confirmou **não ignorados**: `.env.example`, `Dockerfile`, `.dockerignore`, `railway.json`, `global.json`, `dotnet-tools.json`, `CentralContingencia.slnx`, `appsettings*.json`, `package-lock.json`, `components.json`, `vite.config.ts`, `index.html`, snapshot e migrations do EF, scripts `.ps1`, `docs/n8n/*.json`, `docs/security/*.sql`.
- **Prova por clone limpo:** copiei **somente** os 433 arquivos elegíveis para uma pasta vazia e, nela: `dotnet build CentralContingencia.slnx` (0 erros), testes unitários **534/534**, `npm ci` + `tsc` + `eslint` + `npm run build` sem erros, `vitest` **224/224** e `docker build` da imagem concluído. (Testes de integração exigem PostgreSQL e foram aprovados nas etapas anteriores com o mesmo código.) A pasta temporária foi apagada.

## 4. Achados por categoria
Legenda: **N** nenhum achado · **B** baixo (informação operacional/pessoal, sem acesso) · **M** médio (decisão sua)
| # | Categoria | Nível | Resultado |
|---|---|---|---|
| 1 | Chaves privadas (`BEGIN … PRIVATE KEY`), PFX/PEM/KEY | N | Nenhum arquivo |
| 2 | Credenciais de executor (`ce1.<id>.<segredo>`), `Bearer` com valor, JWT, cookies `__Host-Central.*=valor`, tokens antiforgery | N | Nenhum valor real; só formatos/placeholders e valores sintéticos de testes |
| 3 | Senhas/strings de conexão (`Password=`, `senha`, `secret`) | N | Apenas placeholders (`<…>`), valores sintéticos e marcadores de teste (ex.: textos `hunter2`/`never-used` usados para provar que NÃO vazam) |
| 4 | Tokens/hash longos aleatórios | N | Nenhum; os hex de 32–64 caracteres são **checksums SHA-256/MD5 de arquivos e tabelas** em relatórios (ver item 8) |
| 5 | `.claude/settings.json` | B | Apenas duas permissões de leitura (`Get-Content`, `Get-ChildItem`); sem segredos. Opcional: não versionar (arquivo de preferência da ferramenta) |
| 6 | `appsettings*.json` (4 arquivos), `.env.example` | N | Sem segredos. `AllowedHosts:"*"` no `appsettings.json` base é aceitável porque a API **recusa iniciar** fora de Development com `*` (`ProductionConfiguration`). `.env.example` só tem nomes e placeholders |
| 7 | Scripts PowerShell/`.cjs`/`browser` (27 arquivos) | N | Nenhum segredo nem caminho absoluto pessoal; leem/gravam segredos apenas em `.local/secrets` (DPAPI) ou `%LOCALAPPDATA%`, entrada oculta. `Start-LocalApiFull.ps1` escuta `0.0.0.0:7090` por design (n8n em Docker local) — somente desenvolvimento |
| 8 | Dados operacionais em documentação | **B/M** | Ver 4.1 |
| 9 | Caminhos pessoais | **B** | Ver 4.1 |
| 10 | n8n (`docs/n8n/*.json`) | B | Ver 4.2 |
| 11 | URLs internas/IPs | N | Só `localhost`, `127.0.0.1`, `host.docker.internal`, faixas reservadas de documentação (`203.0.113/24`, `198.51.100/24`, `192.0.2/24`) e valores inválidos usados em testes. Nenhum IP público real, nenhum host de proxy real |
| 12 | E-mails | N | Apenas `@example.test/.com` e padrões de regex; o e-mail da conta do usuário não aparece em nenhum arquivo |
| 13 | Histórico Git | N | Um commit (`first commit`, só `README.md`, sem padrões sensíveis); URL do remoto sem token embutido |

### 4.1 Informação operacional e caminhos pessoais em documentação (decisão sua)
Não há credenciais, mas os relatórios das etapas de recuperação (`docs/etapa-2.4c3-*`, `docs/etapa-2.3*`, `docs/csv-compatibility-*`, `docs/etapa-2.5-*`, trechos de `CLAUDE.md`/`AGENTS.md`) contêm:
- caminho do Windows com o **nome de usuário do computador** e o **nome do arquivo da planilha real** na pasta Downloads (3 relatórios; `CLAUDE.md`/`AGENTS.md` citam só o nome relativo);
- **SHA-256 e tamanho em bytes** da planilha real e de sua cópia filtrada (identificam o arquivo, não revelam conteúdo);
- números reais do ambiente (131 proxies, 129 perfis recuperados, linhas problemáticas, "Perfil 1218");
- nomes de grupos do AdsPower e o nome de uma pessoa embutido em um deles (também no `appsettings.json` e em testes);
- menção de que a chave da API do AdsPower "foi exposta em conversas e deve ser trocada" (sem o valor).
**Recomendação:** se o repositório for **privado**, pode versionar como está. Se houver qualquer chance de ficar público, edite/mova esses relatórios antes (posso fazer a redação sob sua autorização). Em qualquer caso, **troque a chave do AdsPower antes do primeiro push**.

### 4.2 n8n
- `pinData` vazio nos dois fluxos (sem dados de execução fixados); sem `instanceId`, sem IDs de credencial (só os **nomes** das credenciais: "chave do n8n", "chave da API do AdsPower"); `active:false`.
- Os trechos "password/cookie/2FA" nos nós de código são lógica de interpretação do TXT, sem valores. Há o endereço `host.docker.internal:50325` (AdsPower local) e o caminho do webhook `p6-ingestao-perfil` (protegido por chave por cabeçalho no n8n).
- O fluxo 02 (SITE) já desliga o salvamento de execuções; o fluxo 01 (API Central) **não** define isso — ao importar, desative a retenção de execuções, pois os TXT trafegam credenciais.

## 5. Riscos restantes
1. **Visibilidade do repositório não verificada** (não consultei o GitHub): confirme **privado** antes do push — por causa do item 4.1.
2. `.local/` (dumps de banco e segredos DPAPI reais) e `.env` existem na pasta e só estão protegidos pelas regras de exclusão. Evite `git add -f`, `git add --all` com `-f`, ferramentas que ignorem o `.gitignore` e upload da pasta inteira (`railway up` sem Git).
3. A chave do AdsPower exposta continua a trocar (fora do escopo desta auditoria).
4. Sem `.gitattributes` e `core.autocrlf=true`: o Git emitirá avisos CRLF→LF; sem risco de segurança. Recomendo adicionar `.gitattributes` depois, em etapa própria.
5. O `README.md` já commitado é anterior às últimas etapas; ficará atrás da documentação nova até o próximo commit.
6. Avisos de compilação existentes (NU1510 e estilo xUnit) não foram tratados aqui.
7. Esta auditoria é por padrões e leitura dirigida, não por scanner dedicado; antes de tornar público, rode também uma ferramenta como `gitleaks` no histórico final.

## 6. Como confirmar antes de commitar
```
git status --short                       # espere só arquivos de código/docs (nenhum .env/.local/.dump)
git ls-files --others --exclude-standard # 433 + este relatório
git add --dry-run .                      # simula; nada é adicionado
```

## Apêndice — lista final de arquivos elegíveis

(Gerada por `git ls-files --others --exclude-standard`, inclusive este relatório.)

```
.claude/settings.json
.dockerignore
.editorconfig
.env.example
.gitignore
AGENTS.md
CLAUDE.md
CentralContingencia.slnx
Dockerfile
docker-compose.yml
docs/AGENT_INTEGRATION_ASSESSMENT.md
docs/HTTPS_HOMOLOGATION.md
docs/PRE_COMMIT_AUDIT.md
docs/RAILWAY_DEPLOYMENT.md
docs/api/README.md
docs/api/adspower-dashboard.md
docs/api/operational-grid.md
docs/api/operational-profiles-allocations.md
docs/api/profile-ingestion.md
docs/api/profile-recovery.md
docs/api/profile-upload.md
docs/api/proxies.md
docs/api/proxy-csv-import.md
docs/api/proxy-csv-preview.md
docs/architecture/README.md
docs/csv-compatibility-diagnosis.md
docs/csv-compatibility-report.md
docs/decisions/0001-architecture.md
docs/decisions/0002-authentication-security.md
docs/decisions/0003-proxy-domain-encryption.md
docs/decisions/0004-proxy-management-api.md
docs/decisions/0005-proxy-management-ui.md
docs/decisions/0006-proxy-csv-preview.md
docs/decisions/0007-controlled-csv-import.md
docs/decisions/0008-operational-profiles-exclusive-allocations.md
docs/decisions/0009-operational-profiles-allocation-persistence.md
docs/decisions/0010-reservation-reliability.md
docs/decisions/0011-operational-grid-profile-assignments.md
docs/decisions/0012-controlled-profile-recovery.md
docs/decisions/0013-profile-ingestion-integration.md
docs/decisions/0014-admin-proxy-credential-reveal.md
docs/decisions/0015-warmup-health-verification.md
docs/decisions/0016-railway-deployment-preparation.md
docs/decisions/0017-work-queue-and-worker.md
docs/decisions/0018-executor-identity-and-https-channel.md
docs/decisions/0019-auth-throttle-claim-replay-and-recovery.md
docs/etapa-0-report.md
docs/etapa-1-report.md
docs/etapa-1.1-report.md
docs/etapa-2.1-report.md
docs/etapa-2.2a-report.md
docs/etapa-2.2b-report.md
docs/etapa-2.3a-report.md
docs/etapa-2.3b-homologacao-preparacao.md
docs/etapa-2.3b-homologacao-report.md
docs/etapa-2.3b-report.md
docs/etapa-2.3c-report.md
docs/etapa-2.3c.1-report.md
docs/etapa-2.4a-report.md
docs/etapa-2.4b-homologacao-preparacao.md
docs/etapa-2.4b-homologacao-report.md
docs/etapa-2.4b1-report.md
docs/etapa-2.4b2-report.md
docs/etapa-2.4b2-revisao-report.md
docs/etapa-2.4b3-report.md
docs/etapa-2.4c-grid-operacional-diagnostico.md
docs/etapa-2.4c-report.md
docs/etapa-2.4c1-homologacao-report.md
docs/etapa-2.4c1-report.md
docs/etapa-2.4c2-report.md
docs/etapa-2.4c3-filtered-preview-report.md
docs/etapa-2.4c3-homologacao-preparacao.md
docs/etapa-2.4c3-homologacao-report.md
docs/etapa-2.4c3-preview-real-correction-report.md
docs/etapa-2.4c3-preview-real-report.md
docs/etapa-2.4c3-recovery-ready-report.md
docs/etapa-2.4c3-report.md
docs/etapa-2.4c3-update-preparation.md
docs/etapa-2.5-ingestion-report.md
docs/etapa-2.5-report.md
docs/etapa-2b-report.md
docs/etapa-2c-report.md
docs/etapa-2c1-report.md
docs/etapa-2d-predeploy.md
docs/n8n/P6 - 01 - INGESTAO DE PERFIL (API Central).json
docs/n8n/P6 - 02 - INGESTAO DE PERFIL (SITE).json
docs/n8n/README.md
docs/security/README.md
docs/security/etapa-1-migration.sql
docs/security/etapa-2.1-migration.sql
docs/security/etapa-2.2a-migration.sql
docs/security/etapa-2.3b-migration.sql
docs/security/etapa-2.4b1-migration.sql
docs/security/etapa-2.4b2-migration.sql
docs/security/etapa-2.4c1-migration.sql
docs/security/etapa-2.4c3-infoproduto-migration.sql
docs/security/etapa-2.4c3-migration.sql
docs/security/etapa-2.5-ingestion-group-migration.sql
docs/security/etapa-2.5-profile-ingestion-migration.sql
docs/security/etapa-2a-dataprotection-migration.sql
docs/security/etapa-2b-work-queue-migration.sql
docs/security/etapa-2c-executors-migration.sql
docs/security/etapa-2c1-claimkey-migration.sql
docs/security/proxy-encryption.md
docs/ui/csv-import.md
docs/ui/operational-profiles.md
docs/ui/proxies.md
docs/warmup/verification-prompt.md
dotnet-tools.json
global.json
railway.json
scripts/Compare-LocalDatabaseSnapshot.ps1
scripts/DatabaseSnapshot.ps1
scripts/Initialize-LocalProxyEncryption.ps1
scripts/Migrate-LocalDatabase.ps1
scripts/New-LocalIntegrationKey.ps1
scripts/New-LocalN8nWebhookKey.ps1
scripts/Provision-FirstAdmin.ps1
scripts/Save-LocalAdsPowerKey.ps1
scripts/Save-WorkerCredential.ps1
scripts/Set-LocalAdsPower.ps1
scripts/Set-LocalDatabase.ps1
scripts/Set-LocalIntegration.ps1
scripts/Set-LocalN8nWebhook.ps1
scripts/Set-LocalProxyEncryption.ps1
scripts/Start-LocalApi.ps1
scripts/Start-LocalApiFull.ps1
scripts/Start-Worker.ps1
scripts/Test-DatabaseSnapshot.ps1
scripts/Test-LocalPostgres.ps1
scripts/Test-ProfileUiIntegration.ps1
scripts/Test-ProxyUiIntegration.ps1
scripts/Verify-CsvImportUi.cjs
scripts/Verify-OperationalGridUi.cjs
scripts/Verify-ProfileUi.cjs
scripts/Verify-ProxyUi.cjs
scripts/browser/BrowserFixture.csproj
scripts/browser/Program.cs
src/CentralContingencia.Api/AdsPower/AdsPowerEndpoints.cs
src/CentralContingencia.Api/CentralContingencia.Api.csproj
src/CentralContingencia.Api/CentralContingencia.Api.http
src/CentralContingencia.Api/Hosting/LocalIntegrations.cs
src/CentralContingencia.Api/Hosting/PlatformHosting.cs
src/CentralContingencia.Api/Hosting/ProductionConfiguration.cs
src/CentralContingencia.Api/Hosting/ReverseProxySettings.cs
src/CentralContingencia.Api/Hosting/SpaHosting.cs
src/CentralContingencia.Api/Identity/AuthEndpoints.cs
src/CentralContingencia.Api/Integration/AuthFailureThrottle.cs
src/CentralContingencia.Api/Integration/IngestionEndpoints.cs
src/CentralContingencia.Api/Integration/IntegrationKeyAuthentication.cs
src/CentralContingencia.Api/Integration/ProfileUploadEndpoints.cs
src/CentralContingencia.Api/Operations/OperationalEndpoints.cs
src/CentralContingencia.Api/Program.cs
src/CentralContingencia.Api/Properties/launchSettings.json
src/CentralContingencia.Api/Proxies/OperationalGridEndpoint.cs
src/CentralContingencia.Api/Proxies/ProxyEndpoints.cs
src/CentralContingencia.Api/Proxies/ProxyImportPreviewEndpoint.cs
src/CentralContingencia.Api/Warmup/WarmupEndpoints.cs
src/CentralContingencia.Api/WorkQueue/AgentEndpoints.cs
src/CentralContingencia.Api/WorkQueue/ExecutorAdminEndpoints.cs
src/CentralContingencia.Api/WorkQueue/ExecutorAuthentication.cs
src/CentralContingencia.Api/WorkQueue/WorkQueueRecoveryService.cs
src/CentralContingencia.Api/WorkQueue/WorkTaskEndpoints.cs
src/CentralContingencia.Api/appsettings.Development.json
src/CentralContingencia.Api/appsettings.Production.json
src/CentralContingencia.Api/appsettings.json
src/CentralContingencia.Application/AdsPower/AdsPowerContracts.cs
src/CentralContingencia.Application/AdsPower/AdsPowerDashboardService.cs
src/CentralContingencia.Application/Allocations/AllocationPersistenceContracts.cs
src/CentralContingencia.Application/Allocations/OperationalContracts.cs
src/CentralContingencia.Application/Allocations/OperationalManagementService.cs
src/CentralContingencia.Application/Allocations/ReservationOptions.cs
src/CentralContingencia.Application/CentralContingencia.Application.csproj
src/CentralContingencia.Application/Health/IReadinessProbe.cs
src/CentralContingencia.Application/Identity/AuthContracts.cs
src/CentralContingencia.Application/Ingestion/IngestionContracts.cs
src/CentralContingencia.Application/Ingestion/ProfileIngestionService.cs
src/CentralContingencia.Application/Profiles/Recovery/ProfileRecoveryService.cs
src/CentralContingencia.Application/Profiles/Recovery/RecoveryContracts.cs
src/CentralContingencia.Application/Profiles/Recovery/RecoveryCsvReader.cs
src/CentralContingencia.Application/Proxies/IProxyPasswordProtector.cs
src/CentralContingencia.Application/Proxies/Import/BoundedCsvParser.cs
src/CentralContingencia.Application/Proxies/Import/ImportContracts.cs
src/CentralContingencia.Application/Proxies/Import/PreviewContracts.cs
src/CentralContingencia.Application/Proxies/Import/ProxyCsvImportService.cs
src/CentralContingencia.Application/Proxies/Import/ProxyCsvPreviewService.cs
src/CentralContingencia.Application/Proxies/OperationalGridContracts.cs
src/CentralContingencia.Application/Proxies/ProxyContracts.cs
src/CentralContingencia.Application/Proxies/ProxyInputRules.cs
src/CentralContingencia.Application/Proxies/ProxyManagementService.cs
src/CentralContingencia.Application/Warmup/HealthCheckService.cs
src/CentralContingencia.Application/Warmup/WarmupContracts.cs
src/CentralContingencia.Application/WorkQueue/ExecutorContracts.cs
src/CentralContingencia.Application/WorkQueue/ExecutorServices.cs
src/CentralContingencia.Application/WorkQueue/WorkQueueContracts.cs
src/CentralContingencia.Application/WorkQueue/WorkQueueOptions.cs
src/CentralContingencia.Application/WorkQueue/WorkTaskPayloads.cs
src/CentralContingencia.Application/WorkQueue/WorkTaskService.cs
src/CentralContingencia.Domain/Allocations/AllocationDomainService.cs
src/CentralContingencia.Domain/Allocations/ProxyAllocation.cs
src/CentralContingencia.Domain/Audit/AuthenticationAuditEvent.cs
src/CentralContingencia.Domain/Audit/OperationalAuditEvent.cs
src/CentralContingencia.Domain/Audit/ProxyAuditEvent.cs
src/CentralContingencia.Domain/CentralContingencia.Domain.csproj
src/CentralContingencia.Domain/Profiles/OperationalProfile.cs
src/CentralContingencia.Domain/Profiles/ProfileAssignments.cs
src/CentralContingencia.Domain/Profiles/ProfileHealthCheck.cs
src/CentralContingencia.Domain/Profiles/ProfileIngestionBatch.cs
src/CentralContingencia.Domain/Profiles/ProfileName.cs
src/CentralContingencia.Domain/Profiles/ProfileNumber.cs
src/CentralContingencia.Domain/Profiles/ProfileRecoveryBatch.cs
src/CentralContingencia.Domain/Proxies/EncryptedProxyPassword.cs
src/CentralContingencia.Domain/Proxies/Proxy.cs
src/CentralContingencia.Domain/Proxies/ProxyHost.cs
src/CentralContingencia.Domain/Proxies/ProxyImportBatch.cs
src/CentralContingencia.Domain/Proxies/ProxyPort.cs
src/CentralContingencia.Domain/Proxies/ProxyRules.cs
src/CentralContingencia.Domain/WorkQueue/WorkErrorSanitizer.cs
src/CentralContingencia.Domain/WorkQueue/WorkExecutor.cs
src/CentralContingencia.Domain/WorkQueue/WorkTask.cs
src/CentralContingencia.Domain/WorkQueue/WorkTaskEnums.cs
src/CentralContingencia.Domain/WorkQueue/WorkTaskEvent.cs
src/CentralContingencia.Infrastructure/AdsPower/AdsPowerLocalApiClient.cs
src/CentralContingencia.Infrastructure/Allocations/AllocationPersistenceErrors.cs
src/CentralContingencia.Infrastructure/Allocations/EfAllocationStore.cs
src/CentralContingencia.Infrastructure/Allocations/EfIngestionStore.cs
src/CentralContingencia.Infrastructure/Allocations/EfOperationalProfileStore.cs
src/CentralContingencia.Infrastructure/CentralContingencia.Infrastructure.csproj
src/CentralContingencia.Infrastructure/DependencyInjection.cs
src/CentralContingencia.Infrastructure/Hosting/CentralDataProtection.cs
src/CentralContingencia.Infrastructure/Hosting/EfReadinessProbe.cs
src/CentralContingencia.Infrastructure/Hosting/MigrationRunner.cs
src/CentralContingencia.Infrastructure/Identity/AdminProvisioner.cs
src/CentralContingencia.Infrastructure/Identity/ApplicationUser.cs
src/CentralContingencia.Infrastructure/Identity/AuthenticationService.cs
src/CentralContingencia.Infrastructure/Persistence/CentralContingenciaDbContext.cs
src/CentralContingencia.Infrastructure/Persistence/Configurations/OperationalProfileConfiguration.cs
src/CentralContingencia.Infrastructure/Persistence/Configurations/ProfileHealthCheckConfiguration.cs
src/CentralContingencia.Infrastructure/Persistence/Configurations/ProfileIngestionBatchConfiguration.cs
src/CentralContingencia.Infrastructure/Persistence/Configurations/ProfileRecoveryConfiguration.cs
src/CentralContingencia.Infrastructure/Persistence/Configurations/ProxyAllocationConfiguration.cs
src/CentralContingencia.Infrastructure/Persistence/Configurations/ProxyConfiguration.cs
src/CentralContingencia.Infrastructure/Persistence/Configurations/WorkTaskConfiguration.cs
src/CentralContingencia.Infrastructure/Persistence/DesignTimeDbContextFactory.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011015548_InitialIdentityAndAuthenticationAudit.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011015548_InitialIdentityAndAuthenticationAudit.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011024141_AddProxyPersistence.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011024141_AddProxyPersistence.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011025452_AddProxyAudit.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011025452_AddProxyAudit.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011165123_AddProxyImportBatches.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011165123_AddProxyImportBatches.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011223416_AddOperationalProfilesAndAllocations.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011223416_AddOperationalProfilesAndAllocations.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011230449_AddOperationalAudit.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261011230449_AddOperationalAudit.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012102737_AddProfileAssignments.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012102737_AddProfileAssignments.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012131756_AddProfileRecoveryReceipts.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012131756_AddProfileRecoveryReceipts.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012174401_AddInfoproductProfileAssignment.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012174401_AddInfoproductProfileAssignment.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012230807_AddProfileIngestion.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261012230807_AddProfileIngestion.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261013105059_AddIngestionBatchGroup.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261013105059_AddIngestionBatchGroup.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261016175350_AddProfileHealthChecks.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261016175350_AddProfileHealthChecks.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021183950_AddDataProtectionKeys.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021183950_AddDataProtectionKeys.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021191536_AddWorkQueue.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021191536_AddWorkQueue.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021205430_AddExecutors.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021205430_AddExecutors.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021230549_AddClaimKey.Designer.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/20261021230549_AddClaimKey.cs
src/CentralContingencia.Infrastructure/Persistence/Migrations/CentralContingenciaDbContextModelSnapshot.cs
src/CentralContingencia.Infrastructure/Profiles/EfHealthCheckStore.cs
src/CentralContingencia.Infrastructure/Profiles/EfProfileRecoveryStore.cs
src/CentralContingencia.Infrastructure/Proxies/AesGcmProxyPasswordProtector.cs
src/CentralContingencia.Infrastructure/Proxies/DataProtectionImportConfirmation.cs
src/CentralContingencia.Infrastructure/Proxies/EfOperationalGridReader.cs
src/CentralContingencia.Infrastructure/Proxies/EfProxyImportConflictReader.cs
src/CentralContingencia.Infrastructure/Proxies/EfProxyImportStore.cs
src/CentralContingencia.Infrastructure/Proxies/EfProxyStore.cs
src/CentralContingencia.Infrastructure/WorkQueue/EfExecutorStore.cs
src/CentralContingencia.Infrastructure/WorkQueue/EfWorkTaskStore.cs
src/CentralContingencia.Worker/CentralContingencia.Worker.csproj
src/CentralContingencia.Worker/Executors.cs
src/CentralContingencia.Worker/HttpWorkQueueClient.cs
src/CentralContingencia.Worker/Program.cs
src/CentralContingencia.Worker/TaskRunner.cs
src/CentralContingencia.Worker/TlsTrust.cs
src/CentralContingencia.Worker/WorkerComposition.cs
src/CentralContingencia.Worker/WorkerOptions.cs
src/CentralContingencia.Worker/WorkerService.cs
src/CentralContingencia.Worker/appsettings.json
src/central-contingencia-web/components.json
src/central-contingencia-web/eslint.config.js
src/central-contingencia-web/index.html
src/central-contingencia-web/package-lock.json
src/central-contingencia-web/package.json
src/central-contingencia-web/src/app/App.test.tsx
src/central-contingencia-web/src/app/App.tsx
src/central-contingencia-web/src/app/QueryScope.tsx
src/central-contingencia-web/src/components/layout/AuthenticatedLayout.tsx
src/central-contingencia-web/src/components/layout/Breadcrumbs.test.tsx
src/central-contingencia-web/src/components/layout/Breadcrumbs.tsx
src/central-contingencia-web/src/components/layout/StatusPages.tsx
src/central-contingencia-web/src/components/layout/sheets.ts
src/central-contingencia-web/src/components/layout/trail.ts
src/central-contingencia-web/src/components/ui/badge.tsx
src/central-contingencia-web/src/components/ui/button.tsx
src/central-contingencia-web/src/components/ui/dialog.tsx
src/central-contingencia-web/src/components/ui/dropdown-menu.tsx
src/central-contingencia-web/src/components/ui/input.tsx
src/central-contingencia-web/src/components/ui/select.tsx
src/central-contingencia-web/src/components/ui/skeleton.test.tsx
src/central-contingencia-web/src/components/ui/skeleton.tsx
src/central-contingencia-web/src/components/ui/theme-toggle.test.tsx
src/central-contingencia-web/src/components/ui/theme-toggle.tsx
src/central-contingencia-web/src/features/auth/AuthProvider.tsx
src/central-contingencia-web/src/features/auth/LoginArt.tsx
src/central-contingencia-web/src/features/auth/LoginPage.tsx
src/central-contingencia-web/src/features/auth/ProtectedRoute.tsx
src/central-contingencia-web/src/features/auth/auth-context.ts
src/central-contingencia-web/src/features/dashboard/DashboardPage.test.tsx
src/central-contingencia-web/src/features/dashboard/DashboardPage.tsx
src/central-contingencia-web/src/features/dashboard/GroupTiles.tsx
src/central-contingencia-web/src/features/dashboard/ProfileTable.tsx
src/central-contingencia-web/src/features/dashboard/api.ts
src/central-contingencia-web/src/features/dashboard/filters.test.ts
src/central-contingencia-web/src/features/dashboard/filters.ts
src/central-contingencia-web/src/features/ingestion/IngestionPage.test.tsx
src/central-contingencia-web/src/features/ingestion/IngestionPage.tsx
src/central-contingencia-web/src/features/ingestion/api.ts
src/central-contingencia-web/src/features/profiles/AllocationHistory.tsx
src/central-contingencia-web/src/features/profiles/OperationalPagination.tsx
src/central-contingencia-web/src/features/profiles/ProfileDetailsPage.tsx
src/central-contingencia-web/src/features/profiles/ProfileForm.tsx
src/central-contingencia-web/src/features/profiles/ProfilesPage.test.tsx
src/central-contingencia-web/src/features/profiles/ProfilesPage.tsx
src/central-contingencia-web/src/features/profiles/ReserveProxyForm.tsx
src/central-contingencia-web/src/features/profiles/api.ts
src/central-contingencia-web/src/features/profiles/contracts.ts
src/central-contingencia-web/src/features/proxies/ProxiesPage.test.tsx
src/central-contingencia-web/src/features/proxies/ProxiesPage.tsx
src/central-contingencia-web/src/features/proxies/ProxyCredentialsPanel.test.tsx
src/central-contingencia-web/src/features/proxies/ProxyCredentialsPanel.tsx
src/central-contingencia-web/src/features/proxies/ProxyForm.tsx
src/central-contingencia-web/src/features/proxies/api.ts
src/central-contingencia-web/src/features/proxies/contracts.ts
src/central-contingencia-web/src/features/proxies/form.test.ts
src/central-contingencia-web/src/features/proxies/form.ts
src/central-contingencia-web/src/features/proxies/grid/AssignmentsForm.tsx
src/central-contingencia-web/src/features/proxies/grid/GridFilters.tsx
src/central-contingencia-web/src/features/proxies/grid/LinkProfileForm.tsx
src/central-contingencia-web/src/features/proxies/grid/OperationalGridPage.test.tsx
src/central-contingencia-web/src/features/proxies/grid/OperationalGridTable.tsx
src/central-contingencia-web/src/features/proxies/grid/RowActions.tsx
src/central-contingencia-web/src/features/proxies/grid/api.ts
src/central-contingencia-web/src/features/proxies/grid/contracts.ts
src/central-contingencia-web/src/features/proxies/grid/navigation.ts
src/central-contingencia-web/src/features/proxies/import/ImportPreviewTable.tsx
src/central-contingencia-web/src/features/proxies/import/ProxyImportPage.test.tsx
src/central-contingencia-web/src/features/proxies/import/ProxyImportPage.tsx
src/central-contingencia-web/src/features/proxies/import/api.ts
src/central-contingencia-web/src/features/proxies/import/contracts.ts
src/central-contingencia-web/src/features/tasks/ExecutorsPage.test.tsx
src/central-contingencia-web/src/features/tasks/ExecutorsPage.tsx
src/central-contingencia-web/src/features/tasks/TasksPage.test.tsx
src/central-contingencia-web/src/features/tasks/TasksPage.tsx
src/central-contingencia-web/src/features/tasks/api.ts
src/central-contingencia-web/src/features/warmup/WarmupPage.test.tsx
src/central-contingencia-web/src/features/warmup/WarmupPage.tsx
src/central-contingencia-web/src/features/warmup/verificationPrompt.ts
src/central-contingencia-web/src/lib/api/client.ts
src/central-contingencia-web/src/lib/brand.ts
src/central-contingencia-web/src/lib/theme.ts
src/central-contingencia-web/src/lib/utils.ts
src/central-contingencia-web/src/main.tsx
src/central-contingencia-web/src/styles.css
src/central-contingencia-web/src/test/setup.ts
src/central-contingencia-web/tsconfig.json
src/central-contingencia-web/vite.config.ts
tests/CentralContingencia.IntegrationTests/AdsPowerEndpointTests.cs
tests/CentralContingencia.IntegrationTests/AgentChannelTests.cs
tests/CentralContingencia.IntegrationTests/AllocationPersistenceTests.cs
tests/CentralContingencia.IntegrationTests/ApiTests.cs
tests/CentralContingencia.IntegrationTests/AuthFixture.cs
tests/CentralContingencia.IntegrationTests/AuthenticationTests.cs
tests/CentralContingencia.IntegrationTests/CentralContingencia.IntegrationTests.csproj
tests/CentralContingencia.IntegrationTests/CsvImportEndpointTests.cs
tests/CentralContingencia.IntegrationTests/CsvPreviewEndpointTests.cs
tests/CentralContingencia.IntegrationTests/DeploymentTests.cs
tests/CentralContingencia.IntegrationTests/HardeningTests.cs
tests/CentralContingencia.IntegrationTests/IngestionEndpointTests.cs
tests/CentralContingencia.IntegrationTests/OperationalEndpointTests.cs
tests/CentralContingencia.IntegrationTests/OperationalGridEndpointTests.cs
tests/CentralContingencia.IntegrationTests/PostgresTests.cs
tests/CentralContingencia.IntegrationTests/PreDeployTests.cs
tests/CentralContingencia.IntegrationTests/ProfileRecoveryEndpointTests.cs
tests/CentralContingencia.IntegrationTests/ProfileUploadEndpointTests.cs
tests/CentralContingencia.IntegrationTests/ProxyEndpointTests.cs
tests/CentralContingencia.IntegrationTests/ProxyPersistenceTests.cs
tests/CentralContingencia.IntegrationTests/RemoteWorkerEndToEndTests.cs
tests/CentralContingencia.IntegrationTests/ReservationReliabilityTests.cs
tests/CentralContingencia.IntegrationTests/WorkQueueStoreTests.cs
tests/CentralContingencia.IntegrationTests/WorkTaskApiTests.cs
tests/CentralContingencia.IntegrationTests/WorkerEndToEndTests.cs
tests/CentralContingencia.UnitTests/AdsPowerTests.cs
tests/CentralContingencia.UnitTests/AllocationDomainTests.cs
tests/CentralContingencia.UnitTests/AllocationPersistenceModelTests.cs
tests/CentralContingencia.UnitTests/ArchitectureTests.cs
tests/CentralContingencia.UnitTests/AuthenticationAuditTests.cs
tests/CentralContingencia.UnitTests/CentralContingencia.UnitTests.csproj
tests/CentralContingencia.UnitTests/CsvCompatibilityTests.cs
tests/CentralContingencia.UnitTests/CsvImportTests.cs
tests/CentralContingencia.UnitTests/CsvPreviewTests.cs
tests/CentralContingencia.UnitTests/ExecutorTests.cs
tests/CentralContingencia.UnitTests/HardeningTests.cs
tests/CentralContingencia.UnitTests/HealthCheckTests.cs
tests/CentralContingencia.UnitTests/OperationalApplicationTests.cs
tests/CentralContingencia.UnitTests/OperationalGridTests.cs
tests/CentralContingencia.UnitTests/PreDeployTests.cs
tests/CentralContingencia.UnitTests/ProfileIngestionTests.cs
tests/CentralContingencia.UnitTests/ProfileRecoveryTests.cs
tests/CentralContingencia.UnitTests/ProxyApplicationTests.cs
tests/CentralContingencia.UnitTests/ProxyDomainTests.cs
tests/CentralContingencia.UnitTests/ProxyEncryptionTests.cs
tests/CentralContingencia.UnitTests/ReservationReliabilityTests.cs
tests/CentralContingencia.UnitTests/WorkTaskPayloadTests.cs
tests/CentralContingencia.UnitTests/WorkTaskTests.cs
tests/CentralContingencia.UnitTests/WorkerTests.cs
```
