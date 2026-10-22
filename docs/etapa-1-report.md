# Relatório — Etapa 1: Autenticação e Segurança

Data local: 10/10/2026 (America/Sao_Paulo).
AGENTS.md, CLAUDE.md e docs/etapa-0-report.md foram lidos integralmente.
O usuário autorizou a Etapa 1; os registros de etapa foram atualizados.

## Implementação

DDD e Clean Architecture preservados. Domain define AuthenticationAuditEvent,
sem dependências externas. Application define DTOs, contratos e nomes de papéis
e políticas. Infrastructure implementa Identity/EF Core/Npgsql e provisionamento.
Api referencia Infrastructure somente para DI; endpoints dependem da Application.

Backend:

- Identity com UUIDs no contexto existente e PostgreSQL, e-mail normalizado único.
- Login, logout, consulta da sessão, alteração de senha e criação de contas por Admin.
- Cookie __Host-Central.Session: HttpOnly, Secure, SameSite=Strict, Path=/,
  não persistente, expiração absoluta de 30 minutos.
- Logout e troca de senha revogam todas as sessões; security stamp validado por requisição.
- Políticas Administration, Management e Operations com autenticação e matriz de papéis.
- Senhas com 12 a 128 caracteres, maiúscula/minúscula/número/símbolo e hasher Identity.
- Cinco falhas bloqueiam por 15 minutos; tentativas concorrentes da mesma conta
  são serializadas no PostgreSQL. Respostas de credenciais/bloqueio são genéricas.
- Rate limiting global 120/minuto/IP; login e operações sensíveis 10/minuto/IP
  por política, sem fila, com 429/Retry-After.
- CSRF validado em todas as mutações /api/v1, inclusive login. Token obtido
  em /auth/csrf, vinculado à identidade e enviado em X-CSRF-TOKEN.
- HTTP recusado para /api/v1; HTTPS também no desenvolvimento.
- Auditoria persistida de login, bloqueio, logout, alteração de senha,
  criação administrativa e bootstrap, sem e-mails, senhas, cookies ou payloads.
- Provisionamento explícito por CLI com senha temporária de processo e prompt
  oculto no script; recusa se já existe Admin e advisory lock contra corrida.
- OpenAPI atualizado com contrato da sessão e header CSRF.

Frontend:

- Login com React Hook Form/Zod, componentes locais na convenção shadcn/ui,
  reutilizando Tailwind, Badge e utilitários existentes.
- AuthProvider com estado em memória, consulta da sessão e integração com API.
- React Router com rotas protegidas e verificação de papel na rota administrativa.
- Layout autenticado, navegação básica e logout.
- Mensagens para credenciais inválidas, bloqueio temporário, expiração,
  acesso negado, limites e falha de conexão na verificação inicial.
- Sessão revalidada quando a janela volta a ficar ativa; CSRF renovado em cada POST.
- Sem armazenamento de tokens ou credenciais em localStorage/sessionStorage.
- HTTPS Vite com proxy de mesma origem para a API local; CORS não habilitado.

Não há tela de criação de usuários: a criação administrativa é feita pela API
documentada, como previsto no escopo frontend solicitado.

## Migration e preservação de dados

Migration: 20261011015548_InitialIdentityAndAuthenticationAudit.
Cria schemas identity/audit, tabelas padrão Identity, AuthenticationEvents e índices.
E-mail obrigatório com limite 254 e índice único normalizado; auditoria em UTC,
ação limitada a 40 caracteres e correlation ID a 128.

O SQL para revisão está em docs/security/etapa-1-migration.sql.
O Up é aditivo. O Down gerado remove as novas tabelas: não foi executado.
Não há aplicação automática de migrations na inicialização.

Migrations foram aplicadas somente em bancos descartáveis p6_auth_test_UUID.
Um teste verificou preservação de uma tabela/linha sintética criada antes da
migration e idempotência da reaplicação. O banco central_contingencia_dev
permanece sem os schemas identity/audit, confirmado por consulta somente leitura.
Os bancos descartáveis foram removidos; consulta final não encontrou remanescentes.

## Testes e resultados reais

