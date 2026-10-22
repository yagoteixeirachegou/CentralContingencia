# Relatório — Etapa 2.2A: API de Gestão de Proxies

Data de conclusão: 11/10/2026 (America/Sao_Paulo); execução iniciou em 10/10.
Status: implementação e verificações concluídas; migrations de persistência e
auditoria aplicadas após aprovação/backup. Pronto para iniciar a Etapa 2.2B.

AGENTS.md, CLAUDE.md e docs/etapa-2.1-report.md lidos integralmente antes de alterações.
Plano apresentado; .NET SDK 10.0.300, Node 22.22.3 e PostgreSQL 17.11 confirmados.
EF Core/Identity 10.0.8 e Npgsql EF 10.0.0 preservados, sem pacotes novos.

## Migration 2.1: autorização condicionada executada

Destino verificado: container central-contingencia-dev-postgres-1 saudável,
bind exclusivo 127.0.0.1:55432, banco central_contingencia_dev, usuário central_dev.
Antes: somente InitialIdentityAndAuthenticationAudit, três usuários sintéticos,
quinze eventos de autenticação e ausência de proxies.Proxies.

SQL docs/security/etapa-2.1-migration.sql revisado integralmente: somente schema,
tabela e índices novos, constraints e registro no histórico EF. Nenhum UPDATE/DELETE,
DROP ou alteração de tabelas/dados existentes. Down não foi executado.

Backup atualizado criado **antes** da aplicação:

- .local/backups/p6-pre-proxy-20261011T025119Z.dump.
- pg_dump custom/gzip 17.11, 19512 bytes, 51 entradas de catálogo.
- SHA256: `478E792EDE778AB68C60277F5EB771EF9EACD3A18195EEBDAC34C0AA9C31DCD8`.
- pg_restore --list e decodificação --file=/dev/null passaram.
- Não foi realizado teste de restauração; cópia também permanece no /tmp do container.
- Backup contém dados sintéticos de autenticação, fica em .local ignorada e não
  deve ser publicado. Hashes/senhas/chaves não foram exibidos.

Comando executado após cumprir as condições expressamente autorizadas pelo usuário:

```powershell
. ./scripts/Set-LocalDatabase.ps1
dotnet ef database update 20261011024141_AddProxyPersistence `
  --project src/CentralContingencia.Infrastructure `
  --startup-project src/CentralContingencia.Infrastructure
```

Sucesso. Histórico passou a conter exatamente as duas migrations; consulta confirmou
três usuários, quinze eventos de autenticação e zero proxies. Nenhum Admin reprovisionado.

## Implementação

| Endpoint | Política | Resultado de sucesso |
| --- | --- | --- |
| GET /api/v1/proxies | Operations: Admin/Manager/Operator | 200, lista paginada |
| GET /api/v1/proxies/{id} | Operations | 200, detalhe mascarado |
| GET /api/v1/proxies/available | Operations | 200, elegíveis paginados |
| POST /api/v1/proxies | Management: Admin/Manager | 201 + Location |
| PATCH /api/v1/proxies/{id} | Management | 200, detalhe mascarado |

Application: ProxyManagementService implementa listagem/detalhe/criação/alteração,
validação, decisão de autorização, uso da porta criptográfica e preparação da auditoria.
IProxyStore é específico, sem IQueryable/EF na Application. Contratos de respostas
não possuem senha/envelope: usernameMasked constante ***, metadados e disponibilidade.
Domain valida alterações atomicamente antes de mudar propriedades e define auditoria.
Infrastructure implementa store EF/Npgsql; Api traduz falhas para ProblemDetails.
Referências e limites de arquitetura preservados, sem refatoração da autenticação.

Validações: host/porta/usuário/região do domínio, estados exatos, validade obrigatória
futura em criação e quando explicitamente alterada; UTF-8 até 1024 bytes de senha.
POST padrão Disabled se status omitido. PATCH parcial, ausentes preservados;
null, duplicatas, campos desconhecidos, objeto vazio e tipos incorretos recusados.
Datas de corpo exigem Z/offset explícito. UUID/timestamps internos não são editáveis.
Paginação 1–10000, tamanho 1–100 (padrão 20); filtros host/port/region/status/expiresBefore;
whitelist de sete ordenações e asc/desc, UUID como desempate. Sem SQL dinâmico de entrada.

Senha cifrada antes de persistir, AES-256-GCM existente, nonce novo e UUID/KeyId em AAD.
Nenhum caso de uso de consulta chama Decrypt. Username continua metadado legível
somente no banco, conforme ADR 0003; respostas sempre mascaram esse identificador.
Sem chave, leitura/autenticação continuam; criação/troca de senha retorna 503 sanitizado.
Middleware e OpenAPI exigem CSRF também em PATCH. Escritas usam rate limiting
sensitive existente; cookies, HTTPS, sessão, logout e políticas anteriores preservados.

