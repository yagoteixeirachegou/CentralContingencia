# Homologação 2.3B — concluída

Data: 11/10/2026.

## Pré-verificações

Erro original: `$env:ProxyEncryption__Keys__development-v1` é interpretado como
expressão com operador/subtração. Corrigido exclusivamente o acesso para
`[Environment]::GetEnvironmentVariable('ProxyEncryption__Keys__development-v1','Process')`.
Script local de pré-verificação aprovado por Language.Parser antes de execução.
Nenhum valor secreto foi impresso. Conexão confirmada em 127.0.0.1:55432,
central_contingencia_dev. Backup conferido integralmente por SHA-256, chave DPAPI
acessível e de 32 bytes, chave/keyring iguais às cópias protegidas. EF confirmou
exatamente uma migration pendente: 20261011165123_AddProxyImportBatches.

Backup atualizado imediatamente antes da aplicação, em
`.local/backups/etapa-2.3b-aplicacao-20261011-151041/`, ACL usuário/SYSTEM,
dump custom validado por pg_restore --list e leitura integral --file=/dev/null,
chave DPAPI/keyring preservados e manifesto SHA-256. Sem restauração de ensaio.

## Execução

Aplicado somente o alvo autorizado via dotnet ef database update com --no-build
e saída isolada .local/build-2.3b. EF registrou Applying migration e Done, exit 0.
Nenhuma importação CSV, outra migration, alteração operacional, commit ou deploy.

## Validação e interrupção

A primeira comparação de dados sinalizou divergência e interrompeu o script antes
do reinício da API. Diagnóstico somente leitura identificou falso positivo:
psql imprimia tanto os SELECTs gerados quanto seus resultados; o filtro excluía
apenas os resultados das tabelas novas, deixando seus SELECTs na comparação.
Conferência somente leitura excluindo ambas as linhas comprovou igualdade de
contagens e fingerprints de todas as tabelas anteriores (Identity, audit e proxies).
Não houve divergência de dados existentes.

Verificado history com quatro migrations, incluindo exatamente o alvo autorizado.
As duas tabelas existem e estão vazias. Três índices secundários e duas PKs
presentes; quatro checks, duas FKs e duas PKs validados. Cascade restrito a
lote/linhas, FK para proxies RESTRICT. Nenhum registro operacional foi importado.

Conforme instrução de parar diante de falha, não foi retomado o fluxo de execução
após a interrupção: API anterior permanece em execução, sem reinício. Testes de
integração e smoke pós-reinício não executados nesta rodada. Nenhum procedimento
de recuperação/Down ou alteração corretiva no banco foi realizado.

Pendência: retomar explicitamente reinício da API com chaves/configuração existentes
e executar testes descartáveis/smoke de leitura. Migration já aplicada, não repetir
aplicação. O código-fonte/layout não sofreu alteração nesta operação.

## Conclusão da homologação — retomada autorizada

O histórico acima descreve a interrupção anterior. A pendência de reinício e
validação foi resolvida nesta retomada; nenhuma migration foi reaplicada na
homologação, importação executada nela ou dado operacional modificado.

Leitura inicial confirmou quatro migrations, incluindo exatamente
20261011165123_AddProxyImportBatches; zero constraints não validadas e zero
índices inválidos/não prontos. Contagens mantidas: 4 usuários, 32 eventos de
autenticação, 1 proxy, 1 evento de proxy, zero lotes e zero linhas de importação.

API anterior encerrada de forma direcionada e versão 2.3B iniciada pelo dotnet
com output isolado, contentRoot existente, ambiente Development, conexão local
e mesma chave DPAPI. API em execução: PID 10364, HTTPS localhost:7090; frontend
existente disponível em HTTPS localhost:5173. Sem regeneração/substituição de
chaves. Hashes do arquivo DPAPI e dos arquivos existentes do keyring conferidos
com o backup. Senhas/chaves não foram impressas ou persistidas em argumentos.

### Resultados desta rodada

