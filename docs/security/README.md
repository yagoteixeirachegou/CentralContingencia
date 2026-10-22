# Segurança — Etapa 1

Consulte a [ADR 0002](../decisions/0002-authentication-security.md).
Este ambiente é exclusivamente local e não está pronto para produção.

## Inicialização local

1. Configure .env usando .env.example; a senha é somente do PostgreSQL local.
2. Execute docker compose up -d --wait postgres.
3. Somente após aprovação e backup, execute ./scripts/Migrate-LocalDatabase.ps1.
   Ele agora inclui a migration de auditoria da Etapa 2.2A: revise etapa-2.2a-migration.sql.
   O banco homologado já tem Identity e persistência de proxies; não aplique
   a migration de auditoria sem aprovação específica.
4. Em ambiente novo, execute ./scripts/Provision-FirstAdmin.ps1. Não reprovisionar
   o banco homologado. Use e-mail sintético em example.test
   e senha forte inédita, preenchida no prompt oculto.
5. Execute ./scripts/Start-LocalApi.ps1 e npm run dev no frontend.

A migration e o provisionamento não são executados ao iniciar a API.
Provisionamento falha se já existe Admin. Para recuperação de administrador,
não remova usuários ou papéis manualmente: recuperação não faz parte desta etapa.
Nunca passe senhas como argumentos de linha de comando ou grave-as no Git.

API: https://localhost:7090. Vite: https://localhost:5173.
O certificado ASP.NET existente foi verificado como válido. Caso o navegador
não confie nele, execute dotnet dev-certs https --trust explicitamente na máquina
de desenvolvimento. O plugin HTTPS do Vite usa certificado autoassinado local;
acesse apenas localhost e confirme esse certificado de desenvolvimento no navegador.
Não desabilite a segurança dos cookies para contornar certificados.

## Uso da API

Primeiro GET /api/v1/auth/csrf, mantendo o cookie recebido. Envie o token retornado
no header X-CSRF-TOKEN em cada POST. Depois de login, obtenha novo token associado
ao usuário autenticado. Depois de logout/troca de senha, obtenha token anônimo novo.
Cookies são enviados pelo navegador; nenhum token de sessão vai ao localStorage.

## Senhas de proxy

Consulte [configuração das chaves e migration da Etapa 2.1](proxy-encryption.md).
A chave é separada do Identity/Data Protection e não é requisito para login.

Administrador cria usuários via POST /api/v1/users com email, password e role
(Admin, Manager ou Operator), sessão autenticada e CSRF. Não existe cadastro público
nem tela de criação de usuários nesta etapa. Troca de senha exige currentPassword
e newPassword e encerra todas as sessões. Não há senha fixa de bootstrap.
