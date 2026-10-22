# CLAUDE.md — Central de Contingência (P6)

> Instruções permanentes para Claude no VSCode. Leia antes de alterar código. Não presuma que funcionalidades planejadas já existem.

## Objetivo e escopo

Construir uma plataforma interna para substituir gradualmente a planilha `HTTP - FACEBOOK UNIFICADA`. **V1:** autenticação, gestão de proxies, importação assistida de CSV, cadastro mínimo de perfis, reserva/confirmação/liberação de alocações, auditoria e dashboard básico. Manter a planilha original até validação formal da migração.

Não implementar automação de navegação, engajamento artificial, evasão de controles de plataformas ou manipulação de contas sem autorização. Não importar dados reais sem confirmação explícita.

## Stack decidida

- **Backend:** C# / ASP.NET Core 10 Web API, PostgreSQL, EF Core 10 com Npgsql.
- **Frontend:** React 19, TypeScript, Vite, Tailwind CSS, shadcn/ui, Lucide React.
- **Frontend libraries:** React Router, TanStack Query, TanStack Table, React Hook Form, Zod.
- **Autenticação:** ASP.NET Core Identity; sessões via cookies `HttpOnly`, `Secure`, `SameSite` e proteção CSRF adequada.
- **Testes:** xUnit; Vitest + Testing Library; testes de integração com PostgreSQL real.
- **Infra:** Docker Compose para desenvolvimento; Railway é destino potencial, não configurado ainda.
- **API:** REST versionada, OpenAPI.
- **Não usar Angular**, Next.js, Firebase, Supabase, MongoDB, microsserviços ou CQRS completo sem aprovação.

## Arquitetura: DDD + Clean Architecture + Clean Code

Usar **monólito modular**, com DDD tático pragmático e dependências voltadas para dentro:

- `Domain`: entidades, value objects, regras e invariantes; nenhuma dependência de EF, HTTP, UI ou serviços externos.
- `Application`: casos de uso, contratos (ports), DTOs, validação e regras de autorização; sem detalhes de persistência.
- `Infrastructure`: EF Core, Identity, criptografia, persistência e adapters.
- `Api`: endpoints, autenticação HTTP, DI/composition root, OpenAPI e tratamento de erros.
- `Web`: interface e chamadas à API; nunca fonte de verdade para autorização.

Referências permitidas: `Application -> Domain`, `Infrastructure -> Application/Domain`, `Api -> Application/Infrastructure` (Infrastructure apenas para DI). Evitar generic repositories universais, abstrações artificiais e frameworks desnecessários. Código legível, nomes explícitos, métodos coesos, tratamento de erros e testes das regras críticas.

## Estrutura inicial

```text
CentralContingencia/
├── CLAUDE.md
├── README.md
├── .gitignore
├── .env.example
├── docker-compose.yml
├── src/
│   ├── CentralContingencia.Domain/
│   ├── CentralContingencia.Application/
│   ├── CentralContingencia.Infrastructure/
│   ├── CentralContingencia.Api/
│   └── central-contingencia-web/
├── tests/
│   ├── CentralContingencia.UnitTests/
│   └── CentralContingencia.IntegrationTests/
└── docs/
    ├── architecture/
    ├── decisions/
    ├── api/
    └── security/
```

Organizar código por contextos `Proxies`, `Profiles`, `Allocations`, `Identity`, `Audit`, com limites claros. Registrar decisões relevantes em `docs/decisions/`.

## Domínio V1

**Proxy:** UUID, host, porta, usuário, senha cifrada, status, data de vencimento, região, timestamps. `ATIVO` não significa automaticamente `DISPONÍVEL`: elegibilidade depende de status, validade e ausência de alocação ativa.

**Profile:** UUID, nome, plataforma, AdsPowerId opcional, AdsPowerGroupId opcional, status operacional, timestamps. Não guardar senha de Facebook, cookies, segredos 2FA, códigos de recuperação ou documentos na V1.

**ProxyAllocation:** UUID, ProxyId, ProfileId, estado (`Reserved`, `Confirmed`, `Released`, `Expired`), horários, operador, chave de idempotência. Garantir no PostgreSQL **no máximo uma alocação ativa por proxy** por índice único parcial, além de transações. Reservas abandonadas expiram conforme regra explícita.

**AuditEvent:** UUID, ator, ação, tipo/ID do recurso, horário UTC, correlation ID e metadados sanitizados. Nunca registrar segredos ou payloads completos.

Definir constraints, índices, nulabilidade, tamanhos e unicidade antes de criar migrations. Nome de perfil não é identificador global confiável.

## Segurança inegociável

