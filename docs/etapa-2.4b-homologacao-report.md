# Homologação das Etapas 2.4B — relatório final

Data: 11/10/2026. Aplicação das duas migrations autorizadas concluída.
**Estado final após retomada autorizada: Etapa 2.4B homologada localmente e pronta
para integração com frontend, dentro do escopo e dos limites descritos abaixo.**
O registro da primeira execução interrompida é preservado nas seções seguintes;
os resultados atuais constam em “Retomada e parecer final”. Migrations não reaplicadas.

## Pré-verificações

Lidos AGENTS.md, CLAUDE.md e relatório de preparação. Confirmado alvo exclusivo
127.0.0.1:55432 / central_contingencia_dev pelo loader existente.
Backup aprovado na preparação revalidado em
`.local/backups/etapa-2.4b-20261011T235137Z`: manifesto SHA-256 íntegro, dump com hash
8F7081ED78E6321B0C46D955AE8D0B8C53526CF25FE8B2CE09BB3C05491BE3B0.
DPAPI e arquivos do keyring idênticos às cópias protegidas.

Antes da aplicação, as contagens/fingerprints das 13 tabelas eram exatamente
iguais ao backup. SQLs conferidos contra os hashes do relatório aprovado:

- 2.4B.1: 26537F30BA30324192868E706CAFA227B0E51C62517B203ED7DA082A4443DA95.
- 2.4B.2: 7F346D1D10717BB5CF3528642B1342E81FE85EB858A6EE649A45EDEA6DC842F8.

CLI EF conectado ao alvo confirmou que somente as duas migrations autorizadas
estavam pendentes, na ordem abaixo. Nenhum valor sensível de configuração foi exibido.

## Execução

Aplicados targets explícitos usando o assembly existente da Etapa 2.4B.3:

1. `20261011223416_AddOperationalProfilesAndAllocations`: comando terminou com
   código 0 / Done. Conferido histórico intermediário com cinco registros e
   esta migration como última, antes de prosseguir.
2. `20261011230449_AddOperationalAudit`: comando terminou com código 0 / Done.

```powershell
# Comandos já executados; não repetir para retomar validação.
dotnet ef database update 20261011223416_AddOperationalProfilesAndAllocations --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
dotnet ef database update 20261011230449_AddOperationalAudit --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
```

Conexão configurada pelo Set-LocalDatabase.ps1 e BaseOutputPath apontando para
`.local/build-2.4b3/`, sem segredos em argumentos. EF obteve seu lock exclusivo
de aplicação. Não foi executada migration adicional, importação ou alteração
manual de registro. Não ocorreu falha nos comandos de migration.

## Validação estrutural

Histórico EF confirmou seis registros, mantendo os quatro anteriores e acrescentando
somente os dois autorizados. As três tabelas novas estão vazias:

| Tabela | Registros |
| --- | ---: |
| profiles.OperationalProfiles | 0 |
| allocations.ProxyAllocations | 0 |
| audit.OperationalEvents | 0 |

Catálogo inspecionado: oito índices secundários, três PKs com seus índices,
doze CHECKs e duas FKs ON DELETE RESTRICT. Todas as constraints listadas estão
validadas. Conferidos filtros Reserved/Confirmed dos dois índices únicos parciais,
unicidade ator/chave, FKs para perfil/proxy e definições dos CHECKs de estado/datas.
Objetos correspondem aos SQLs aprovados.

## Falha da verificação e diagnóstico

O script Compare-LocalDatabaseSnapshot.ps1 retornou:
`Contagens ou fingerprints divergentes; interromper operação.`

Causa: consulta todas as tabelas de identity/audit/proxies, mas o snapshot anterior
não contém a tabela nova audit.OperationalEvents. A comparação do conjunto completo
considerou sua adição, ainda vazia, uma divergência. Essa chamada deveria ter
comparado somente as tabelas do baseline e verificado objetos novos separadamente.
**Foi um falso positivo do comparador, não alteração de registros antigos.**

A execução foi interrompida antes da identificação/reinicialização da API. Após
informar a falha, executado somente diagnóstico em leitura, sem recuperação:

- Selecionadas as 13 tabelas presentes no baseline do backup.
- Para __EFMigrationsHistory, comparado o conjunto anterior de quatro registros,
  desconsiderando somente as duas inserções explicitamente autorizadas; seu
  fingerprint original foi preservado. O histórico completo de seis já foi conferido.