Auditoria sanitizada audit.ProxyEvents: UUID, ator, ProxyId, Created/Updated, UTC e
correlation ID; sem credenciais, hosts, diffs ou payloads. Proxy + evento são gravados
no mesmo SaveChanges transacional. xmin é concurrency token shadow do PostgreSQL;
não há coluna física extra. Conflito concorrente/único vira 409 e não deixa evento órfão.
OpenAPI inclui contratos, políticas por cookie, CSRF POST/PATCH e password writeOnly.

## Migration adicional de auditoria

Migration **20261011025452_AddProxyAudit**, designer e snapshot gerados.
SQL docs/security/etapa-2.2a-migration.sql revisado: somente CREATE TABLE audit.ProxyEvents,
dois índices e INSERT de histórico; nenhuma mudança em Identity ou linhas existentes.
xmin é coluna nativa, não gera ALTER TABLE. Down gerado é destrutivo e não foi executado.

Inicialmente aplicada apenas em bancos descartáveis p6_auth_test_UUID. A autorização
original especificou a migration 2.1; a auditoria foi apresentada separadamente.
Backup preparado para revisão e decodificado com pg_restore:
.local/backups/p6-pre-proxy-audit-20261011T030543Z.dump, 23798 bytes,
SHA256 `0798CF900B49CDB347D3A94A7D82B4A0BC0F1524BE04C5C8DF94C6EDD2E9D6F5`.
A aprovação específica foi recebida: “Aprovo a aplicação local após backup”.
Conexão e SQL aditivo foram conferidos novamente. Novo backup imediatamente antes
da aplicação: .local/backups/p6-approved-pre-audit-20261011T032803Z.dump,
23798 bytes, SHA256 `FAAB88B8382D260C4C54F60D12CD1ECE28BD7114765183E393913641778BB9BE`.
pg_restore --file=/dev/null validou sua decodificação. Nenhum segredo foi exibido.
Comando efetivamente executado após aprovação/backup:

```powershell
. ./scripts/Set-LocalDatabase.ps1
dotnet ef database update 20261011025452_AddProxyAudit `
  --project src/CentralContingencia.Infrastructure `
  --startup-project src/CentralContingencia.Infrastructure
```

Aplicação concluída com sucesso. Histórico contém as três migrations; três usuários
e quinze eventos de autenticação preservados, zero proxies e zero eventos de proxies.
Startup não aplica migrations. Nenhum bootstrap adicional executado/necessário.

## Testes e resultados

| Comando/check | Resultado |
| --- | --- |
| dotnet build CentralContingencia.slnx --no-restore | Sucesso; zero erros/avisos |
| ./scripts/Test-LocalPostgres.ps1 | 153 aprovados: 67 unitários + 86 integração; zero falhas/ignorados |
| npm run typecheck | Sucesso |
| npm run lint | Sucesso |
| npm test | 7 aprovados |
| npm run build | Sucesso; dois avisos conhecidos Zod/Rollup |
| dotnet ef migrations add AddProxyAudit | Sucesso; somente geração |
| dotnet ef migrations script AddProxyPersistence ... | SQL adicional gerado/revisado |
| dotnet ef migrations has-pending-model-changes | Nenhuma alteração pendente de modelo |
| Smoke da API real reiniciada | Health 200, sessão/proxies anônimos 401, cinco operações de proxies no OpenAPI, password writeOnly |
| Consulta final do banco homologado | Três migrations, 3 usuários, 15 eventos de autenticação, 0 proxies e 0 eventos de proxies |
| Bancos descartáveis remanescentes | Nenhum |

Novos casos: 18 unitários + 29 integração, além dos 106 .NET anteriores. Cobrem:
matriz de papéis, anônimo, autorização na Application, validações/limites, criptografia
antes da gravação, ausência de senha/username/envelope nas respostas, decrypt controlado
da senha persistida em teste, auditoria sanitizada, criação/detalhe/PATCH parcial,
PATCH/POST sem CSRF/adulterado, key ausente 503, paginação/filtros/todas as ordenações,
ativo vencido indisponível, IDs ausentes/vazios, tipos/campos/duplicatas/null inválidos,
OpenAPI writeOnly/CSRF/cookie e conflito concorrente com rollback do evento perdedor.

Fixtures criam somente bancos locais descartáveis, aplicam migrations e contas/chaves
sintéticas em memória. Os 47 testes de autenticação originais e os da Etapa 2.1
permanecem exercitados. O antigo teste de ausência da rota de proxies passou a
verificar 401; o teste de upgrade aceita a nova migration pendente sem perder a
verificação de preservação. Frontend não alterado; seus sete testes continuaram passando.

## Evidências, execução e limites

Fontes de evidência: saída real dos comandos acima, histórico EF e contagens locais,
backup com hash, SQLs versionáveis, testes novos e contratos OpenAPI. Nenhum password,
chave, valor de cookie/CSRF ou corpo com credenciais foi incluído no relatório.
Não houve nova validação em navegador real; API foi validada por TestServer com
PostgreSQL real. A homologação Chrome da Etapa 1.1 permanece histórica.

