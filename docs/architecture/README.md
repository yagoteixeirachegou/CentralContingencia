# Arquitetura

Domain contém o evento sanitizado de auditoria e o agregado Proxy com value objects,
invariantes e disponibilidade, sem EF/HTTP ou criptografia concreta.
Application define contratos, DTOs, papéis/políticas e IProxyPasswordProtector, sem EF.
Infrastructure implementa Identity, EF Core/Npgsql, provisionamento e AES-256-GCM.
O DbContext existente mantém identity/audit e adiciona proxies.Proxies, sem alterar
as tabelas de autenticação. Não há aplicação automática de migrations.
Api é o composition root, define transporte HTTP, CSRF, autorização e rate limiting.
Web mantém sessão em memória e usa cookie HttpOnly via proxy de mesma origem.

As referências permitidas continuam verificadas por ArchitectureTests.
IdentityUser é detalhe de infraestrutura; não atravessa o contrato da Application.
Proxies possui domínio, casos de uso Application, store EF, criptografia, auditoria
e cinco endpoints REST autorizados na Etapa 2.2A. A Etapa 2.2B adiciona a feature
React de proxies, TanStack Query/Table e formulários RHF/Zod, sem mudar o backend.
O cliente HTTP compartilhado mantém cookies/CSRF e suporta PATCH/respostas 201.
O cache é separado por identidade/papéis e descartado na troca de sessão.
Profiles e Allocations continuam sem implementação. Disponibilidade exige informação
externa sobre alocação; a função de domínio não reserva nem controla concorrência.
Consulte as ADRs 0001, 0002 e [0003](../decisions/0003-proxy-domain-encryption.md),
o [guia de chaves](../security/proxy-encryption.md) e CLAUDE.md.
Consulte também a [ADR 0004](../decisions/0004-proxy-management-api.md) e
o [contrato HTTP de proxies](../api/proxies.md).
Decisões da UI em [ADR 0005](../decisions/0005-proxy-management-ui.md) e
operação local no [guia de proxies](../ui/proxies.md).

Etapa 2.3A: Application/Proxies/Import analisa CSV e produz somente DTOs mascarados;
Infrastructure/EfProxyImportConflictReader projeta host/porta em leitura, sem
selecionar credenciais. Api utiliza MultipartReader com limites e sem buffering
em disco. Invariantes extraídas para ProxyRules/ProxyInputRules são reutilizadas
pelo agregado, CRUD e prévia. Decisões na [ADR 0006](../decisions/0006-proxy-csv-preview.md).

Etapa 2.3B: o mesmo analisador alimenta a confirmação Application; Infrastructure
implementa prova Data Protection e transação EF/PostgreSQL com bloqueio de escrita.
Domain define lote/resultados sanitizados; audit.ProxyImportBatches/ProxyImportLines
guardam idempotência e auditoria, sem CSV. [ADR 0007](../decisions/0007-controlled-csv-import.md).

Etapa 2.4C.3: Application/Profiles/Recovery reutiliza o parser limitado, invariantes
e prova protegida. Infrastructure implementa snapshot readonly, transação e recibos
separados de recuperação. Novos perfis/alocações usam o domínio existente; não há
alteração de proxies/credenciais. [ADR 0012](../decisions/0012-controlled-profile-recovery.md).