- **Contagens e fingerprints das 13 tabelas anteriores idênticos**, com essa
  mudança intencional e validada do histórico EF.
- As únicas tabelas adicionais são as três previstas e todas possuem zero linhas.
- Backup revalidado novamente; DPAPI e keyring permanecem idênticos ao backup.

Não foi corrigido o script nesta execução nem realizado rollback, restore ou
outro procedimento de recuperação. Nenhuma recuperação de dados é indicada
pelo diagnóstico atual. A correção do comparador deve limitar a comparação aos
nomes do baseline, exigir que todos continuem presentes, validar separadamente
novos objetos autorizados e tratar explicitamente as inserções no histórico EF.

## Resultados e pendências da execução inicial — histórico

| Item | Situação |
| --- | --- |
| Pré-verificações exigidas | Aprovadas |
| Aplicação das duas migrations | Concluída, códigos 0 |
| Histórico, tabelas, índices, CHECKs e FKs | Conferidos |
| Dados anteriores e chaves | Preservados no diagnóstico em leitura |
| Comparador automatizado | Falso positivo identificado; correção pendente |
| Reinicialização da API | Não executada após interrupção |
| Health checks e smoke tests de runtime | Não executados nesta aplicação |
| Testes com escrita em banco descartável | Não repetidos após interrupção |
| Homologação funcional completa | Pendente |

A preparação anterior reexecutou 70 testes operacionais, aprovados; a Etapa 2.4B.3
registrou 361 testes .NET aprovados. São evidências anteriores, não resultados de
testes pós-aplicação desta execução.

Para concluir, aguardar orientação/autorização de continuação: corrigir o comparador,
repetir verificações de preservação sem reaplicar migrations, reiniciar API com
configurações/chaves existentes, validar health e endpoints, e repetir os fluxos
de expiração/idempotência/concorrência/rollback em banco descartável. No banco
homologado manter verificações sem alterar registros operacionais.

Nenhuma chave regenerada, registro operacional modificado, importação CSV, mudança
frontend, commit, deploy ou Etapa 2.4C iniciada.
Na interrupção inicial a etapa não foi declarada homologada. Esse estado foi
substituído pela conclusão da retomada autorizada, registrada a seguir.

## Retomada e parecer final

Retomada explicitamente autorizada pelo usuário. AGENTS.md, CLAUDE.md e o registro
acima foram relidos. Nenhum database update, downgrade ou rollback foi executado
no PostgreSQL homologado nesta retomada. **Etapa 2.4B homologada localmente:** schema,
preservação, runtime e fluxos operacionais aprovados; pronta para integração frontend.
Etapa 2.4C não iniciada. Nenhuma alteração visual, commit ou deploy.

### Correção do comparador

Arquivos modificados/criados: scripts/Compare-LocalDatabaseSnapshot.ps1,
scripts/DatabaseSnapshot.ps1 e scripts/Test-DatabaseSnapshot.ps1.

O comparador agora consulta todas as tabelas de aplicação, incluindo public,
profiles e allocations, em vez de apenas identity/audit/proxies. Exige o conjunto
original completo e rejeita qualquer adição fora da lista explícita autorizada.
As três tabelas novas devem existir, estar vazias e ter estrutura igual à referência
construída a partir do SQL aprovado. Não existe ignorar genericamente tabelas desconhecidas.

Contagens/fingerprints dos dados originais continuam obrigatórios. Para o histórico EF,
somente os dois IDs aprovados são separados da comparação; seus registros completos
e ProductVersion=10.0.8 são exigidos. Assim, alteração de migration antiga, inserção
extra, ausência ou versão divergente continuam sendo recusadas. ExcludeTables, mantido
por compatibilidade, só admite tabelas já presentes no baseline e nunca exclui estrutura;
nenhuma exclusão de dados foi usada nesta homologação.

Novos snapshots também guardam fingerprints estruturais de colunas/tipos/defaults,
nulabilidade, CHECKs/PKs/FKs e definições/validade dos índices. Alteração estrutural,
tabela removida, snapshot duplicado/malformado ou referência insuficiente falham fechados.
Artefatos ficam restritos ao diretório local ignorado pelo Git.

O baseline anterior não registrava estrutura. Seu schema foi reconstruído do archive
do backup em banco descartável, sem restaurar dados. Objetos novos esperados foram
criados apenas nessa referência descartável, a partir dos SQLs revisados, sem registrar
migrations no banco homologado. Comparação inicial identificou diferença textual de
CHECKs causada pela reinterpretação normal do PostgreSQL ao restaurar schema:
parênteses de AND/BETWEEN e casts equivalentes de arrays. Não era mudança real de regra.

