# Homologação 2.4C.3 — Concluída tecnicamente

Data: **12/10/2026**. Retomada autorizada pelo usuário.

**Homologação técnica concluída na retomada autorizada; aguardando revisão do usuário.**
Migration já aplicada na execução anterior. Nesta retomada não foi reaplicada ou
revertida. Nenhum CSV original enviado ou recuperação real executada.

## Pré-verificações e correção anterior

AGENTS.md, CLAUDE.md e relatório de preparação consultados. Conexão validada em
**127.0.0.1:55432 / central_contingencia_dev**. Conferência EF repetida antes da
aplicação: sete migrations aplicadas, somente
**20261012131756_AddProfileRecoveryReceipts** pendente, após
20261012102737_AddProfileAssignments.

A falha anterior do builder foi corrigida exclusivamente no harness, usando
set_ConnectionString, set_Item e get_ConnectionString. Verificação explícita impediu
chave de dicionário ConnectionString e exigiu Database=postgres para a referência.
Parser PowerShell aprovado antes da execução. Guard do harness mantido.

SQL integral revisado: [security/etapa-2.4c3-migration.sql](security/etapa-2.4c3-migration.sql).
SHA-256 856362CFBABF69A8AAB33746F248A11FC2BB830206713B2A7FD95F12DBE831BC,
idêntico ao aprovado. Duas tabelas novas, sem ALTER/DML destrutivo em tabelas
anteriores. Dependência e única pendência conferidas novamente no instante anterior
à aplicação.

Backup disponível:
`C:\PROJETOS\CENTRALCONTINGENCIA\.local\backups\etapa-2.4c3-preparacao-20261012T142014Z`.
Manifesto dos oito artefatos e seus tamanhos/hashes conferidos antes e depois.
ACL restrita e sem herança confirmada; archive completo novamente decodificado por
pg_restore --file=/dev/null, sem restore/conexão de escrita. Cópia temporária removida.

Chave DPAPI original e dois XMLs do keyring idênticos ao backup. Roundtrip AES-GCM
sintético, nonces distintos e rejeição de adulteração aprovados. **131 envelopes
existentes autenticados em READ ONLY**, com buffers temporários zerados, sem expor
segredos. Keyring copiado carregou dois encryptors e passou roundtrip com geração
automática desabilitada. Nenhuma chave regenerada/substituída.

## Referência, aplicação e validação estrutural

Banco p6_reference_test_UUID criado a partir de conexão administrativa ao postgres,
sem dados reais. Aplicado o target nele para gerar referência nativa de estruturas.
As 16 estruturas anteriores corresponderam exatamente; somente as duas adições
aprovadas foram reconhecidas. Referência descartada antes de concluir.

Aplicado exclusivamente:

```powershell
dotnet ef database update 20261012131756_AddProfileRecoveryReceipts --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
```

Saída: Applying migration '20261012131756_AddProfileRecoveryReceipts'. Done.
Histórico confirmado em leitura: **20261012131756_AddProfileRecoveryReceipts | 10.0.8**.
Nenhuma outra migration aplicada ao banco homologado.

Duas tabelas audit.ProfileRecoveryBatches e audit.ProfileRecoveryLines, ambas vazias.
Colunas/tipos/nulabilidade, constraints/FKs e índices comparados integralmente com
`.local/24c3-reference.structure.txt` pela implementação existente do comparador.
**Sete índices físicos e onze constraints**: duas PKs, cinco CHECKs e quatro FKs.
RESTRICT para proxy/perfil/alocação; CASCADE somente recibo → suas linhas.
Não houve aceitação genérica de objetos desconhecidos.

## Preservação antes/depois

Baseline `.local/24c3-homolog-before.txt` e sidecar de estruturas, correspondente ao
baseline aprovado na preparação. Comparações antes, após migration e após
reinício/testes passaram, sem excluir tabela anterior.

As contagens/fingerprints e estruturas das **16 tabelas anteriores permaneceram
idênticas**, descontando somente o novo registro autorizado de __EFMigrationsHistory.
Os sete registros antigos de migrations foram preservados; histórico agora tem oito.
Novas tabelas foram validadas explicitamente, inclusive vazias.