| Comando/verificação | Resultado final |
| --- | --- |
| dotnet build CentralContingencia.slnx --no-restore | Sucesso, zero erros e avisos |
| ./scripts/Test-LocalPostgres.ps1 | 47 aprovados: 7 unitários e 40 de integração; zero falhas/ignorados |
| dotnet test tests/CentralContingencia.IntegrationTests --no-build --filter FullyQualifiedName~ApiTests | 4 aprovados após completar os metadados de resposta OpenAPI |
| npm run typecheck | Sucesso |
| npm run lint | Sucesso |
| npm test | 7 aprovados |
| npm run build | Sucesso |
| dotnet ef migrations add InitialIdentityAndAuthenticationAudit | Sucesso, arquivos gerados |
| dotnet ef migrations script | Sucesso, SQL gerado para revisão |
| dotnet ef migrations has-pending-model-changes | Nenhuma alteração de modelo pendente |
| dotnet dev-certs https --check | Certificado ASP.NET local válido |
| Parser dos scripts PowerShell | Sem erros de sintaxe |
| npm audit | Zero vulnerabilidades conhecidas |
| dotnet list package --vulnerable --include-transitive --no-restore | Nenhuma vulnerabilidade conhecida nas fontes consultadas |
| docker compose ps | PostgreSQL healthy em 127.0.0.1:55432 |

Cobertura de integração: login válido/inválido/inexistente; cookie Secure,
HttpOnly e Strict; adulteração; expiração absoluta; lockout sequencial e concorrente;
matriz completa de papéis; criação Admin, recusa Manager/Operator/anônimo;
duplicidade, papel inválido, senha fraca e e-mail inválido; ausência de cadastro;
logout com revogação de outra sessão; alteração de senha e novo login; CSRF ausente,
cookie/token incompatíveis e token anônimo reutilizado após login; limites global
e de login; auditoria sanitizada; bootstrap concorrente; comando CLI real em processo
separado (primeira execução aceita, segunda recusada); preservação de tabela pela
migration; documento OpenAPI e disponibilidade somente em Development.

Os unitários cobrem limites das camadas, evento de auditoria, UTC, limites de
correlation ID e ações/papéis permitidos. Os testes UI cobrem redirecionamento,
login/logout, validação, credenciais inválidas, rota negada, expiração e falha de rede.
As contas usam example.test e senhas sintéticas; nenhum dado real foi utilizado.

## Ocorrências durante a execução

- Uma tentativa de aprovação para instalar pacotes expirou; a repetição concluiu.
- Um download NuGet teve conexão interrompida; a restauração concluiu após nova tentativa.
- O analisador xUnit apontou uso de Where antes de Assert.Single; foi corrigido.
- Ao acrescentar o advisory lock, faltou a importação Microsoft.EntityFrameworkCore;
  houve erro CS1061, corrigido antes da rodada final.
- A suíte ampliada atingiu o limite global ao compartilhar o IP do TestServer.
  A configuração dos testes funcionais passou a usar limites maiores; os limites
  da aplicação foram mantidos e são verificados separadamente em testes isolados.
- O build frontend emitiu dois avisos Rollup sobre comentários de anotação de Zod.
  O bundler removeu esses comentários e concluiu; typecheck/lint/testes passaram.

## Execução local

Na raiz, com .env local configurado:

```powershell
docker compose up -d --wait postgres
dotnet restore
dotnet tool restore
# Revise docs/security/etapa-1-migration.sql.
./scripts/Migrate-LocalDatabase.ps1
# Prompt oculto; use e-mail sintético e senha forte inédita.
./scripts/Provision-FirstAdmin.ps1
./scripts/Start-LocalApi.ps1
```

Em outro terminal:

```powershell
cd src/central-contingencia-web
npm ci
npm run dev
```

Abra https://localhost:5173; API em https://localhost:7090.
Consulte docs/security/README.md para os certificados de desenvolvimento.
O .env não é carregado automaticamente pelo .NET: os scripts preparam a conexão.
Não passe senha de administrador em argumentos ou arquivos versionados.

## Pendências e limites

- A migration e o primeiro Admin do banco local de desenvolvimento não foram
  aplicados/provisionados automaticamente. Execute os comandos explícitos acima.
- Não foi realizada validação visual/end-to-end em navegador real. Os testes UI
  usam Testing Library, e os testes HTTP usam TestServer com PostgreSQL real.
- O prompt oculto PowerShell não foi exercitado interativamente; sua sintaxe foi
  validada. O comando CLI e a lógica transacional foram executados nos testes.
- Certificado ASP.NET válido foi confirmado; confiança no navegador e certificado
  autoassinado do Vite precisam ser conferidos pelo operador local.
- Antes de produção: MFA administrativo, recuperação de contas, operação de chaves
  Data Protection, retenção de auditoria, TLS/proxies confiáveis e rate limiting
  distribuído se houver múltiplas instâncias. Consulte ADR 0002.
- Eventos de CSRF/negação por política/rate limit não são persistidos na auditoria
  desta etapa; os eventos de autenticação e administração listados são persistidos.

PostgreSQL local permanece em execução e seu volume foi preservado.
Sem JWT, cadastro público, CRUD de proxies, AdsPower, Notion, n8n, MCP,
dados reais, commits, deploys ou operações em produção.