1. Senhas de usuários: hash seguro do ASP.NET Core Identity; nunca reversível.
2. Senhas de proxies: criptografia autenticada **AES-256-GCM**, nonce único, tag, versão de chave (`KeyId`), chave fora do banco e Git. Testar adulteração e planejar rotação.
3. Usuários de proxy e outros identificadores: minimizar exposição; criptografar quando necessário pelo modelo de ameaça.
4. Nunca devolver senha de proxy descriptografada em endpoints de listagem, frontend, logs, exportações ou MCP. **Exceção aprovada (ADR 0014, 14/10/2026):** `POST /api/v1/proxies/{id}/credentials/reveal`, só Admin, auditado, sem cache, uma proxy por vez; a UI mostra pontos e o olho consulta o endpoint. Nada mais devolve a senha.
5. Segredos apenas em variáveis protegidas/secret manager; `.env` real e arquivos de credenciais fora do Git.
6. Login interno por e-mail/senha, contas criadas por administradores, sem cadastro público. Papéis iniciais: `Admin`, `Manager`, `Operator`, `Integration`; aplicar menor privilégio no backend.
7. Cookies seguros, CSRF, CORS restrito, rate limiting, lockout, MFA administrativo antes de produção.
8. Credenciais de serviço para integrações, com escopos específicos; nunca reutilizar login humano compartilhado.
9. Logs e mensagens de erro não devem conter host/usuário/senha de proxy quando isso for sensível, tokens, cookies ou dados pessoais desnecessários.
10. Nunca executar migração destrutiva, importar dados reais, modificar produção, commitar ou fazer deploy sem autorização explícita.

## API REST planejada

