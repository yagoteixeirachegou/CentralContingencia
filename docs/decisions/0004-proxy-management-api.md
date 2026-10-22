# ADR 0004 — API de gestão de proxies

Status: aceita para a Etapa 2.2A.

## Arquitetura e autorização

ProxyManagementService fica na Application, com IProxyStore específico para as
operações necessárias. Não expõe IQueryable/DbContext. Infrastructure implementa
EF/Npgsql, projeção e gravação. API recebe HTTP e traduz falhas para ProblemDetails.
Domain adiciona UpdateDetails, validando tudo antes de alterar o agregado, e
ProxyAuditEvent; Identity e suas tabelas/eventos permanecem preservados.

Operations (Admin/Manager/Operator) permite GET/listagem/detalhe/disponíveis.
Management (Admin/Manager) permite POST/PATCH. O caso de uso também verifica o ator
e papéis informados pela identidade autenticada, antes de acessar dados/criptografia.
Operator não altera credenciais nem estado. Integration não está implementado.

## Contrato e segredos

Os cinco endpoints estão em docs/api/proxies.md. Respostas projetam somente UUID,
host, porta, usernameMasked constante ***, estado, região, validade, timestamps e
available. Não têm senha, ciphertext, nonce, tag ou KeyId. Listagem projeta no SQL
sem carregar as colunas de credenciais; detalhe/mutação carregam o agregado cifrado,
mas nunca descriptografam. Password é writeOnly no schema OpenAPI de entrada.
Objetos de requisição têm ToString redigido; não registrar corpos/configuração.

Somente senha é secreta e cifrada em AES-256-GCM nesta modelagem; username permanece
metadado legível no banco, conforme ADR 0003, e sempre mascarado nas respostas.
Chave sob demanda: GET e alterações sem senha funcionam sem chave; criação/troca
de senha falham fechadas com 503 quando a configuração criptográfica falta.

PATCH recebe objeto JSON parcial (não JSON Patch/merge-patch). Campos ausentes
preservam valor; null, campos desconhecidos, duplicatas inclusive com capitalização
diferente, objeto vazio e tipos incompatíveis são recusados com 400. Não permite
troca de UUID/timestamps internos. Validação estrutural ocorre após autorização/CSRF,
antes do binding, sem mensagens que ecoem valores. Corpo máximo Kestrel 16 KiB;
buffer padrão em memória comporta esse limite. Não habilitar logs de corpos.
Datas de entrada exigem offset explícito/Z, formato interpretável por DateTimeOffset.
Nova validade ou alteração explícita deve estar no futuro. É possível suspender ou
desabilitar um registro já vencido sem mudar sua validade. Senha mantém limite de
1024 bytes UTF-8 da ADR 0003; host/porta/usuário/região usam invariantes anteriores.
Estados aceitos: Active, Suspended e Disabled, strings exatas; padrão criação Disabled.

Paginação: page 1–10000, pageSize 1–100 (padrão 20). Filtros exatos normalizados de
host, porta, região e status; expiresBefore inclusivo. Ordenação só por whitelist,
asc/desc, com UUID como desempate. Não há SQL dinâmico de entrada nem filtro por
senha/usuário. Count + página são duas consultas: o total pode mudar entre elas
com escritores concorrentes. Offset limitado não substitui future cursor pagination
para volumes grandes.

Disponível exige Active e validade temporal. Não há tabela/alocação nesta etapa:
ausência de alocação é verdadeira no sistema atual. O futuro módulo precisa
acrescentar NOT EXISTS Reserved/Confirmed ao store e passar a informação correta
para a avaliação do domínio. Não representa conectividade nem garantia de reserva.

## Auditoria, transações e concorrência

audit.ProxyEvents guarda somente Id, ActorId, ProxyId, Action Created/Updated,
OccurredAt e CorrelationId. Sem payloads/diffs/host/username/password. Sem FK com
cascade: histórico permanece independente da futura remoção de usuários/recursos.
Mesmo SaveChanges grava proxy e evento dentro da transação EF; falha de gravação,
nonce duplicado ou conflito não produz auditoria de sucesso órfã.

Usamos xmin (coluna nativa PostgreSQL) como concurrency token shadow no EF;
não adicionamos coluna física ao agregado. Duas requisições que leram a mesma
versão não sobrescrevem silenciosamente uma à outra durante a gravação: perdedora
recebe 409 e precisa reler/repetir conscientemente. Não há ETag/If-Match: essa proteção
não detecta formulários antigos que só enviam uma alteração depois da outra terminar.
Violação única também vira 409, inclusive nonce duplicado; não repetir envelope
criptográfico recusado. Não há retry automático de criação nem deduplicação de POST:
repetir POST confirmado cria outro UUID. Idempotência requer contrato próprio antes
de importações/alocações; clientes não devem repetir POST após resposta ambígua.

## Segurança HTTP e migrations

Cookies/HTTPS/CSRF existentes preservados. Middleware e OpenAPI agora incluem PATCH
na exigência X-CSRF-TOKEN. Escritas usam rate policy sensitive existente (10/min/IP);
todas as chamadas continuam sujeitas ao limite global. Nenhum CORS novo.
400/401/403/404/409/415/429/503 são documentados; erros internos seguem handler genérico.

Migration 2.1 foi aplicada ao banco homologado após conexão local, backup e revisão
aditiva. AddProxyAudit cria somente tabela/índices de auditoria e histórico EF; não
altera Identity nem registros existentes. Foi aplicada em bancos descartáveis nos
testes e, depois, no banco homologado após aprovação específica e novo backup validado.
Startup não migra. A chave DPAPI existente foi carregada na API local sem divulgar valores.

## Referências

- ADRs 0001, 0002 e 0003.
- [Npgsql — concurrency tokens/xmin](https://www.npgsql.org/efcore/modeling/concurrency.html).
