# ADR 0002 — Autenticação interna e segurança

Status: aceita para Etapa 1, por autorização explícita do usuário.

## Arquitetura

Identity é um detalhe da Infrastructure. Application define contratos e DTOs,
sem EF ou HTTP. Domain contém apenas o evento sanitizado de auditoria. Api usa a
Infrastructure somente para DI. Não foi introduzido JWT nem cadastro público.

O DbContext existente passou a IdentityDbContext com UUIDs, mantendo EF Core 10
e PostgreSQL. Os schemas identity e audit isolam as novas tabelas. O índice único
de e-mail normalizado reforça RequireUniqueEmail no banco. O Up da migration é
aditivo; não altera tabelas preexistentes. O Down gerado remove as novas tabelas:
nunca executar rollback sem avaliação e autorização. Não há migration na inicialização.

## Sessões, transporte e CSRF

Cookie __Host-Central.Session, HttpOnly, Secure, SameSite=Strict, Path=/,
sem Domain e sem persistência entre sessões de navegador. Ticket de 30 minutos,
sem renovação deslizante. HTTPS é obrigatório, inclusive em desenvolvimento.
Logout e troca de senha revogam todas as sessões via security stamp, validado
em cada requisição. O custo é uma consulta ao Identity por requisição autenticada.

GET /api/v1/auth/csrf emite token para X-CSRF-TOKEN. Cookie antiforgery também
é HttpOnly/Secure/Strict. Todas as mutações /api/v1 exigem validação, incluindo
login; o frontend obtém token novo a cada POST, para a identidade atual.
Respostas de sessão são no-store. CORS não é habilitado: o Vite fornece um proxy
de mesma origem. A opção secure=false existe somente no proxy de desenvolvimento
para o certificado local do ASP.NET; nunca transportar isso para produção.

Rotas /api/v1 recusam HTTP com 400 antes de processar autenticação.
Nas demais rotas, o ASP.NET redireciona HTTP para HTTPS quando a porta é conhecida.
Sempre acessar HTTPS diretamente:
redirecionar não protege credenciais já enviadas inicialmente por HTTP.
HSTS é habilitado fora de Development. Kestrel limita o corpo a 16 KiB.

## Senhas, bloqueio e limites

Hasher padrão do ASP.NET Core Identity, sem criptografia reversível.
Senhas: 12 a 128 caracteres, maiúscula, minúscula, número e símbolo.
E-mail validado e limitado a 254 caracteres. Cinco falhas bloqueiam a conta
por 15 minutos. Conta inexistente realiza hashing fictício para reduzir
diferenças de tempo. Respostas de login não distinguem inexistência, erro de
senha e bloqueio; não há garantia de tempo constante.
Tentativas de login da mesma conta são serializadas por advisory lock transacional
com chave derivada do e-mail normalizado, para evitar perda de incrementos em
tentativas concorrentes. Os parâmetros SQL não são registrados com valores.

Rate limiting em memória: 120 requisições/minuto/IP globalmente; 10/minuto/IP
por política de login e operações sensíveis, sem fila. Respostas 429 incluem
Retry-After. Não confiar em X-Forwarded-For de clientes. Em múltiplas instâncias,
limites distribuídos e configuração explícita de proxies serão necessários.

## Papéis

| Política | Admin | Manager | Operator |
| --- | --- | --- | --- |
| Administration | Sim | Não | Não |
| Management | Sim | Sim | Não |
| Operations | Sim | Sim | Sim |

Todas exigem autenticação. POST /users exige Administration, além de checagem
de Admin no adaptador. Manager e Operator não podem criar contas. As políticas
de gestão/operações ficam disponíveis para futuras fatias, sem criar CRUD.

## Primeiro administrador e auditoria

Provisionamento explícito por CLI, com senha em variável temporária de processo
preenchida por prompt oculto. Não existe endpoint de bootstrap, seed de senha ou
criação automática. Um advisory lock transacional PostgreSQL serializa
provisionamentos. A existência de qualquer Admin impede nova execução.
Papéis, conta e auditoria são confirmados em uma transação. As variáveis e
o buffer BSTR são limpos no script. A senha ainda existe temporariamente na
memória do processo; use o comando somente em uma máquina local controlada.

Auditoria persistida: login aceito/recusado, bloqueio, logout, alteração de senha,
criação de usuário e provisionamento. Campos: UUID, ator/recurso UUID opcionais,
ação enum, horário UTC e correlation ID gerado pelo servidor. Sem e-mail, senha,
cookie, IP, payload livre ou dados reais. Eventos de CSRF, autorização e rate limit
não são persistidos nesta etapa. Escritas de auditoria precisam ter sucesso
antes de emitir cookie de login; falha de banco resulta em erro sem login.
Alteração de senha, revogação no logout e suas auditorias também são transacionais.

Referências: [antiforgery do ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery)
e [SecurityStampValidator 10.0.8](https://github.com/dotnet/aspnetcore/blob/v10.0.8/src/Identity/Core/src/SecurityStampValidator.cs).

## Limites antes de produção

MFA administrativo, fluxo de recuperação de conta, política de retenção de
auditoria, operação de chaves Data Protection, TLS/domínio e configuração de
proxy confiável exigem projeto e validação antes de produção. As chaves padrão
locais do ASP.NET não substituem armazenamento protegido e persistente.
Nenhum deploy foi realizado. Não há serviços externos, dados reais ou CRUD.
