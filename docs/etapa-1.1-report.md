# Relatório — Etapa 1.1: Homologação local da autenticação

Data local: 10/10/2026 (America/Sao_Paulo).
Status: homologação local concluída, com a limitação de confiança TLS descrita abaixo.

AGENTS.md, CLAUDE.md e docs/etapa-1-report.md foram lidos integralmente.
Antes de alterar o banco, foram realizadas inspeção somente leitura, revisão da
migration, backup e apresentação dos comandos. O usuário confirmou explicitamente
a execução com “Aprovo!”. Não houve alterações na arquitetura ou funcionalidades.

## Estado anterior e backup

Destino exclusivamente local: 127.0.0.1:55432, central_contingencia_dev.
PostgreSQL 17.11 saudável, tamanho observado 7518 kB.
Não havia tabelas de aplicação, histórico EF nem bancos de teste remanescentes.
EF listou uma migration pendente: 20261011015548_InitialIdentityAndAuthenticationAudit.

O SQL de aplicação foi revisado integralmente. Cria schemas identity/audit,
sete tabelas Identity, AuthenticationEvents, índices e histórico EF.
Não executa UPDATE, DELETE de dados ou remoção de tabelas existentes.
ON DELETE CASCADE define constraints nas tabelas novas. O Down destrutivo não foi executado.

Backup criado antes da migration:

- Arquivo: .local/backups/p6-pre-auth-20261011T021319Z.dump.
- Formato custom pg_dump/gzip; pg_dump 17.11; tamanho 908 bytes.
- SHA256: `0D205CF37CC82F62D8839C6336C775485ADDF2C3BBF817FE76E37D614FAD1C0A`.
- Leitura de catálogo por pg_restore --list e decodificação por
  pg_restore --file=/dev/null concluídas com sucesso.
- Hash confirmado novamente após a homologação.
- Não foi realizado teste de restauração em outro banco.
- Cópia temporária permanece em /tmp no container.
- .local/ foi adicionada ao .gitignore antes de criar os artefatos locais.

## Execução autorizada

Comandos efetivamente executados, após a confirmação:

```powershell
./scripts/Migrate-LocalDatabase.ps1
./.local/homologacao/Provision-Admin.ps1
./scripts/Start-LocalApi.ps1
# Em src/central-contingencia-web:
npm run dev
```

Migration aplicada com sucesso. Histórico confirmado no banco com EF Core 10.0.8.
Nova execução de migrations list mostrou a migration sem indicação Pending.

Primeiro Admin provisionado pelo comando existente --provision-admin:
homologacao.admin@example.test. A senha foi gerada aleatoriamente em memória;
somente a representação cifrada por DPAPI foi salva em
.local/homologacao/admin-password.dpapi, vinculada ao usuário Windows atual.
Não foi enviada ao chat, argumentos de processo, screenshots ou relatório.

Variáveis temporárias e buffer BSTR foram limpos ao terminar. Nenhuma senha
fixa foi introduzida. O script e a credencial ficam na pasta local ignorada.

## Navegador real e evidências

Playwright Core foi instalado apenas em .local/homologacao.
As dependências do produto não foram alteradas.

Chrome real 154.0.8037.98, Windows, headless, perfis temporários separados.
API e frontend reais, PostgreSQL local real; nenhuma chamada de autenticação mockada.

Execução:

```powershell
./.local/homologacao/Run-Browser.ps1
```

**17 verificações aprovadas:**

| Verificação no Chrome | Resultado |
| --- | --- |
| Rota anônima redireciona ao login | Aprovado |
| Login inválido mostra mensagem e limpa a senha | Aprovado |
| Login sem CSRF ou com token adulterado | Recusado com 400 |
| Login Admin pela interface | Aprovado |
| Cookies seguros e ausência de armazenamento web | Aprovado |
| Sessão preservada após recarga | Aprovado |
| Administração acessível ao Admin; CSRF anônimo antigo recusado | Aprovado |
| Admin cria contas sintéticas Manager e Operator | 201 |
| Manager: login, navegação e administração negada na UI/API | Aprovado; API 403 |
| Operator: login, navegação e administração negada na UI/API | Aprovado; API 403 |
| Logout sem CSRF preserva sessão | Recusado com 400 |
| Logout pela UI revoga também outra sessão Admin | Aprovado; ambas passam a 401 |
| Troca de senha verifica senha atual, revoga sessão e aceita nova senha | Aprovado |
| Manager encerra sessão pela UI | Aprovado |
| Criação anônima recusada e cadastro público inexistente | 401/404 |
| Excesso de tentativas de login | 429 |
| Erros JavaScript não tratados | Zero |

O aviso “Sessão expirada. Entre novamente.” foi validado após revogação por
logout em outra sessão, acionando o evento de foco no navegador.
Não foi aguardada a expiração natural de 30 minutos no Chrome.

Ambos os cookies observados (__Host-Central.Session e __Host-Central.Csrf):
HttpOnly=true, Secure=true, SameSite=Strict, Path=/ e sessão de navegador.
Seus valores não foram registrados. document.cookie não expõe esses cookies;
localStorage e sessionStorage estavam vazios.

Evidências locais, sem passwords, tokens, valores de cookies, HAR ou traces:

