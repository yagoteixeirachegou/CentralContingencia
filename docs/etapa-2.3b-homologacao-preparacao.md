# Preparação da homologação — Etapa 2.3B

Data: 11/10/2026. Migration não aplicada. Nenhuma importação ou alteração de dados.
AGENTS.md, CLAUDE.md, relatório 2.3B, migration completa (Up/Down) e SQL lidos.

## Revisão e estado atual

Migration alvo: `20261011165123_AddProxyImportBatches`.
Up/SQL criam apenas audit.ProxyImportBatches, audit.ProxyImportLines, três índices,
PKs, checks e FKs; SQL insere a identificação na __EFMigrationsHistory dentro de
transação. Não há DROP, UPDATE, DELETE, TRUNCATE ou alteração de colunas existentes.
Down remove as duas tabelas novas: é destrutivo para os recibos e não foi executado.
FK ProxyId usa RESTRICT; cascade ocorre somente de lote para suas linhas, nunca
para proxies. ActorId não cria FK para Identity, preservando independência do módulo.

Banco confirmado: central_contingencia_dev, Docker PostgreSQL 17.11 saudável,
porta exposta exclusivamente 127.0.0.1:55432. Aplicadas:

- 20261011015548_InitialIdentityAndAuthenticationAudit
- 20261011024141_AddProxyPersistence
- 20261011025452_AddProxyAudit

As duas novas tabelas ainda não existem; a migration alvo permanece pendente.
Contagens: 4 usuários, 32 eventos de autenticação, 1 proxy e 1 evento de proxy.
Nenhuma constraint não validada ou índice inválido/não pronto encontrado.
Estas verificações de catálogo não equivalem a uma prova completa de integridade
física. pg_amcheck não verificou relações porque a extensão amcheck não está
instalada; não foi instalada para evitar alteração não autorizada.

## Criptografia

Código AES-256-GCM existente preservado: chave de 32 bytes, nonce de 12 bytes,
tag de 16 bytes e associated data vinculada ao UUID/KeyId. Registro existente
usa development-v1 e comprimentos compatíveis. Leitura DPAPI pelo perfil Windows
atual e comprimento da chave foram verificados sem imprimir material secreto.
14 testes unitários filtrados por Encryption passaram, sem falhas/ignorados,
usando dados/chaves sintéticos. Não foi descriptografada a senha existente.

Data Protection mantém ApplicationName CentralContingencia.P6; propósitos novos
de prova/fingerprint são separados dos cookies e não alteram o keyring existente.
Migration não toca credenciais, envelopes, Identity ou chaves. Não rotacionar nem
recriar chaves durante aplicação/reinício. A tentativa de teste AES diretamente no
Windows PowerShell não executou, pois esse runtime não disponibiliza AesGcm;
a validação sintética foi executada pelo runtime .NET nos testes automatizados.

## Backup verificável

Diretório local ignorado pelo Git, ACL restrita ao usuário atual e SYSTEM:
`.local/backups/etapa-2.3b-pre-homologacao-20261011-143327/`.

Contém database.dump (custom pg_dump, 26736 bytes), cópia da chave AES protegida
DPAPI e dois arquivos do keyring Data Protection, mais manifest.json com tamanhos
e SHA-256. Todos os hashes foram recalculados e conferidos. pg_dump concluiu;
pg_restore --list e pg_restore --file=/dev/null concluíram, lendo integralmente o
archive sem imprimir dados ou executar SQL. Arquivo temporário no container removido.
Não houve restauração de ensaio; validação de leitura/checksum não garante por si
só restauração completa. Backup permanece local, sem cópia externa/off-machine.
O dump contém dados de homologação e hashes de Identity: manter acesso restrito.
DPAPI exige o mesmo perfil Windows; os arquivos sozinhos não substituem seu backup.

## Impacto e execução proposta — aguardando aprovação

Criação de tabelas vazias e índices pequenos; FK pode exigir bloqueio breve de
metadados na tabela Proxies. Janela sem escritas concorrentes recomendada.
Não executar importações durante aplicação. O SQL não é idempotente para tabelas
preexistentes; conferir novamente migrations/objetos imediatamente antes.
Se houver escritas desde o backup, gerar novo backup antes da aplicação.

Comandos propostos, não executados:

```powershell
. ./scripts/Set-LocalDatabase.ps1
dotnet ef database update 20261011165123_AddProxyImportBatches --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure
```

Após autorização: conferir novamente destino/backup, aplicar somente o alvo,
comparar contagens/tabelas/constraints/history, reiniciar API com a mesma chave
DPAPI e preservar keyring, verificar health/autenticação/contrato. Escritas de
importação na homologação exigem autorização própria; não utilizar CSV real.

## Plano de recuperação

1. Falha antes do COMMIT: transação do SQL desfaz criação/history. Confirmar
   estado e manter API anterior; não executar Down automaticamente.
2. Falha da API após migration: interromper novas escritas e voltar ao binário
   anterior, conservando tabelas novas e chaves; migration aditiva permite essa
   recuperação sem remover dados.
3. Se necessário recuperar dados: obter autorização, preservar backup do estado
   pós-incidente, restaurar database.dump em banco isolado e conferir schema,
   migrations e contagens antes de planejar substituição do banco homologado.
   pg_restore deve usar conexão protegida, --exit-on-error e --single-transaction;
   nenhum comando de DROP/--clean deve ser executado automaticamente.
4. Recuperação criptográfica exige também keyring e arquivo DPAPI no perfil
   Windows apropriado. Validar hashes e não gerar chaves substitutas.
5. Down só seria considerado após aprovação e avaliação dos recibos: remover
   idempotência/auditoria pode permitir reprocessamentos e perder evidências.

Riscos operacionais posteriores: lock de tabela durante importação limita escritas;
timeout de cinco segundos e recibos dependentes de conservação do keyring.
Nenhum commit, deploy, redesign ou reinício de serviços realizado nesta preparação.