| Verificação | Resultado |
| --- | --- |
| Build .NET da solução | Aprovado, 0 avisos/erros |
| Testes unitários | 134 aprovados |
| Integração PostgreSQL em banco descartável | 116 aprovados |
| Total automatizado .NET | 250 aprovados, 0 falhas/ignorados |
| Health, OpenAPI e frontend | 200 |
| Sessão anônima, listagem/disponíveis/detalhe, POST/PATCH e logout sem sessão | 401 |
| Prévia/commit sem sessão, multipart e JSON | 401, sem gravação |
| OpenAPI CSV | multipart binary, Admin/cookie, CSRF e headers de confirmação/idempotência documentados |
| AES com chave DPAPI existente | Roundtrip em memória, nonce diferente e ciphertext adulterado rejeitado |
| Snapshot homologado antes/depois | Contagens/fingerprints idênticos em todas as tabelas dos três schemas |
| Bancos descartáveis remanescentes | Zero |
| Logs de startup/smoke | Nenhum padrão de credencial detectado; valores não impressos |

Testes de integração cobrem login/sessão/logout, papéis, CSRF, CRUD, prévia,
confirmação válida, replay, conflitos, alterações de arquivo, rollback e concorrência,
AES e ausência de credenciais nas respostas/logs. Usam credenciais sintéticas e
bancos p6_auth_test_UUID criados/removidos pela fixture. Preparação de schema ocorre
somente nesses bancos descartáveis, sem aplicação de migrations na homologação.

Na homologação, não foi feito login que gerasse auditoria: auth/me anônimo e CSRF
foram verificados sem efeitos persistentes. Fluxos autenticados e de escrita foram
validados nas fixtures descartáveis. Não foram usadas credenciais reais. Verificação
de logs por padrões não é garantia absoluta de ausência de toda informação sensível;
testes com valores sintéticos complementam essa inspeção.

### Correção do falso positivo

Adicionado scripts/Compare-LocalDatabaseSnapshot.ps1, aprovado pelo parser PowerShell
e executado tanto contra o snapshot antigo quanto contra o snapshot desta rodada.
O script seleciona somente linhas de resultado schema.tabela|contagem|fingerprint,
descartando SELECTs impressos pelo psql. Tabelas novas só podem ser excluídas
explicitamente; snapshots permanecem em .local e não expõem registros.
Comparação antiga excluiu explicitamente apenas as duas tabelas adicionadas;
comparação antes/depois desta rodada incluiu todas as tabelas.

O smoke local inicialmente procurava passwordMasked no DTO de proxies, propriedade
que nunca pertenceu a esse contrato. A expectativa foi corrigida para usernameMasked
e ausência de password/username/ciphertext. Smoke completo passou, sem mudança da
API ou regra de negócio. Nenhuma falha real do sistema ou divergência de dados.

### Comandos e evidências

```powershell
./scripts/Compare-LocalDatabaseSnapshot.ps1 -SnapshotPath C:/PROJETOS/CENTRALCONTINGENCIA/.local/23b-before.txt -ExcludeTables audit.ProxyImportBatches,audit.ProxyImportLines
. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES=$env:ConnectionStrings__DefaultConnection
dotnet test CentralContingencia.slnx --no-build --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3b/
Remove-Item Env:CENTRAL_TEST_POSTGRES
node .local/smoke-23b-homologacao.cjs
node .local/verification-2.2b/final-readonly.cjs
./scripts/Compare-LocalDatabaseSnapshot.ps1 -SnapshotPath C:/PROJETOS/CENTRALCONTINGENCIA/.local/23b-homolog-before.txt
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3b/
```

Smoke criptográfico .local/crypto-23b-smoke executado com chave carregada apenas no
processo e removida do ambiente ao final. Logs locais ignorados:
.local/api-23b.stdout.log e .local/api-23b.stderr.log. Nenhum frontend/layout mudou;
lint/testes frontend não repetidos nesta rodada porque não houve alteração frontend.

Etapa 2.3B homologada no escopo autorizado e pronta para integração com o frontend.
Importação autenticada ponta a ponta foi validada em banco descartável, preservando
integralmente a homologação. Limitações operacionais da ADR 0007 permanecem;
pg_amcheck físico e restauração de ensaio continuam não realizados. Nenhuma Etapa
2.3C iniciada automaticamente, commit ou deploy executado.
