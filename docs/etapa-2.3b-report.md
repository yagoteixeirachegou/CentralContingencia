# Relatório — Etapa 2.3B: Persistência Controlada da Importação CSV

Data: 11/10/2026. Escopo: backend, dados sintéticos e bancos descartáveis.
AGENTS.md, CLAUDE.md e o relatório 2.3A foram lidos antes da implementação.

## Entrega

- POST `/api/v1/proxies/import/commit`, exclusivo Admin, com autenticação por
  cookies, CSRF e limites/rate limiting existentes. OpenAPI atualizado.
- Reenvio de CSV com prova stateless Data Protection emitida pela prévia, vinculada
  ao administrador, bytes e região explícita, com validade de 15 minutos.
- Parser, validações e consulta de conflitos da Etapa 2.3A reutilizados; nenhuma
  linha enviada pelo frontend é considerada prova de validação.
- Região obrigatória preservada. `defaultRegion` opcional, explícito e validado
  pelo domínio, preenche somente células vazias/ausentes; não existe default implícito.
- Senhas criptografadas com o AES-256-GCM existente antes da persistência.
- Idempotência por administrador/UUID, recibo durável e relatório por linha sem
  credenciais. Replay não repete gravações ou auditoria.
- Transação única para proxies, auditoria de criação, lote e resultados. Lock de
  tabela impede corrida entre importações e com INSERT/UPDATE do CRUD.
- Nenhum CSV original ou senha em claro em recibos, respostas, logs ou auditoria.

Qualquer erro de linha/cabeçalho aborta toda a importação com 422 e relatório.
Vazios e duplicatas internas válidas são ignorados; duplicatas com outros erros
continuam inválidas. Conflitos atuais por host/porta são contabilizados e ignorados,
sem sobrescrever proxies. PERFIL, PLATAFORMA e lote não criam vínculos ou entidades.

Alternativas avaliadas: guardar upload temporário protegido ou reenviar o CSV.
Escolhido reenvio com prova protegida, evitando armazenamento temporário de
credenciais e limpeza de uploads. A confirmação inicial revalida o arquivo e
consulta conflitos sob bloqueio; replay de operação concluída recupera o resultado
histórico e exige prova válida, sem executar novamente a importação.
Detalhes na [ADR 0007](decisions/0007-controlled-csv-import.md) e no
[contrato HTTP](api/proxy-csv-import.md).

## Arquivos implementados/alterados

- Domain: `Proxies/ProxyImportBatch.cs` (lote e resultados sanitizados).
- Application: `Proxies/Import/ImportContracts.cs`, `ProxyCsvImportService.cs`,
  `PreviewContracts.cs` e `ProxyCsvPreviewService.cs` (analisador reutilizado,
  região explícita, prova, casos de uso e relatório).
- Infrastructure: `Proxies/DataProtectionImportConfirmation.cs`,
  `Proxies/EfProxyImportStore.cs`, `DependencyInjection.cs`,
  `Persistence/CentralContingenciaDbContext.cs`, migration/Designer/snapshot.
- Api: `Proxies/ProxyImportPreviewEndpoint.cs` e `Program.cs`
  (transporte multipart compartilhado, erros sanitizados e OpenAPI).
- Testes: `CsvImportTests.cs`, `CsvImportEndpointTests.cs` e `ApiTests.cs`.
- Documentação: ADR 0007, contrato de importação, guia de prévia, índices de API
  e arquitetura, README, AGENTS.md, CLAUDE.md, SQL e este relatório.

Nenhum código frontend/layout foi alterado. Autenticação e dependências entre
camadas foram preservadas. Não foram adicionados pacotes ou integrações externas.

## Migration

`20261011165123_AddProxyImportBatches`: cria somente `audit.ProxyImportBatches`
e `audit.ProxyImportLines`, constraints, FKs e três índices, incluindo unicidade
por administrador/chave. O fingerprint é protegido por Data Protection; não
armazena hash simples, CSV ou credenciais. Resultados persistidos são apenas os
de operações concluídas.