Para evitar esse segundo falso positivo, schemas esperado e atual passaram pelo
mesmo ciclo schema-only de dump/restore em bancos descartáveis. Isso usa o parser
PostgreSQL, sem remover parênteses/casts por regex ou enfraquecer regras. A referência
canônica do schema atual é vinculada a um snapshot bruto de origem; se qualquer
estrutura atual mudar depois de sua geração, a comparação recusa a referência
desatualizada. Todos os bancos e arquivos temporários no container foram removidos.

**14 regressões aprovadas**: adição autorizada vazia, tabela desconhecida/ausente,
mudança de contagem, mudança de fingerprint, tabela original removida, estrutura
original/nova divergente, linhas inesperadas nas tabelas novas, histórico divergente,
snapshot duplicado/malformado e referência canônica desatualizada.

### Evidências de preservação e schema

Antes do restart e novamente depois dos testes: **contagens e fingerprints das 13
tabelas originais preservados**, considerando as duas adições autorizadas ao histórico
EF. As estruturas originais também passaram na comparação com o backup. As três
tabelas novas permanecem vazias e compatíveis com o SQL esperado, incluindo oito
índices secundários, três PKs, doze CHECKs e duas FKs RESTRICT. O histórico continua
com seis migrations, sem outras adições. Usuários=4, auditorias de autenticação=36,
proxies=1, auditorias de proxies=1 e lotes/linhas CSV=0/0 permanecem iguais.

DPAPI, duas chaves do keyring e manifesto do backup revalidados, sem alteração de
bytes, regeneração ou substituição. AES-256-GCM: roundtrip sintético, nonces distintos
e rejeição de adulteração aprovados. O único envelope homologado foi autenticado
em READ ONLY com buffer plaintext temporário zerado; nada sensível foi exibido ou salvo.
Keyring copiado validou roundtrip Data Protection com geração de chaves desabilitada.

Baseline novo, completo, para futuras verificações:
`.local/24b-final-snapshot.txt` e `.local/24b-final-snapshot.txt.structure.txt`.
Referências legadas reconstruídas e evidências ficam em `.local/24b-*-structure.txt`.
Backup original preservado no caminho da seção inicial.

### Runtime e smoke tests

API reiniciada com conexão, configurações e chave DPAPI existentes, usando assembly
2.4B.3. PID observado=31320; HTTPS https://localhost:7090, HTTP http://localhost:5041.
Serviço permanece em execução para avaliação local. Frontend não foi alterado.

No banco homologado: health=200, OpenAPI=200 com endpoints novos presentes e
consultas protegidas anônimas=401 (auth/me, proxies, profiles, allocations e histórico).
Não foi feito login de teste na homologação, pois gravaria auditoria de autenticação.

Fluxos autenticados e com escrita foram exercitados por HTTP HTTPS real em outra
instância do mesmo assembly, https://localhost:7091, usando banco e role descartáveis
com bloqueio de acesso à tabela de auditoria homologada. Dados e credenciais sintéticos;
chave AES de teste em memória. Banco/role/servidor descartáveis removidos ao terminar.

| Smoke test | Resultado |
| --- | --- |
| Login, sessão Admin, CSRF e logout | Aprovado: 204 / sessão válida / logout seguido de 401 |
| Cadastro e consulta de proxy | Aprovado: 201, usernameMasked=***, sem senha |
| Cadastro de dois perfis | Aprovado: 201 |
| Reserva e indisponibilidade enquanto ocupada | Aprovado: 201 e available=false |
| Confirmação e repetição idempotente | Aprovado: 204 / 204 |
| Consulta de histórico confirmado | Aprovado: 200, estado Confirmed |
| Liberação e disponibilidade/reutilização | Aprovado: 204 e available=true |
| Reserva vencida e confirmação proibida | Aprovado: 409 e histórico Expired |
| Duas reservas simultâneas da mesma proxy | Aprovado: um 201 e um 409 |
| Mutação sem CSRF | Aprovado: 400, sem liberação |

Evidência sanitizada: `.local/24b-smoke-results.json`. Harnesses locais:
`.local/run-24b-smoke.ps1` e `.local/smoke-24b.cjs`, reutilizando BrowserFixture
existente para isolar banco/role. Não houve automação ou implementação de frontend.