**Autenticação e usuários**
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/logout`
- `GET /api/v1/auth/me`
- `POST /api/v1/auth/change-password`
- `GET /api/v1/users` (Admin)
- `POST /api/v1/users` (Admin)

**Proxies**
- `GET /api/v1/proxies` — filtros, paginação, resposta mascarada.
- `GET /api/v1/proxies/{id}`
- `POST /api/v1/proxies`
- `PATCH /api/v1/proxies/{id}`
- `GET /api/v1/proxies/available`
- `POST /api/v1/proxies/import/preview` — sem persistir.
- `POST /api/v1/proxies/import/commit` — confirmar importação autorizada.

**Perfis e alocações**
- `GET /api/v1/profiles`
- `GET /api/v1/profiles/{id}`
- `POST /api/v1/profiles`
- `POST /api/v1/allocations/reserve`
- `POST /api/v1/allocations/{id}/confirm`
- `POST /api/v1/allocations/{id}/release`
- `GET /api/v1/allocations`

**Indicadores e auditoria**
- `GET /api/v1/dashboard/summary`
- `GET /api/v1/dashboard/proxies`
- `GET /api/v1/audit-events` (autorizado)

Aplicar OpenAPI, validação, ProblemDetails, códigos HTTP adequados, paginação consistente, timestamps UTC e idempotência nas mutações repetíveis. **Esses endpoints são planejados, não existentes.** Implementar por fatias verticais.

## Frontend

Usar `src/app`, `src/components/ui`, `src/components/layout`, `src/features/{auth,dashboard,proxies,profiles,allocations,administration}`, `src/lib/api`.

- Interface responsiva e acessível, com estados de loading/erro/vazio.
- TanStack Query para cache e invalidação; TanStack Table com filtros/paginação no servidor quando necessário.
- Formulários com React Hook Form + Zod; backend sempre revalida.
- Não guardar tokens em `localStorage`; usar sessão segura por cookie.
- Não mostrar dados fictícios como se fossem produção; identificar mocks.

## Migração da planilha

Fonte: aba `HTTP - FACEBOOK UNIFICADA`. Campos identificados anteriormente: `PERFIL`, `PLATAFORMA`, `PROXY`, `PORTA`, `USUARIO`/`USUÁRIO`, `SENHA`, `STATUS`, `DATA DE VENCIMENTO`, `REGIÃO`, `ADsPower Lote`.

Não presumir formatos/cabeçalhos idênticos em produção. Criar importação CSV em duas etapas: prévia e confirmação. Validar duplicatas, datas, portas (1–65535), campos vazios e conflitos de vínculo. Mascarar segredos na prévia, limitar tamanho e retenção, usar transação e produzir relatório de aceitos/rejeitados. Não salvar CSV real no Git. Preservar planilha até conciliação e aprovação de desligamento.

## Integrações futuras — fora do MVP inicial

- **n8n hospedado:** consumir API com credencial de serviço, sem acesso irrestrito.
- **AdsPower:** API local nos computadores Windows dos operadores; Railway/n8n remoto não acessa `localhost` ou `host.docker.internal` automaticamente. Exige conector seguro e arquitetura aprovada.
- **Notion:** sincronizar somente metadados operacionais; jamais senhas, cookies, 2FA ou documentos.
- **Claude/MCP:** preparar contratos; implementar depois ferramentas somente leitura, sanitizadas, com autenticação, auditoria e escopos. A conexão MCP existente do Claude não autoriza automaticamente o backend.

## Processo obrigatório para Claude

1. Inspecionar o repositório e versões instaladas antes de escrever código; não inventar arquivos, resultados ou serviços.
2. Explicar brevemente objetivo, riscos, arquivos afetados e plano incremental.
3. Implementar **uma fatia vertical por vez** (domínio → aplicação → infraestrutura → API → testes → UI, quando pertinente).
4. Manter migrations, OpenAPI e documentação sincronizados com mudanças reais.
5. Executar `dotnet build`, `dotnet test`, lint/typecheck/testes frontend conforme disponíveis. Reportar **comandos e resultados reais**, sem afirmar execução inexistente.
6. Solicitar confirmação para alterações de segurança, contratos públicos, dados reais, deploy e operações destrutivas.
7. Evitar refatorações não solicitadas, código morto, dependências sem justificativa e `TODO` sem contexto.
8. Ao concluir, apresentar resumo, arquivos modificados, testes, riscos e próximo passo.
9. Não realizar `git commit`, `push`, deploy ou alterações em produção automaticamente.
10. Comunicar em português do Brasil, objetivamente; perguntar só quando uma decisão impedir progresso.

## Definition of Done

- Regra de domínio testada, inclusive concorrência quando aplicável.
- Autorização e validação verificadas no backend.
- Nenhum segredo exposto em respostas, logs ou artefatos.
- Migration/OpenAPI/documentação atualizadas quando aplicável.
- UI contempla estados de erro, carregamento e vazio quando aplicável.
- Builds/testes executados e reportados; limitações declaradas.

## PRIMEIRA TAREFA — Etapa 0: fundação

**Não conectar a serviços reais nem importar dados nesta etapa.**

1. Verificar `dotnet --info`, `node --version`, `npm --version`, `docker --version` e reportar incompatibilidades.
2. Criar solution e quatro projetos .NET com referências corretas.
3. Criar React + TypeScript + Vite com a stack mínima de UI.
4. Configurar Docker Compose com PostgreSQL local, volume e healthcheck.
5. Criar `.gitignore`, `.env.example`, README de execução local e `docs/decisions/0001-architecture.md`.
6. Implementar `/health` e tela inicial simples marcada como desenvolvimento.
7. Criar testes iniciais, executar builds e testes.
8. **Parar e apresentar relatório antes de implementar Identity, migrations de produção ou importar dados.**

## Etapa atual — Etapa 1 autorizada

A Etapa 0 foi concluída. O usuário autorizou autenticação interna e segurança:
Identity com PostgreSQL, migrations aditivas, cookies seguros, CSRF, lockout,
rate limiting, Admin/Manager/Operator, provisionamento explícito do primeiro Admin,
criação administrativa de contas, auditoria sanitizada e interface autenticada.
As decisões estão em docs/decisions/0002-authentication-security.md.
Continuam fora do escopo CRUD de proxies, JWT sem justificativa, cadastro público,
integrações externas, dados reais, commits e deploys. Não iniciar outra etapa
sem nova autorização. Integration permanece papel futuro, não implementado.

A Etapa 1.1 foi autorizada e homologada localmente, incluindo migration, primeiro
Admin sintético e testes em Chrome real. Resultados e limitação de confiança TLS
estão em docs/etapa-1.1-report.md.

## Etapa atual — Etapa 2.1 autorizada

O usuário autorizou exclusivamente domínio, criptografia AES-256-GCM e persistência
de proxies, migrations aditivas e testes, preservando a autenticação homologada.
Decisões em docs/decisions/0003-proxy-domain-encryption.md. Não implementar endpoints
CRUD nem UI de proxies, alocações, integrações ou importação de dados reais.
Não aplicar a migration ao banco local existente sem nova aprovação explícita.
Sem commits ou deploys; etapas posteriores continuam dependendo de autorização.

## Etapa atual — Etapa 2.2A autorizada

O usuário autorizou a aplicação local condicionada da migration 2.1 e a API de
gestão de proxies: listagem, detalhe, criação, alteração parcial e disponíveis.
Application contém os casos de uso; políticas Operations/Management protegem
leituras/escritas; auditoria de proxies é separada da autenticação. Nenhum frontend,
integração ou dado real autorizado. Migration adicional de auditoria é gerada e
validada em bancos descartáveis e aplicada na homologação após aprovação explícita
e novo backup validado. A API foi reiniciada com a chave DPAPI existente e validada.
Na conclusão da Etapa 2.2A, o frontend ainda dependia de autorização de escopo.
Consulte docs/decisions/0004-proxy-management-api.md e docs/etapa-2.2a-report.md.

## Etapa atual — Etapa 2.2B autorizada

O usuário autorizou a interface React de proxies, consumindo exclusivamente os cinco
endpoints existentes. TanStack Query/Table, React Hook Form/Zod, Tailwind/shadcn/ui,
permissões visuais por papel, respostas mascaradas e CSRF compartilhado. Sem exclusão,
importação, novas APIs ou funcionalidades de integrações. Nenhuma escrita homologada
autorizada; usar mocks ou PostgreSQL descartável para testes de escrita. Validar em
Chrome via Playwright e documentar evidências, resultados reais e limitações.
Consulte docs/decisions/0005-proxy-management-ui.md e docs/etapa-2.2b-report.md.
Sem commits ou deploys. As etapas futuras dependem de nova autorização.

## Etapa atual — Etapa 2.3A autorizada

Backend de análise e pré-visualização CSV autorizado: POST /api/v1/proxies/import/preview,
Admin/cookies/CSRF existentes, UTF-8, limites, regras reutilizadas, duplicatas e consulta
de conflitos somente em leitura. Sem persistência de arquivo/proxies/credenciais,
tokens de confirmação, frontend novo, alterações de layout, integrações ou dados reais.
Nenhuma migration homologada, commit ou deploy autorizado. Testes em bancos descartáveis.
Decisões na ADR 0006 e resultados em docs/etapa-2.3a-report.md. Não iniciar Etapa 2.3B.

## Etapa atual — Etapa 2.3B autorizada

A autorização atual substitui a restrição histórica de confirmação da Etapa 2.3A.
Confirmar CSV reenviado com prova Data Protection vinculada ao Admin/arquivo/região,
reutilizando parser e invariantes. Revalidar conflitos sob transação, criptografar
senhas, manter idempotência por administrador/chave e auditoria sanitizada.
Erros de validação abortam toda a operação; conflitos não sobrescrevem existentes.
Região ausente exige opção explícita validada ou impede a importação.
Nenhuma escrita ou migration homologada autorizada: testes somente em bancos
descartáveis e dados sintéticos. Não alterar layout, criar perfis/alocações/vínculos,
integrar serviços externos, realizar commits/deploys ou iniciar Etapa 2.3C.
Consulte ADR 0007, docs/api/proxy-csv-import.md e docs/etapa-2.3b-report.md.

## Etapa atual — Etapa 2.3C autorizada

Backend 2.3B homologado; não reaplicar migrations. Interface de importação Admin
autorizada em /proxies/import, preservando layout, cliente HTTP, cookies e CSRF.
Reenvio multipart, prova exatamente conforme contrato, região explicitamente
informada, UUID idempotente por operação e confirmação humana antes da escrita.
Arquivo/prova somente em memória; sem storage ou logs sensíveis. Frontend não
substitui validação do backend nem exibe valores de região ausentes do contrato.
Testes com mocks ou bancos descartáveis; nenhuma escrita homologada/dado real,
integração, commit, deploy ou alteração de chave autorizada. Avaliação manual do
usuário ainda necessária para homologação da interface. Não iniciar outra etapa.
Consulte docs/etapa-2.3c-report.md e docs/ui/csv-import.md.

Compatibilidade CSV aprovada pelo usuário: manter datas sem ano inválidas e exigir
ano no arquivo; não inferir ano. Aceitar duplicatas de cabeçalho reconhecido só
com ocorrências vazias e mapeamento inequívoco. Opção explícita para ignorar linhas
sem qualquer dado de conexão, vinculada à prova, mantendo aviso/linha no relatório
e auditoria existente. Metadados de perfil/plataforma/lote não são importados.
Sem migrations, dados reais, alterações homologadas ou mudança de criptografia.

## Etapa 2.3C.1 — Ano de referência autorizado

A decisão anterior de exigir sempre ano no CSV foi substituída pelo usuário:
referenceYear opcional, vazio inicialmente, permite interpretar somente dd/MM
com ano explicitamente escolhido. Datas completas preservadas; sem opção, rejeitar
dd/MM. Validar calendário e vencimento com regras existentes; ano participa da
prova protegida e fingerprint idempotente. Interface mostra vencimento em UTC.
Preservar região complementar, linhas sem conexão, layout e chaves existentes.
Não aplicar migrations, gravar na homologação ou importar dados reais. Testes de
escrita somente descartáveis. Aguardar homologação manual; sem commits/deploys.
Consulte docs/etapa-2.3c.1-report.md e os contratos CSV atualizados em docs/api.

## Etapa 2.4A — Domínio de perfis e alocações autorizado

Somente Domain, testes unitários e documentação. Perfis com status independente;
reservas/confirmações exclusivas por proxy e perfil; encerramento finaliza vínculo;
histórico preservado e proxy reutilizável somente se operacional e válida.
Sem endpoints, persistência, migrations, UI ou integrações. Preservar autenticação,
criptografia, auditoria e importador. Consulte ADR 0008 e docs/etapa-2.4a-report.md.
Índices únicos parciais e transações ficam documentados para persistência futura;
não afirmar garantia de concorrência por validação apenas em memória.
Não alterar dados homologados, realizar commits/deploys ou avançar automaticamente.

## Etapa 2.4B.1 — Persistência autorizada

A restrição histórica de persistência da Etapa 2.4A foi substituída pelo usuário:
mapear perfis/alocações, contratos Application e repositories Infrastructure,
transações e índices únicos parciais nos dois lados. Gerar/revisar migration e
testar em PostgreSQL descartável. Não aplicar no banco homologado nem alterar
registros existentes de proxies. Sem endpoints, frontend, alteração de criptografia,
integrações, commits ou deploys. Consulte ADR 0009 e docs/etapa-2.4b1-report.md.
Não iniciar Etapa 2.4B.2 automaticamente.

## Etapa 2.4B.2 — APIs autorizadas

Application e endpoints versionados de perfis/alocações: Admin/Manager escrevem,
Operator consulta. Reutilizar domínio/transações, auditar atomicamente e excluir
vínculos ativos da disponibilidade. Sem frontend, integrações ou escritas homologadas.
Migration adicional de auditoria apenas gerada/revisada e aplicada nas fixtures.
Preservar AES/DPAPI, autenticação e contratos CSV/proxies. Não reiniciar homologação
com migrations pendentes. Consulte docs/etapa-2.4b2-report.md e contratos em docs/api.
Aguardar revisão antes de próxima etapa; sem commits/deploys.

## Etapa 2.4B.3 — Confiabilidade autorizada

Reservas com TTL configurável de 15 minutos por padrão, recuperação transacional
sob demanda e auditoria de expiração. Leituras não gravam e desconsideram reservas
vencidas na disponibilidade. Confirm repetido de Confirmed é idempotente; expirado
não confirma. Falha interna de auditoria retorna 500 sanitizado com rollback.
Preservar índices/histórico/criptografia. Sem migrations ou escritas homologadas,
frontend, background desnecessário ou integrações. Consulte ADR 0010 e relatório
docs/etapa-2.4b3-report.md. Aguardar revisão; sem commits/deploys/próxima etapa.

## Etapa 2.4C — Interface de perfis e alocações autorizada

Backend 2.4B homologado; migrations já aplicadas, não reaplicar. UI React de listagem,
cadastro, detalhes, reserva/confirmação/liberação e histórico, consumindo somente
contratos existentes. Preservar layout e compartilhar autenticação/cookies/CSRF/HTTP.
Admin/Manager escrevem; Operator consulta. Histórico paginado não informa sozinho
a alocação atual. Prazo vem da API; não assumir Active como disponível nem duplicar
exclusividade no cliente. Liberação exige confirmação humana; sem retries automáticos.
Validar com mocks e PostgreSQL descartável, preservar dados/chaves homologados.
Aguardar avaliação manual antes de encerrar etapa; sem integrações/commits/deploys
ou próxima etapa. Consulte docs/etapa-2.4c-report.md e docs/ui/operational-profiles.md.

## Etapa 2.4C.1 — Backend do grid operacional autorizado

OperationalProfile possui platform1/platform2 opcionais do catálogo aprovado;
platform legado preservado sem conversão implícita. Grid com uma linha por proxy,
LEFT JOIN apenas de vínculos efetivos, filtros SQL e snapshot readonly coerente.
Disponibilidade: Available/Reserved/InUse/Unavailable; Active não equivale a livre.
Contrato GET /api/v1/proxies/operational-grid. PATCH null limpa somente slots novos;
omissão preserva. Reservas/histórico/cripto/auditoria existentes continuam intactos.
Migration gerada/revisada, não aplicada na homologação. Não reiniciar nova API com
schema pendente; escritas de teste somente descartáveis. Sem frontend, recuperação
CSV, integrações, commits ou deploys. Aguardar aprovação antes de migration.
Consulte docs/etapa-2.4c1-report.md e docs/api/operational-grid.md.

Homologação posterior autorizada: AddProfileAssignments aplicada exclusivamente
no PostgreSQL local em 12/10/2026, com backup/verificação de schema/dados/chaves.
Não reaplicar. API 2.4C.1 disponível, 131 proxies preservadas; testes de escrita
somente descartáveis. Consulte docs/etapa-2.4c1-homologacao-report.md e aguarde
revisão. Etapa 2.4C.2/frontend unificado exigia autorização posterior.

Etapa 2.4C.2 autorizada posteriormente: frontend operacional único em /proxies,
GET /api/v1/proxies/operational-grid, TanStack Query/Table, filtros e paginação
no servidor. Disponibilidade sob autoridade da API; perfis/CSV são administração
secundária. Preservar cookies, CSRF, ações e histórico. Sem backend/migrations,
escritas homologadas, integrações, commits ou deploys. Testes com mocks.
Resultados em docs/etapa-2.4c2-report.md; aguardar avaliação manual, sem 2.4C.3.

Etapa 2.4C.3 autorizada explicitamente depois: backend separado de recuperação
Admin, parser limitado compartilhado e CSV reenviado com prova vinculada ao plano.
Host canônico/porta precisam de correspondência exata e única. Não usar ordem,
aproximação, senha ou nome como identidade global. Colunas PLATAFORMA até duas
ocorrências preenchem slots por posição, sem atribuições artificiais. Duplicatas,
ambiguidades, perfis existentes sem vínculo comprovadamente idêntico e vínculos
conflitantes bloqueiam o lote. Associação confirmada representa o momento atual,
não histórico inferido. Transação, auditoria e recibo durável; replay sem efeitos.
Não alterar/recriar proxies ou credenciais. Migration adicional somente gerada,
revisada e aplicada em fixtures descartáveis; não reiniciar nova API na homologação.
Sem recuperação/CSV real, escrita homologada, frontend, mudança de chave, integração,
commit ou deploy. Aguardar aprovação separada. Consulte ADR 0012 e relatório 2.4C.3.

Migration 20261012131756_AddProfileRecoveryReceipts aplicada em retomada autorizada
da homologação local: não repetir. 131 proxies, dados anteriores e chaves preservados;
API nova iniciada e 416 testes aprovados. Smoke auxiliar autenticado interrompido
por compilação CS0119, sem requisições desse harness. Homologação funcional integral
ainda pendente. Aguardar autorização de retomada; não executar recuperação real.
Consulte docs/etapa-2.4c3-homologacao-report.md. Sem commits/deploys/integrações.

Retomada final autorizada: harness corrigido com System.IO.File explícito;
smoke HTTPS autenticado, CSRF/rejeições, 416 testes e integridade aprovados.
Homologação técnica 2.4C.3 concluída e pronta para prévia sem gravação via endpoint
dedicado de recuperação. Aguardar revisão; original ainda não enviado e commit
real exige autorização própria. Não reaplicar/reverter migration ou mudar chaves.

Recuperação 2.4C.3 — cópia filtrada preparada em 12/10/2026: linhas 22–23 (Perfil 1218,
pendência operacional, sem vínculo escolhido) e 131 (conexão vazia) excluídas somente
da cópia Downloads\HTTP - FACEBOOK UNIFICADA - recuperacao-129.csv (SHA-256 30DF1F96…).
Prévia read-only: 129 propostas, 0 conflitos/inválidos/ignorados, 129 pareamentos
idênticos ao original. INFOPRODUTO ainda pendente: aplicar migration, reiniciar API e
confirmar exigem autorização explícita. Consulte docs/etapa-2.4c3-filtered-preview-report.md.
Recuperação 2.4C.3 CONCLUÍDA em 12/10/2026 (autorizada): migration INFOPRODUTO aplicada
(checks corrigidos após incidente de encoding), API reiniciada, recoveryId ce98b7ed…:
129 perfis + 129 alocações Confirmed, 131 proxies inalteradas, Perfil 1218 pendente.
Não repetir migration nem commit. Ver docs/etapa-2.4c3-filtered-preview-report.md.

## Etapa 2.5 — Ações rápidas no grid autorizada (12/10/2026)

Dados do banco local (131 proxies, 129 perfis recuperados) são NÃO AUTORITATIVOS:
divergem do AdsPower e aguardam validação com o gestor. Tratá-los como dados de teste:
escritas operacionais no PostgreSQL local (criar/editar perfis, reservar, confirmar,
liberar) estão autorizadas para desenvolvimento e validação. Continuam proibidos sem
autorização: migrations, troca de chaves/DPAPI, exposição de senhas de proxy (mesmo
em testes, telas, logs ou prints), integrações externas, commits e deploys.
Backup pré-recuperação para restauração: .local/backups/etapa-2.4c3-recovery-20261012T181818Z.
Escopo: no grid /proxies, vincular perfil a proxy disponível em um passo, liberar
pela linha com confirmação e editar Plataforma 1/2 diretamente, reutilizando
domínio, transações, idempotência, CSRF e auditoria existentes.

## Etapa 2.6 — Planilhas no menu lateral (12/10/2026)

O sistema substitui a planilha inteira (várias abas). Cada aba vira uma entrada do
menu lateral (grupo "Planilhas", lista em components/layout/sheets.ts) e um módulo.
Hoje só existe HTTP - FACEBOOK UNIFICADA (rota /proxies mantida, sem redirecionamentos;
título da tela com o nome da aba). Demais abas: aguardar os nomes do usuário.
Integração n8n: o n8n também roda local; API própria (credencial de serviço, vínculo
em uma chamada) fica para depois, sem Google Sheets. Deploy Railway só mais adiante.
Injeção de novos perfis pelo frontend: etapa futura.

Ajuste 12/10/2026: a aba ativa passou a se chamar apenas "HTTP - FACEBOOK" (o sufixo
UNIFICADA existia por causa de abas antigas de Facebook). Rota /proxies inalterada.
Abas futuras no menu (Em breve): HTTP - TIKTOK,
HTTP - GOOGLE ADS, NUMEROS - OPERAÇAO, NUMEROS - FACEBOOK; estrutura de dados a definir.
"HTTP - FACEBOOK (ATUAL PROXIES)" e a antiga "HTTP - FACEBOOK UNIFICADA" sao a mesma aba: unica entrada HTTP - FACEBOOK.

Etapa 2.7 — Modo escuro e novo login (12/10/2026, frontend apenas): tema claro/escuro
por variáveis de cor em src/styles.css (:root.dark), segue o sistema e guarda a escolha
no navegador (central-theme); botão no cabeçalho e no login. Login em tela dividida com
ilustração decorativa (sem dados reais). Cores que mudam de papel usam tokens
(bg-surface, text-link, bg-ink); não usar bg-white/text-sky-800 em telas novas.

Etapa 2.8 — Dashboard AdsPower (12/10/2026, leitura apenas): autorizada a leitura da API local
do AdsPower (127.0.0.1:50325) para o Dashboard (/dashboard): GET /api/v1/adspower/dashboard e
POST .../refresh. Sem migration e sem persistir perfis; resultado em memória. username/password/
remark/IP da proxy do AdsPower são descartados na leitura. Chave via scripts/Save-LocalAdsPowerKey.ps1
(DPAPI em .local/secrets) e scripts/Set-LocalAdsPower.ps1; nunca em arquivos do repositório; a chave
foi exposta em conversas e deve ser trocada. Escopo: perfis por grupo e tag, busca/ordenação no navegador, data de criação do perfil; sem ativo/inativo; criador não existe na API.
Sem escrita no AdsPower. Ver docs/api/adspower-dashboard.md.

Etapa 2.5 — Ingestão de perfis pelo n8n (12/10/2026): API /api/v1/integration/profile-ingestions
(availability, reserve, confirm, release) com chave de serviço (hash na configuração). Migration
20261012230807_AddProfileIngestion JÁ APLICADA localmente (10 migrations); não reaplicar. Senha de proxy
é devolvida somente na reserva, só das proxies do lote (decisão do usuário). Entrega parcial com motivo
NoProxyAvailable. API escuta em 0.0.0.0:7090 para o Docker do n8n. Ver docs/etapa-2.5-ingestion-report.md,
docs/api/profile-ingestion.md e docs/n8n/README.md. Sem Google Sheets no fluxo novo.

Revisão 13/10/2026 da ingestão: numeração e criação só no grupo do AdsPower do modo ativo (Test = "FB Teste | YAGO", Production = "03 ESTEIRA DE PERFIL"),
próximo = maior "Perfil N" DO GRUPO + 1 (lido no AdsPower), sem olhar outros grupos nem perfis da Central; nomes repetidos entre grupos são aceitos. Migration
20261013105059_AddIngestionBatchGroup JÁ APLICADA localmente (11 migrations); não reaplicar. Padrão é Test; produção exige ProfileIngestion:Mode=Production.


Etapa 2.9 — Envio de TXT pelo site (13/10/2026): tela /proxies/ingestion (Admin/Manager) envia TXT à Central (POST /api/v1/profile-ingestions/submit, GET .../status) que repassa só pela memória ao webhook do n8n (fluxo "P6 - 02 - INGESTAO DE PERFIL (SITE)", X-Central-Key, N8nIngestion:WebhookUrl/WebhookKey via scripts/New-LocalN8nWebhookKey.ps1 e Set-LocalN8nWebhook.ps1). Arquivos nunca gravados nem logados; um envio por vez; relatório filtrado por lista de campos. Liberação do lote inteiro: POST /api/v1/integration/profile-ingestions/batches/{id}/release. Sem migration. Chave do AdsPower: o usuário trocará ao final da validação. Ver docs/api/profile-upload.md e docs/n8n/README.md.

Ajuste 14/10/2026 (frontend): a aba HTTP - FACEBOOK virou grupo com submenus Proxies (/proxies, /proxies/import) e Perfis
(/profiles, /profiles/:id, /proxies/ingestion), definidos em components/layout/sheets.ts (campo children). Rotas inalteradas.
No grid ficam só Atualizar, Importar CSV (Admin) e Novo proxy; "Novos perfis (TXT)" mora na tela Perfis. Marca oficial: "Chegou Contingência" (src/lib/brand.ts).

Submenu Aquecimento (14/10/2026): reservado em HTTP - FACEBOOK como "Em breve", sem tela, para o futuro agente de aquecimento.

## Etapa 2.10 — Aquecimento = verificação de impedimentos (16/10/2026)

Autorizado pelo usuário em 16/10/2026, com escopo RESTRITO. "Aquecimento" é só o nome comercial
usado com clientes e no menu (mantido a pedido do gestor); o que o sistema executa é uma
VERIFICAÇÃO somente leitura de impedimentos em perfis do AdsPower: no máximo uma por perfil por
dia UTC, em esteira de 7 dias. A verificação abre o perfil, carrega UMA página, lê o estado
(normal, CAPTCHA, verificação, login, confirmação de identidade, checkpoint, restrição, erro de
navegação) e registra o resultado. Fica fora do escopo e continua proibido: rolagem, Reels,
sessões cronometradas, curtidas, comentários, mensagens, solicitações ou qualquer interação,
bem como simular comportamento humano. Com impedimento: parar, não contornar, não repetir e
deixar o perfil aberto para análise. Sem impedimento: fechar o perfil pelo AdsPower.
Backend (domínio, API /api/v1/warmup, tabela profiles.HealthChecks) implementado; migration
20261016175350_AddProfileHealthChecks apenas GERADA, não aplicada (aguardar autorização).
Pendentes, cada um com autorização própria: UI no submenu Aquecimento e disparo do agente em
segundo plano (depende de definir como o AdsPower IA Agent é invocado). Ver ADR 0015 e
docs/warmup/verification-prompt.md.

Aquecimento — tela (16/10/2026, frontend apenas): /warmup lista os perfis do grupo "FB | ESTEIRA DE PERFIS"
(leitura existente GET /api/v1/adspower/dashboard) com caixa de seleção (até 20) e botão que COPIA o prompt de
verificação; nada é executado nem gravado pela tela. Sem migration aplicada, sem disparo do agente.

## Etapa 2A — Preparação para Railway (21/10/2026)

Autorizada: preparar a API para contêiner Linux/Railway sem deploy. Frontend servido pela API (mesma origem),
proxy reverso com lista explícita, Data Protection no PostgreSQL cifrado por certificado, validação de configuração
de produção, integrações locais (AdsPower/n8n) isoladas por LocalIntegrations:Enabled, /health/ready, Dockerfile e
.dockerignore. Migration AddDataProtectionKeys apenas gerada (não aplicada em banco existente); a API nunca migra sozinha.
Sem worker/tarefas (Etapa 2B não iniciada), sem deploy, sem segredos. Ver ADR 0016 e docs/RAILWAY_DEPLOYMENT.md.

## Etapa 2B — Fila de tarefas e worker (21/10/2026)

Autorizada: fila de tarefas em PostgreSQL (work.Tasks, audit.WorkTaskEvents), API /api/v1/tasks (criar, consultar, listar,
cancelar; Admin/Manager escrevem, Operator lê) e projeto Worker independente com executor SIMULADO. Lease, heartbeat, claim com
SKIP LOCKED, retry com backoff, dead-letter, recuperação e cancelamento cooperativo. Payload fechado e versionado: nunca
credenciais, cookies ou tokens. Migration AddWorkQueue apenas GERADA (não aplicar em banco existente sem autorização).
Sem AdsPower, navegador, agentes externos ou deploy. Worker acessa o banco direto (somente local); canal HTTPS de saída é a
Etapa 2C, não iniciada. Ver ADR 0017 e docs/etapa-2b-report.md.

## Etapa 2C — Canal HTTPS entre a Central e o worker (21/10/2026)

Autorizada: worker remoto (Windows) que fala somente HTTPS com /api/v1/agent/*, sem conexão com o PostgreSQL. Identidade própria por
executor (work.Executors), credencial individual ce1.<ExecutorId>.<segredo> (só o hash é guardado; exibida uma vez; rotação/revogação
imediatas), claim restrito aos tipos permitidos, lease/idempotência/auditoria reutilizando WorkTask, recuperação de leases pela API,
telas Tarefas e Executores. Worker é Remote por padrão; LocalDatabase (2B) só em Development. Migration AddExecutors apenas GERADA
(depende de AddWorkQueue; não aplicar em banco existente sem autorização). Somente SimulatedProcessing: sem AdsPower, Claude, navegador
ou redes sociais; sem deploy. Execução com processos reais via HTTPS pendente de certificado confiável. Ver ADR 0018 e docs/etapa-2c-report.md.

## Etapa 2C.1 — Homologação HTTPS e segurança pré-deploy (21/10/2026)

Autorizada: homologar API e worker em processos separados por HTTPS (sem desabilitar TLS), freio de credenciais de serviço inválidas
(executores e chave do n8n; 429 sem consultar o banco; IP real só via proxy confiável), canal do agente fora do limite global por IP,
claim idempotente (Idempotency-Key → WorkTask.ClaimKey), recuperação de leases pela API verificada sem tráfego, validação da sequência de
migrations e de upgrade. Nova migration AddClaimKey apenas GERADA. Certificado de desenvolvimento confiável no Windows (nada alterado);
Worker:Api:TrustedCaFile confia numa CA privada só no processo do worker. Sem deploy, AdsPower ou Claude. Ver ADR 0019,
docs/etapa-2c1-report.md e docs/HTTPS_HOMOLOGATION.md. Staging na Railway: pronto condicionado; produção: não recomendado ainda.

## Etapa 2D — Preparação e homologação no Railway (Fase 1, 21/10/2026)

Fase 1 concluída SEM deploy: Dockerfile com ENTRYPOINT exec e PORT lida pela API, railway.json (health check /health/ready, sem pré-deploy),
comando controlado --migrate [--plan] (confirmação do alvo + trava contra script destrutivo), sonda de rede /diagnostics/network (só com
Diagnostics:NetworkProbeToken), /health* isento de redirecionamento HTTPS, AllowedHosts deve incluir healthcheck.railway.app. Faixa de IP do
proxy do Railway NÃO documentada: não presumir; medir com a sonda. A Fase 2 (criar recursos no Railway, deploy, homologação) exige autorização
explícita. Ver docs/etapa-2d-predeploy.md.