SQL revisado em [security/etapa-2.3b-migration.sql](security/etapa-2.3b-migration.sql).
Up aditivo, sem UPDATE/DELETE ou alteração de tabelas existentes. Down remove as
duas tabelas novas e não foi executado. Migration aplicada somente pelas fixtures
nos bancos descartáveis; **não aplicada no banco homologado**.

## Validação executada

| Verificação | Resultado |
| --- | --- |
| Build solução .NET 10 | Aprovado, zero avisos e erros |
| Testes unitários .NET | 134 aprovados |
| Testes integração .NET/PostgreSQL descartável | 116 aprovados |
| Total .NET | 250 aprovados, zero falhas/ignorados |
| Frontend lint e typecheck | Aprovados |
| Testes frontend | 63 aprovados |
| Build frontend | Aprovado; avisos Rollup/Zod já existentes |
| EF has-pending-model-changes | Nenhuma alteração pendente |
| Serviços locais existentes, em leitura | API health 200, frontend 200, rotas protegidas anônimas 401 e OpenAPI acessível |

Cobertura nova: importação válida/repetida, prova adulterada e arquivo/região
modificados, chave reutilizada com arquivo diferente, conflito inserido após prévia,
senha diferente sem sobrescrever conflito, região ausente/explícita/inválida,
erros de linha e duplicata inválida, expiração durante confirmação, criptografia
real e ausência de chave, autorização/CSRF, prova expirada/identidade diferente,
ausência de credenciais/token nos logs e respostas, concorrência de importações
com mesma/diferente chave e bloqueio de escrita do CRUD.

Teste de rollback instala trigger apenas no banco descartável, falha ao salvar
resultados após o INSERT do proxy e comprova a inserção prévia por sequência
não transacional. Nenhum proxy/evento/lote permanece; retirada a falha, repetir
com a mesma chave conclui a operação. Testes anteriores de parser, limites,
autenticação, proxies e arquitetura continuam aprovados.

Comandos usados, na raiz:

```powershell
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3b/
. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES = $env:ConnectionStrings__DefaultConnection
dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3b/
Remove-Item Env:CENTRAL_TEST_POSTGRES
# Na pasta src/central-contingencia-web:
npm run lint
npm run typecheck
npm test -- --run
npm run build
```

Saída de build isolada evita substituir DLLs da API homologada em execução.
A conexão disponibilizada pelo script permanece somente no processo local;
as fixtures criam e removem seus próprios bancos `p6_auth_test_UUID`.

## Preservação da homologação

Contagens antes/depois idênticas: 4 usuários, 32 eventos de autenticação, 1 proxy,
1 evento de proxy, 3 migrations. A nova tabela de lotes não existe na homologação.
Consulta final encontrou zero bancos de teste remanescentes. Não foram realizados
login, importação ou alteração de usuários/dados homologados durante esta etapa.
API e frontend existentes foram mantidos em execução, com verificação somente
de leitura. Sem commits, deploys ou avanço para Etapa 2.3C.

## Execução local e pendências

Antes de disponibilizar commit na homologação: obter aprovação explícita, validar
backup e conexão e aplicar a migration revisada. Comando futuro, **não executado**:

```powershell
dotnet ef database update 20261011165123_AddProxyImportBatches --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure
```

Depois da autorização/aplicação, reiniciar a API com a configuração/chave DPAPI
existentes. A API atualmente em execução conserva a versão anterior; testes de
integração validaram o código novo em ambiente descartável. Login/CSRF existentes
devem preceder prévia e commit, conforme contrato HTTP. Não importar arquivos reais.

Limitações/riscos: bloqueio de tabela reduz paralelismo de escrita, timeout de cinco
segundos requer retry com a mesma chave, recibos não têm expurgo implementado e
dependem da conservação do keyring Data Protection. Provas expiradas exigem nova
prévia. Strings gerenciadas não permitem garantia de apagamento físico. Logging
de transporte deve redigir corpos e headers sensíveis. Não foi feita homologação
manual de importação no banco existente, por ausência de autorização de migration
e escrita. Nenhuma nova tela ou validação visual é pertinente ao escopo backend.

Backend 2.3B implementado e validado; habilitação homologada pendente de migration
autorizada. Etapa 2.3C permanece aguardando solicitação explícita.