Respostas verificadas em memória contra senha sintética do usuário, senha de proxy,
username sensível de teste e chave AES de teste. Logs das duas instâncias verificados
contra esses valores; logs também conferidos contra a chave AES real existente, sem
imprimi-la. Nenhum deles apareceu em corpo HTTP/logs examinados. Cookies de sessão
e token CSRF continuam mecanismos previstos; não são publicados nas evidências.

### Builds e testes automatizados

| Verificação nesta retomada | Resultado |
| --- | --- |
| Regressões PowerShell do comparador | 14 aprovadas |
| Suíte unitária .NET completa | 200 aprovados, zero falhas/ignorados |
| Suíte integração .NET completa | 161 aprovados, zero falhas/ignorados |
| Build final .NET isolado | Aprovado, zero erros/avisos |
| Build do harness existente | Aprovado, zero erros/avisos |
| Smoke HTTPS real | Aprovado |
| Comparação final de dados/estruturas e chaves | Aprovada |
| Bancos/roles descartáveis remanescentes | Zero |

**361 testes .NET** cobrem autenticação/CSRF/autorização, proxies/CSV, criptografia,
expiração, recuperação concorrente, idempotência, histórico, auditoria e rollback.
Falhas de auditoria são provocadas somente nos bancos descartáveis e comprovam HTTP
500 sanitizado com rollback de criação/edição/fechamento/reserva/confirmação/liberação/
expiração, inclusive da expiração salva antes de inserir nova reserva.

Uma tentativa adicional de build usou a pasta de DLLs da API já em execução e falhou
por bloqueio de arquivo Windows (MSB3021/MSB3027). Corrigido usando saída isolada
`.local/build-24b-homologacao-final/`, sem encerrar ou substituir o servidor. Não houve
divergência de dados nem correção de código de produto nessa ocorrência.

Comandos principais:

```powershell
./scripts/Test-DatabaseSnapshot.ps1
. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES=$env:ConnectionStrings__DefaultConnection
try {
  dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b3/
} finally { Remove-Item Env:CENTRAL_TEST_POSTGRES }
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-24b-homologacao-final/
```

Comparação legada executada com SnapshotPath apontando ao database-fingerprints.txt
do backup, StructureSnapshotPath=24b-original-structure.txt,
ExpectedStructurePath=24b-authorized-structure.txt e
CanonicalStructurePath=24b-live-canonical-structure.txt, além das três tabelas
e dois IDs explicitamente aprovados. A referência foi produzida por
`.local/validate-24b-schema.ps1`; não reutilizar referência canônica desatualizada.
Para o novo baseline completo, não há adições a autorizar:

```powershell
./scripts/Compare-LocalDatabaseSnapshot.ps1 -SnapshotPath C:/PROJETOS/CENTRALCONTINGENCIA/.local/24b-final-snapshot.txt
```

### Riscos residuais e limites

- Homologação é local. Escritas funcionais foram validadas em banco descartável;
  registros operacionais homologados permaneceram intactos. Não houve teste manual
  do usuário, navegador ou validação de frontend nesta etapa backend.
- Smoke HTTPS local aceita certificado de desenvolvimento; isso não certifica TLS
  ou infraestrutura de produção. Keyring/DPAPI dependem do contexto Windows original.
- Lock de tabela serializa mutações de alocação; não foi feito benchmark de carga.
  Expiração física é sob demanda, embora vencidas já não bloqueiem disponibilidade.
- Reserve/release mantêm 409 em repetição. Não orientar retry cego de POST de proxy:
  risco R4 de leitura de disponibilidade pós-commit permanece documentado.
- Fingerprints estruturais cobrem colunas/defaults/constraints/índices, não são auditoria
  exaustiva de todas as permissões, funções ou objetos globais do PostgreSQL. Nenhuma
  migration executada alterou esses objetos. Backup completo/globals permanece disponível.
- Não realizado ensaio de restauração dos dados do backup; somente schema descartável
  e leitura/decodificação integral do archive. Não aplicar Down automaticamente.
- Nenhuma integração externa, importação real, migration adicional, mudança de chave,
  commit, deploy ou Etapa 2.4C foi iniciada.

**Parecer final: homologação local da Etapa 2.4B aprovada e backend pronto para
integração com frontend. Aguardar nova autorização para iniciar a próxima etapa.**