| Evidência | Antes | Depois |
|---|---:|---:|
| Proxies | **131** | **131** |
| Perfis | 0 | 0 |
| Alocações | 0 | 0 |
| Recibos de recuperação | Tabela ausente | 0 |
| Linhas de recuperação | Tabela ausente | 0 |
| Tabelas | 16 | 18 |
| Migrations | 7 | 8 |

Fingerprint integral Proxies:
**f1f2b42e10fc6573a75f93b777e7fcf6**, preservado. Inclui IDs, credenciais/envelopes,
status, região e timestamps sem expor seus valores. Mantidas 130 importadas e uma
preexistente. AuthenticationEvents também permaneceu com 38 registros.
Backup e três arquivos de chave revalidados após reinício/testes; nenhum byte ou
arquivo adicional de chave encontrado. Zero bancos descartáveis remanescentes.

## API, HTTPS e testes concluídos

Somente após as validações anteriores passarem, API antiga PID 5908 encerrada e
nova API iniciada com DLL da compilação 2.4C.3, configurações/chave existentes e
janela oculta. **PID 4384**, https://localhost:7090 e http://localhost:5041;
processamento de /api/v1 continua exigindo HTTPS. Frontend preservado.

Smoke HTTPS concluído sem login ou efeitos persistentes:

| Requisição | Resultado |
|---|---:|
| GET /health | 200 |
| GET /api/v1/auth/me anônimo | 401 |
| POST /api/v1/profiles/recovery/preview anônimo | 401 |
| POST /api/v1/profiles/recovery/commit anônimo | 401 |
| GET /openapi/v1.json | 200; duas rotas novas presentes |

Health verifica processo, não integridade física do PostgreSQL. TLS de desenvolvimento
ignorado somente no cliente local de verificação, sem modificar confiança do sistema.

Build final em `.local/build-24c3-homolog-tests/`: **zero erros/avisos**.
Suíte completa .NET: **237 unitários + 179 integração = 416 aprovados**, zero
falhas/ignorados. Integração HTTP usa TestServer com URLs HTTPS e PostgreSQL real
p6_auth_test_UUID. Escritas/migrations/seeds exclusivamente nas fixtures descartáveis;
conexão administrativa inicial aponta para postgres, nunca para a homologação.

Os nove testes de endpoints de recuperação e a regressão verificaram login Admin,
autorização/CSRF, pareamento/ambiguidades/conflitos, prova/arquivo/opções,
idempotência/replay, concorrência, expiração, transação/rollback de auditoria,
preservação de proxies/histórico e ausência de credenciais em respostas/logs
observados. Nenhum CSV real foi enviado. Não houve criação operacional homologada.

Resultados: `.local/24c3-homolog-test-results/` e `.local/24c3-homolog-tests.log`.
Logs da nova API: `.local/api-24c3.stdout.log` e `.stderr.log`. Frontend não foi
alterado nem seus testes reexecutados nesta homologação de backend.

## Erro anterior e correção autorizada

O smoke HTTPS autenticado adicional em `.local/24c3-readonly-smoke/` não compilou:
**CS0119: File() é um método que não é válido no contexto fornecido**, Program.cs:105.
A função auxiliar File() para multipart sombreou System.IO.File, usado depois
para gravar a evidência JSON. Erro de montagem do harness, identificado antes de
qualquer execução/requisição dele.

A execução anterior foi interrompida. Na retomada autorizada, a chamada passou a
**System.IO.File.WriteAllTextAsync**, preservando a função multipart File().
Compilação final aprovada: zero erros/avisos. Somente harness local foi alterado;
nenhuma regra de negócio, contrato ou fonte de produto mudou.

## Retomada — smoke autenticado e integridade concluídos

Smoke direto em **https://localhost:7090**, Kestrel existente:

| Cenário | Resultado |
|---|---|
| Sessão Admin reconhecida por /auth/me | 200, papel confirmado |
| Prévia sem CSRF | 400 |
| Prévia sintética sem correspondência | 200, conflito, sem prova |
| Confirmação com prova inválida | 409 |
| Confirmação com prova protegida expirada | 409 |
| Confirmação anônima | 401 |
| Grid paginado, filtros e ordenação | 131 UUIDs únicos, sete páginas |
| Credenciais em corpos/logs observados | Nenhum vazamento detectado |