- .local/homologacao/evidence/results.json — resultados e atributos não secretos.
- .local/homologacao/evidence/01-login.png.
- .local/homologacao/evidence/02-admin-autenticado.png.
- .local/homologacao/evidence/03-acesso-negado-manager.png.
- .local/homologacao/evidence/03-acesso-negado-operator.png.
- .local/homologacao/evidence/04-sessao-revogada.png.

As telas de login e acesso negado Operator também foram inspecionadas visualmente.
As imagens autenticadas exibem somente identificadores de contas sintéticas example.test.

## Auditoria e estado do banco

Consulta somente leitura confirmou um usuário por papel: Admin, Manager e Operator.
As duas contas não administrativas foram criadas pela API Admin durante a homologação.
Permanecem no banco para preservar o histórico; suas senhas aleatórias não foram
salvas. A senha Operator foi alterada durante o teste e também não foi persistida
em arquivos. A credencial cifrada do Admin foi preservada.

Auditoria local confirmada, 15 eventos:

| Ação | Quantidade |
| --- | --- |
| AdminProvisioned | 1 |
| LoginSucceeded | 5 |
| LoginFailed | 3 |
| Logout | 3 |
| PasswordChanged | 1 |
| UserCreated | 2 |

Campos existentes: Id, ActorId, ResourceId, Action, OccurredAt e CorrelationId.
Sem colunas de e-mail, senha, cookie ou payload livre; timestamps timestamptz.
Nenhum correlation ID fora do limite de 1 a 128 caracteres.
Não foram consultados hashes de senha, valores de cookies ou credenciais.
Recusas CSRF/política/rate limit não geram auditoria persistida, conforme a ADR 0002.

Todos os contextos do navegador foram fechados. Admin, Manager e Operator
encerraram suas sessões; a sessão Admin adicional foi comprovadamente revogada.
Nenhum banco descartável de testes permaneceu após a regressão.

## Regressão executada

| Comando | Resultado |
| --- | --- |
| dotnet build CentralContingencia.slnx --no-restore | Sucesso; zero erros e avisos |
| ./scripts/Test-LocalPostgres.ps1 | 47 aprovados: 7 unitários, 40 integração; zero falhas/ignorados |
| npm run typecheck | Sucesso |
| npm run lint | Sucesso |
| npm test | 7 aprovados |
| npm run build | Sucesso |

Testes .NET de autenticação continuam usando bancos descartáveis, separados do
banco homologado. Incluem lockout sequencial/concorrente, expiração absoluta com
relógio controlado, adulteração de cookie, matriz de papéis, CSRF, criação
administrativa, logout, senha, auditoria, bootstrap e preservação pela migration.

## Ocorrências e limitações

- Na preparação, passagem de aspas por PowerShell gerou avisos em uma chamada psql
  e erro no primeiro node -e. SQL via entrada padrão e script Node por arquivo resolveram.
- O EF registrou falha de SELECT do histórico inexistente antes da primeira
  migration; criou o histórico e concluiu a aplicação normalmente.
- A leitura inicial da credencial DPAPI pelo script de navegador falhou por uma
  quebra de linha final. Foi corrigida com Trim nos scripts locais, sem alterar
  credencial, banco ou produto. A repetição concluiu todas as verificações.
- Rollup emitiu dois avisos sobre anotações de comentários da dependência Zod.
  Removeu os comentários e concluiu o build; lint/typecheck/testes passaram.
- O certificado ASP.NET não é confiável para o sistema. O Chrome também recusou
  o frontend sem exceção TLS. A automação usou ignoreHTTPSErrors somente nos
  contextos locais de teste. Não foi modificada a confiança de certificados do Windows.
  Esta execução **não homologa a cadeia de confiança TLS**.
- Navegador homologado: Chrome desktop headless. Não foram testados Edge,
  dispositivo móvel, interação humana ou expiração natural por espera de 30 minutos.
- O backup foi validado para leitura/decodificação, mas não restaurado.
- As limitações antes de produção da ADR 0002 permanecem; não houve deploy.

## Serviços e reprodução local

Serviços deixados em execução:

- Frontend: https://localhost:5173.
- API: https://localhost:7090; health em /health.
- PostgreSQL: 127.0.0.1:55432, container saudável.
- Processos observados: API PID 30080, Vite PID 16616; IDs valem apenas nesta execução.

O operador precisa conferir a confiança dos certificados ao abrir no navegador.
A senha Admin permanece cifrada no arquivo DPAPI local; não deve ser enviada pelo
chat nem incluída em documentação. Os scripts de navegador a consomem em memória.

Para iniciar em outra sessão, quando esses processos estiverem parados, use
Start-LocalApi.ps1 e npm run dev. Não execute novamente o bootstrap:
já existe Admin e o backend recusará o provisionamento.
O script browser-auth.cjs cria contas sintéticas adicionais em cada repetição;
não executá-lo apenas para abrir o sistema.

Arquivos do produto e arquitetura preservados. Alterações versionáveis desta
homologação: .gitignore, registros de etapa e este relatório. Ferramentas,
credencial cifrada, backup e evidências ficam em .local/.
Sem dados reais/produção, integrações externas, commits ou deploys.