Execução local com as migrations já aplicadas:

```powershell
# A chave DPAPI já existe; não gerar outra nem sobrescrevê-la.
. ./scripts/Set-LocalProxyEncryption.ps1
try { ./scripts/Start-LocalApi.ps1 }
finally {
  [Environment]::SetEnvironmentVariable('ProxyEncryption__Keys__development-v1', $null, 'Process')
  Remove-Item Env:ProxyEncryption__ActiveKeyId -ErrorAction SilentlyContinue
}
```

API HTTPS localhost:7090, PostgreSQL localhost:55432. Frontend existente localhost:5173
mantido somente para autenticação; nenhuma tela de proxies. Não reprovisionar Admin.
Veja docs/api/proxies.md, ADR 0004 e guia de chaves para contratos e operação.
API inicialmente reiniciada sem chave enquanto a auditoria aguardava aprovação.
Após aprovação, foi reiniciada com a chave DPAPI existente. A sessão da ferramenta
deixou de estar disponível e a inspeção não encontrou processo/porta ativos; foi
reiniciada em processo de fundo oculto com logs locais em .local/verification-2.2a.
Processo API observado PID 5044; o identificador vale somente nesta execução.
Autenticação continua disponível. Smoke usou exceção TLS apenas no cliente temporário;
o aviso de certificado sem confiança permanece.

Limitações: sem conectividade externa ou
alocações (available não garante reserva); sem ETag/If-Match ou deduplicação de POST;
total/página podem divergir sob concorrência; nonces/orçamento/rotação e proteção
Windows DPAPI seguem ADR 0003. TLS local ainda depende de confiança explícita do
operador, conforme Etapa 1.1; confiança de certificados não foi alterada.
Backups foram validados para leitura/decodificação, sem teste de restauração.

Ocorrência de edição: um patch com duas operações para o mesmo arquivo foi recusado
pelo editor; reapresentado em uma única operação e aplicado. Builds/testes executados
passaram sem erros de compilação ou falhas de testes. Nenhum novo pacote instalado.

Sem frontend novo, Google Sheets, AdsPower, Notion, n8n, MCP, dados reais, produção,
commits ou deploys.

## Verificações após reinicialização com DPAPI — 11/10/2026

Solicitadas e autorizadas pelo usuário; nenhum dado adicional gravado no banco homologado.

- API real HTTPS iniciou; health 200, CSRF 200, OpenAPI 200; sessão e três consultas
  de proxies sem autenticação retornaram 401. Cinco operações REST presentes no contrato.
- Verificador local ignorado .local/verification-2.2a usa o adapter e registro DI do
  produto com a **chave DPAPI existente**, carregada somente no processo. AES-256-GCM
  foi exercitado integralmente em memória: round-trip UTF-8, nonce 12/tag 16 bytes,
  1000 nonces distintos; adulteração de tag/nonce/ciphertext e outro UUID recusados.
  Não resolve store nem executa gravação. Não foi criado endpoint de diagnóstico.
- Duas saídas de log da API e corpos HTTP consultados foram inspecionados em memória
  procurando a chave e a senha sintética, sem imprimir valores: nenhuma ocorrência.
  Respostas autenticadas/mutações sem exposição de credenciais são verificadas pela
  suíte de integração. Isso comprova o material observado, não uma garantia sobre
  futuras configurações de logging ou todas as requisições possíveis.
- Regressão pós-reinício executada: 67 unitários aprovados; 4 integração aprovados
  e 82 falharam por PostgreSQL indisponível (conexão recusada). O pipe do Docker
  também estava ausente. Docker Desktop foi reiniciado e o container existente
  (Exited 255) iniciado com docker compose start --wait postgres, preservando volume.
  Não foi identificado o motivo da interrupção do daemon. Nova rodada após recuperação
  passou nos **153 testes** (67 unitários/86 integração), zero falhas/ignorados.
  Login, sessão, logout,
  autorização, CSRF e endpoints com escrita usam bancos descartáveis; não foi feito
  novo login no banco homologado, pois produziria auditoria e violaria a restrição.
- Confirmação final do banco homologado após recuperação: três migrations,
  3 usuários, 15 eventos de autenticação, 0 proxies e 0 eventos de proxies; contagens
  preservadas. Nenhum banco descartável remanescente. Nenhum novo commit, deploy,
  migration ou dado homologado nesse exercício.
- Evidência sanitizada: .local/verification-2.2a/results.json. Verificador/logs ficam
  em .local, fora do versionamento. Não foi alterado código do produto.
- Frontend existente verificado novamente: npm test, 7 aprovados; npm run typecheck
  e npm run lint concluídos com sucesso. Nenhuma interface nova implementada.

**Pronto para iniciar a Etapa 2.2B — Interface React**, mediante autorização de seu
escopo. A etapa React não foi iniciada. Backend local, schema e chave estão disponíveis;
limitações de TLS, conectividade/alocações, idempotência e produção acima permanecem.