Principal Admin e cookie criados apenas em memória pelos mecanismos Identity/Data
Protection a partir do usuário existente. Conexão do harness configurada READ ONLY;
geração automática de chaves desabilitada. Não houve login/logout ou alteração de
conta na homologação. Login por senha, Manager/Operator, pareamento exato com proxy
sintética existente, ambiguidades/conflitos e confirmação válida foram verificados
pelos testes HTTP no banco descartável, não no banco homologado.

CSV do smoke homologado era sintético, sem credenciais, com host UUID inexistente.
Prévia não gravou e não emitiu prova. Confirmações não tinham caminho válido de
persistência: prova inválida/expirada ou ausência de sessão. Prova expirada foi
gerada em memória com prazo passado e também não correspondia ao plano; teste
unitário isola expiração mantendo o mesmo plano. Nenhuma confirmação válida tentada.

Harness leu os logs stdout/stderr da API com compartilhamento de leitura e comparou
em memória senha da conexão, chave AES, cookie, CSRF, prova expirada e marcadores de
credenciais sintéticas. Nenhuma ocorrência detectada. Corpos HTTP também conferidos;
grid não inclui campos de credenciais. Essa evidência cobre valores conhecidos e
logs observados, sem garantir futuras configurações de logging/reverse proxy.
Não foram gravados tokens, HAR ou traces. Evidência sanitizada:
`.local/24c3-readonly-smoke.json`.

Regressão completa repetida nesta retomada: **237 unitários + 179 integração =
416 aprovados**, zero falhas/ignorados. Resultados atuais:
`.local/24c3-homolog-resume-results/` e `.local/24c3-homolog-resume-tests.log`.
Escritas somente nas fixtures sintéticas p6_auth_test_UUID, removidas ao final.

EF confirmou oito migrations registradas e nenhuma pendente. Comparações finais
passaram para dados/estruturas das 16 tabelas anteriores, descontando apenas o
registro autorizado de migration, e para as duas novas tabelas/vazias contra a
referência. **131 proxies; zero perfis, alocações, recibos/linhas de recuperação**.
As oito entradas de histórico permaneceram; nenhuma nova migration executada.

DPAPI e dois XMLs do keyring idênticos ao backup; conjunto preservado, sem gerar
chaves. Roundtrip sintético, nonces distintos e adulteração AES rejeitada; 131
envelopes autenticados novamente em leitura, com buffers temporários zerados.
Keyring copiado passou roundtrip com geração desabilitada. Backup disponível e
manifesto revalidado. Nenhum efeito persistente dos smoke tests encontrado.
API não precisou ser reiniciada nesta retomada.

## Riscos e parecer

Migration/schema e preservação homologados tecnicamente no banco local. Nenhuma
falha real de dados, estrutura ou chaves encontrada. API atual permanece em execução.
**Homologação técnica concluída; pronta para prévia do CSV original sem gravação.**

Não executar novamente a migration. Nenhum restore/Down necessário ou tentado.
Riscos anteriores de locks, conservação de keyring/backup, ausência de ensaio de
restauração e conciliação de fonte permanecem no relatório de preparação/ADR 0012.
Recuperação real continua exigindo autorização própria e revisão explícita da prévia.

Nesta entrega foram alterados apenas artefatos locais de execução/verificação e
este relatório. Sem recuperação real, CSV real, exclusão/alteração de proxies,
perfis reais, alteração de credenciais/chaves, commits, deploys ou integrações.
Aguardar revisão do usuário. Prévia futura deve usar o endpoint dedicado
**POST /api/v1/profiles/recovery/preview**, com Admin e CSRF. A importação de proxies
é outro fluxo e não deve ser usada para recuperar perfis. Não há nova interface React
de recuperação. Quantidade recuperável e impedimentos reais dependem da prévia da
fonte, que ainda não foi enviada. Commit real continua exigindo autorização própria.
