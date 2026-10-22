# Relatório — Etapa 2.1: Domínio e Persistência de Proxies

Data local: 10/10/2026 (America/Sao_Paulo).
Status: implementação e validação local concluídas; migration do banco homologado pendente de aprovação.

AGENTS.md, CLAUDE.md e relatórios das Etapas 1 e 1.1 lidos integralmente.
Repositório e ambiente inspecionados, plano breve apresentado antes das alterações.
Versões observadas: .NET SDK 10.0.300, Node 22.22.3, npm 10.9.8,
Docker 29.5.2, Compose 5.1.3, PostgreSQL 17.11; EF Core/Identity 10.0.8,
Npgsql EF 10.0.0. Nenhum pacote adicionado ou atualizado.

## Implementação

DDD/Clean Architecture preservadas, sem mudança de referências entre projetos:

- Domain/Proxies: agregado Proxy com UUID não vazio, Host, Port, Username,
  Password cifrada, Status, Region, ExpiresAt, CreatedAt e UpdatedAt.
- ProxyHost normaliza DNS ASCII/IP e recusa URLs, userinfo, caminhos e entradas
  inválidas. ProxyPort protege intervalo 1–65535. EncryptedProxyPassword valida
  envelope e copia buffers defensivamente, com igualdade por valor.
- Estados Active, Suspended e Disabled, com criação Disabled. Disponibilidade
  exige Active, validade temporal e ausência de alocação ativa informada explicitamente.
  Expirado pode ser representado, sem se tornar disponível. Não há módulo de alocações.
- Alteração de estado e substituição de envelope para futura rotação preservam
  timestamps UTC e recusam regressão temporal. Texto claro não entra no agregado.
- Application/Proxies: porta IProxyPasswordProtector, sem EF ou criptografia concreta.
- Infrastructure/Proxies: AES-256-GCM, chave de 32 bytes, nonce aleatório novo de
  12 bytes por operação, tag de 16 bytes, KeyId e AAD vinculando finalidade/UUID/versão.
  Recusa adulteração, troca de registro, chave desconhecida/incorreta e configuração inválida.
- Keyring com leitura por versão e escrita somente na chave ativa, sem fallback.
  Erros sanitizados, buffers temporários zerados, nenhuma senha em logs/relatório.
- Registro DI sob demanda: ausência de chave não impede autenticação homologada.
- Infrastructure/Persistence/Configurations: mapeamento no contexto existente,
  schema proxies separado; envelope bytea na mesma linha via OwnsOne.
- Scripts Initialize-LocalProxyEncryption e Set-LocalProxyEncryption: RNG de 32 bytes,
  arquivo DPAPI local ignorado, vinculado ao usuário Windows; carga em variáveis de processo.
  Inicialização recusa sobrescrita, inclusive criação exclusiva do arquivo.

Não foram criados endpoints, DTOs CRUD, interfaces React, repositórios genéricos,
integrações, importações, jobs de rotação ou auditoria de proxies. Identity, cookies,
CSRF, políticas, rate limiting, logout e componentes frontend foram preservados.
OpenAPI HTTP permanece com os mesmos contratos; smoke confirmou zero paths de proxies.

Documentação: ADR 0003, guia docs/security/proxy-encryption.md, arquitetura,
README, guia de segurança e registros de etapa AGENTS/CLAUDE atualizados.
Relatórios históricos das Etapas 1/1.1 não foram reescritos.

## Migration e banco homologado

Migration gerada: **20261011024141_AddProxyPersistence**, incluindo designer e snapshot.
SQL revisado integralmente em docs/security/etapa-2.1-migration.sql:

- Cria somente schema proxies e tabela Proxies.
- Campos obrigatórios, UUID PK, limites de texto, porta, estados permitidos,
  timestamps finitos e UpdatedAt >= CreatedAt, tamanhos e formato do envelope.
- Índices Host/Port, Status/ExpiresAt, Region; único PasswordKeyId/PasswordNonce
  como barreira contra repetição de nonce persistido sob uma versão de chave.
- Não altera tabelas Identity/auditoria nem executa UPDATE/DELETE de dados existentes.
  O INSERT no histórico EF registra somente a nova migration quando aplicada.
- Down gerado remove a nova tabela; não foi executado.
- Sem aplicação automática em startup; chave não fica no banco nem em arquivos versionáveis.

Migrations aplicadas **somente em bancos descartáveis p6_auth_test_UUID**.
Um teste prepara diretamente o schema anterior, provisiona Admin sintético e aplica
o upgrade: IDs/hashes/security stamps de usuários, papéis, IDs da auditoria e
tabela/linha sentinela permanecem iguais. Reaplicação é idempotente.
Os valores de hashes/senhas não foram impressos.

Inspeções somente leitura antes/depois no banco central_contingencia_dev:

| Evidência | Antes | Depois |
| --- | --- | --- |
| Migration aplicada | InitialIdentityAndAuthenticationAudit | A mesma |
| Usuários | 3 | 3 |
| Eventos de auditoria | 15 | 15 |
| Tabela proxies.Proxies | Ausente | Ausente |
| PostgreSQL | Healthy | Healthy |

Nenhuma migration ou escrita foi executada no banco homologado.
Não foi necessário novo backup: não houve alteração nele. O backup da Etapa 1.1
é histórico e não substitui um backup atualizado antes da futura aplicação.
Consulta final não encontrou bancos p6_auth_test_* remanescentes.

## Testes e resultados

| Comando/verificação | Resultado |
| --- | --- |
| dotnet build CentralContingencia.slnx --no-restore | Sucesso; zero erros/avisos |
| ./scripts/Test-LocalPostgres.ps1 | 106 aprovados: 49 unitários e 57 integração; zero falhas/ignorados |
| npm run typecheck | Sucesso |
| npm run lint | Sucesso |
| npm test | 7 aprovados |
| npm run build | Sucesso; dois avisos conhecidos de comentários Zod/Rollup |
| dotnet ef migrations add AddProxyPersistence | Sucesso; somente geração |
| dotnet ef migrations script InitialIdentityAndAuthenticationAudit ... | Sucesso; SQL revisado |
| dotnet ef migrations has-pending-model-changes | Nenhuma mudança pendente de modelo |
| Parser dos scripts PowerShell | Sem erros |
| Geração DPAPI + carregamento da chave | Aprovados, sem exibir chave |
| Segunda inicialização da chave | Recusada; hash do arquivo permaneceu igual, sem imprimir hash/chave |
| Smoke HTTP local após reiniciar API | Health 200; sessão anônima 401; CSRF 200; OpenAPI 200; frontend 200; zero paths de proxies |
| Consultas finais do PostgreSQL | Histórico/contagens preservados; nova tabela ausente; zero bancos descartáveis |

Foram adicionados 42 casos unitários e 17 de integração aos 47 testes .NET anteriores.
Cobertura nova: DNS/IPv4/IPv6 e limites; porta; metadados, UUID e timestamps;
estados, vencimento e alocação; envelope inválido/imutabilidade; UTF-8 e espaços;
1000 cifragens paralelas com nonces distintos; adulteração de nonce/ciphertext/tag;
UUID/KeyId autenticados; chave errada/desconhecida/removida, aliases e configuração
inválida; descarte; escrita/leitura entre versões; persistência/materialização;
substituição de envelope; ausência de senha/chaves no registro; nonce duplicado;
adulteração no PostgreSQL; constraints por escritas SQL diretas; upgrade preservando
Identity/auditoria; serviços de autenticação disponíveis sem chave de proxies.

Todos os testes de autenticação anteriores foram executados novamente, incluindo
lockout concorrente, cookies, sessão, logout, papéis, CSRF, criação administrativa,
alteração de senha, auditoria e CLI de provisionamento em bancos descartáveis.
Os sete testes frontend de autenticação também passaram. Não foi realizada nova
homologação de login em navegador real nesta etapa; a evidência Chrome da Etapa 1.1
permanece histórica, e os testes atuais são xUnit/TestServer, PostgreSQL e Vitest.

## Ocorrências corrigidas

- Primeiro build sob sandbox falhou sem diagnóstico; execução autorizada fora
  desse limite passou. Docker também exigiu acesso autorizado ao pipe local.
- Primeiro teste de host revelou que IPAddress aceitava IPv6 entre colchetes;
  a validação passou a recusá-los antes do parser.
- Um teste de DI no projeto unitário não tinha runtime ASP.NET disponível;
  a verificação foi movida para integração, sem adicionar dependência ao domínio.
- Consulta inicial do histórico EF tentou schema identity; corrigida para public.
  Foi somente SELECT e não alterou o banco.
- API local foi parada para liberar os arquivos do build e reiniciada com sucesso.
- Build frontend manteve os dois avisos conhecidos de anotações Zod; concluiu normalmente.
- Certificado ASP.NET continua válido mas sem confiança do Windows, como na Etapa 1.1.
  Smoke HTTPS usou exceção TLS somente no agente temporário local, sem mudar cookies
  nem a confiança do sistema. Não homologa a cadeia TLS.

## Execução local e pendências

Sem aplicar a migration, a autenticação existente continua disponível:

```powershell
./scripts/Start-LocalApi.ps1
# Em outro terminal, em src/central-contingencia-web:
npm run dev
```

API reiniciada e deixada em execução em https://localhost:7090 (sessão 77299).
Frontend existente permanece em https://localhost:5173; PostgreSQL em 127.0.0.1:55432.
Não reprovisionar Admin. A API reiniciada não recebeu a chave de proxies: autenticação
sem ela foi validada. A chave DPAPI criada no exercício permanece em .local/secrets,
sem credenciais reais ou cifragem de registros no banco homologado.

Para carregar a chave numa próxima sessão, siga o try/finally do guia de segurança.
Os testes não dependem dessa chave: usam somente material sintético em memória.

Migration local **aguarda aprovação explícita, novo backup e revisão**. O guia contém
o comando exato, direcionado a AddProxyPersistence. Migrate-LocalDatabase aplica
todas as migrations atuais; não execute por hábito com base no relatório da Etapa 1.

Limitações relevantes registradas na ADR 0003:

- Não há conectividade real de proxies nem garantia de reserva concorrente.
- Nonces aleatórios têm risco residual de colisão; índice protege os envelopes
  persistidos. Orçamento global/monitoramento por chave e eventual gerador durável
  precisam ser definidos antes de escala/produção.
- Host/usuário/região são metadados legíveis; reavaliar antes de dados reais.
- DPAPI é desenvolvimento Windows, não secret manager de produção. Strings/memória
  de processo não têm garantia de apagamento; proteger perfil e backups de chaves.
- Rotação compatível foi testada; job, concorrência, auditoria e operação de backups
  para rotação são futuros, conforme o roteiro documentado.
- PostgreSQL tem precisão de microssegundos; testes permitem esse arredondamento.
- Confiança TLS e demais pendências de produção da ADR 0002 permanecem.

Sem dados reais, produção, integrações externas, commits ou deploys.
