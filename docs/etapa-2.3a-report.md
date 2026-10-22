# Relatório — Etapa 2.3A: Backend de Pré-visualização CSV

Data: 11/10/2026. Implementação e validação concluídas.

AGENTS.md, CLAUDE.md e relatórios 2.1, 2.2A e 2.2B lidos integralmente.
Inspecionados agregado/Value Objects, regras da Application, adapter AES, store EF,
Identity/CSRF, endpoints, OpenAPI e fixtures antes de implementar. Plano breve
apresentado. Versões confirmadas nesta execução: .NET SDK 10.0.300 e Node 22.22.3.
EF Core/Identity 10, PostgreSQL e dependências existentes preservados; nenhum pacote
adicionado. Sem refatoração visual, novos componentes React ou mudança de layout.

## Implementação

- POST /api/v1/proxies/import/preview: exclusivo Admin, autorização também na
  Application, cookies/CSRF/no-store e rate limiting sensitive existentes.
- Upload multipart com exatamente um arquivo file; paginação via query page/pageSize.
  Leitura manual do stream em memória, sem ReadForm, IFormFile binding ou arquivos
  temporários. Limites configuráveis e validados no startup: 2 MiB, 5000 registros,
  10000 linhas físicas, 4096 caracteres por campo e 32 colunas, por padrão.
- Kestrel amplia limite somente nessa rota; demais corpos JSON continuam 16 KiB.
  Stream contador protege também uploads sem Content-Length; overhead 16 KiB e
  limites adicionais para boundary/headers multipart. Buffers de bytes zerados
  no finally; strings gerenciadas não garantem apagamento físico.
- Parser UTF-8 estrito com/sem BOM; delimitador ; ou , fora das aspas; escape "",
  delimitadores dentro de campos, multilinha e LF/CRLF/CR. Erros de estrutura/UTF-8
  são sanitizados, sem conteúdo original ou exceções do decoder expostas.
- Mapeamento explícito com normalização de acentos/caixa/espaços. Cabeçalhos
  ausentes/duplicados bloqueiam a validação de linhas; desconhecidos geram avisos
  sem ecoar seus nomes. Posições e linhas físicas originais são preservadas.
- Host/porta e metadados reutilizam as invariantes do domínio. Status/vencimento
  reutilizam as regras de criação da Application. Senha é validada em memória pelo
  mesmo limite de 1024 bytes UTF-8; não é cifrada nem persistida na prévia.
- Duplicatas internas por host canônico + porta + usuário exato, sem usar PERFIL
  ou senha como identidade. HMAC por request evita guardar usuário em claro nas
  chaves de comparação; material aleatório e buffers descartados ao fim.
- Leitor EF específico consulta possíveis conflitos de host/porta com projeção
  SQL somente desses campos, sem entidades/credenciais/envelopes; lotes de 100.
  Conflitos são avisos, pois host/porta não são únicos no modelo atual.
- Resumo de todo o arquivo: total, válidos, inválidos, duplicados, vazios, conflitos
  e avisos. Duplicados/vazios integram inválidos; conflitos podem continuar válidos.
  Paginação limita items, não altera resumo. Cada página exige reenvio do arquivo.
- Resposta com host/usuário/senha mascarados, porta/status/vencimento tipados,
  flags de presença dos demais metadados, erros e avisos por linha/coluna. Sem nomes
  de perfis/plataformas/regiões/lotes, valores inválidos ou filenames ecoados.
- Texto semelhante a fórmula é tratado como texto e gera aviso; nenhuma fórmula,
  comando, macro, URL ou integração é executada.
- OpenAPI inclui multipart/file binary, query limitada, cookie, CSRF e códigos HTTP.
  Schema/validações de linha retornam 200 com diagnóstico; arquivo vazio/malformado
  ou UTF-8 inválido 400, limites 413, tipo incorreto 415, anônimo 401 e não-Admin 403.

Não há SaveChanges no fluxo, CSV original em banco/arquivo, credenciais persistidas,
cache compartilhado, token, confirmação, importação definitiva ou auditoria de
conteúdo. Nenhuma migration criada ou aplicada nesta etapa. Autenticação, AES,
CRUD e UI existentes preservados, com regressão completa.

## Arquivos alterados/criados

| Área | Arquivos |
| --- | --- |
| Domain | Proxies/ProxyRules.cs (novo); Proxies/Proxy.cs |
| Application | Proxies/ProxyInputRules.cs (novo); Proxies/ProxyManagementService.cs; Proxies/Import/PreviewContracts.cs, BoundedCsvParser.cs, ProxyCsvPreviewService.cs (novos) |
| Infrastructure | Proxies/EfProxyImportConflictReader.cs (novo); Proxies/AesGcmProxyPasswordProtector.cs; DependencyInjection.cs |
| Api | Proxies/ProxyImportPreviewEndpoint.cs (novo); Program.cs; appsettings.json |
| Testes | UnitTests/CsvPreviewTests.cs e IntegrationTests/CsvPreviewEndpointTests.cs (novos); IntegrationTests/ApiTests.cs |
| Documentação | AGENTS.md, CLAUDE.md, README.md; docs/api/README.md, proxy-csv-preview.md; docs/architecture/README.md; docs/decisions/0006-proxy-csv-preview.md; este relatório |

Caminhos de código relativos ao respectivo projeto src/CentralContingencia.*;
testes relativos a tests/CentralContingencia.*. Extração de regras limitada ao
reuso solicitado; dependências DDD/Clean Architecture permanecem iguais.
Artefatos de build/smoke isolados ficam em .local, ignorados. Nenhum arquivo frontend
fonte/configuração foi alterado; seu build apenas regenerou dist ignorado.

## Builds e testes executados

| Comando/verificação | Resultado final |
| --- | --- |
| dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3a/ | Sucesso, 0 erros/avisos |
| dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3a/ com CENTRAL_TEST_POSTGRES configurado em memória | 217 aprovados: 118 unitários + 99 integração, 0 falhas/ignorados |
| npm run lint | Aprovado |
| npm run typecheck | Aprovado |
| npm test | 63 aprovados |
| npm run build | Aprovado, dois avisos conhecidos de comentários PURE da dependência Zod/Rollup |
| Smoke Kestrel isolado em https://localhost:7092 | Health/OpenAPI 200; file binary/CSRF documentados; preview anônimo multipart e JSON 401 |
| Smoke dos servidores existentes | API health/OpenAPI/CSRF e frontend 200; sessão e consultas anônimas 401 |
| Consulta final de fixtures | Nenhum banco p6_auth_test_* remanescente |

64 novos casos .NET: 51 unitários e 13 integração. Cobertura inclui todos os
cenários exigidos: válido/vazio, cabeçalhos missing/duplicados/desconhecidos,
acentos/aliases, BOM/UTF-8 inválido, vírgula/ponto e vírgula, aspas/escape/delimitador
interno/multilinha, porta/data/status/metadados inválidos, senha acima do limite de
bytes, duplicatas sem confundir perfil, conflitos EF e múltiplos lotes, linhas
vazias/numeração/paginação, limites de bytes/linhas/campos/colunas, cancelamento,
multipart incorreto/múltiplas seções, upload sem Content-Length, anônimo/papéis,
CSRF ausente/adulterado/anterior ao login, rota com barra final, no-store,
OpenAPI, ausência de senha/usuário nos corpos/erros/logs e contagens sem gravação.

Fixtures usam exclusivamente dados sintéticos e bancos p6_auth_test_UUID,
criados/apagados pelo harness existente. Migrations existentes aplicadas somente
nessas fixtures. Os 153 testes .NET anteriores e os 63 frontend passaram novamente.
A inspeção de logs testa o material observado, não garante futuras configurações
de logging ou comportamento de reverse proxies externos.

## Ocorrências corrigidas

- OpenAPI 4 utiliza strings nas propriedades Minimum/Maximum; primeiro build da
  documentação de query usou int e foi corrigido. Harness de logs precisou importar
  a extensão IWebHostBuilder.ConfigureLogging.
- Primeira integração revelou limite de linhas lido antes dos overrides do host.
  Leitura da configuração passou ao singleton resolvido após builder.Build, com
  validação explícita antes de aceitar requests.
- Assertion de cache ajustada para checar NoStore: antiforgery acrescenta no-cache.
- Metadata Accepts fazia tipo incorreto cair no fallback de roteamento antes da
  política. Foi removida; handler verifica MIME após Admin/CSRF, e OpenAPI recebe
  requestBody explícito. O smoke Kestrel confirmou 401 anônimo inclusive para JSON.
- Um patch de edição foi recusado por contexto fora de ordem; reapresentado e
  aplicado. Todas as rodadas finais acima passaram.

## Preservação do banco e serviços

Somente contagens foram consultadas no PostgreSQL homologado, sem ler conteúdo de
proxies existentes, senhas ou dados de usuários. Estado observado antes/depois:

| Item | Antes | Depois |
| --- | ---: | ---: |
| Usuários | 4 | 4 |
| Auditorias de autenticação | 32 | 32 |
| Proxies | 1 | 1 |
| Auditorias de proxies | 1 | 1 |
| Migrations | 3 | 3 |

Nenhuma escrita homologada foi executada. A API isolada de smoke fez somente
health/OpenAPI e solicitações anônimas; não acessou contas nem realizou login.
Foi encerrada ao final. TLS autoassinado ignorado apenas nesse cliente local;
confiança do Windows não foi alterada.

Servidores anteriores em 7090/5173 foram preservados; build usou saída isolada
para não substituir DLLs em uso. **A API que já estava em execução não foi
reiniciada e ainda precisa carregar a nova compilação para disponibilizar a prévia
nessa porta.** O endpoint novo foi exercitado integralmente em TestServer com
PostgreSQL real descartável; startup/OpenAPI/401 também em Kestrel isolado.
Não houve teste de interface CSV no Chrome, pois frontend novo não faz parte do escopo.

## Execução local

Após encerrar a instância anterior da API, iniciar uma nova sessão na raiz:

```powershell
. ./scripts/Set-LocalProxyEncryption.ps1
try { ./scripts/Start-LocalApi.ps1 }
finally {
  Remove-Item Env:ProxyEncryption__ActiveKeyId -ErrorAction SilentlyContinue
  Remove-Item Env:ProxyEncryption__Keys__development-v1 -ErrorAction SilentlyContinue
}
```

Chave existente somente necessária para CRUD com senha, não para prévia. Não
gerar outra chave, reprovisionar contas ou aplicar migrations. Frontend atual
permanece https://localhost:5173; API padrão https://localhost:7090. Não há UI
de importação. Para testar HTTP, autentique como Admin, renove CSRF e envie file
multipart com arquivo sintético conforme docs/api/proxy-csv-preview.md. Nunca
usar comandos com senha literal, corpo em logs, HAR/traces ou arquivos reais.

Reproduzir os testes preservando saídas isoladas e segredos só em memória:

```powershell
. ./scripts/Set-LocalDatabase.ps1
$previousTestConnection = $env:CENTRAL_TEST_POSTGRES
try {
  $env:CENTRAL_TEST_POSTGRES = $env:ConnectionStrings__DefaultConnection
  dotnet test CentralContingencia.slnx --no-restore `
    -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.3a/
}
finally {
  $env:CENTRAL_TEST_POSTGRES = $previousTestConnection
  Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
}
```

## Incompatibilidades, limitações e próxima etapa

- PERFIL/PLATAFORMA/ADsPower Lote reconhecidos, mas não existem campos/vínculos no
  modelo atual; não foram inventadas entidades ou integrações. Limites estruturais
  desses metadados não equivalem a regras de domínio de perfis.
- REGIÃO opcional no arquivo e obrigatória no domínio; registros sem região são
  inválidos. Não se assumiu país/região padrão.
- Datas de planilha sem hora usam meia-noite UTC. Confirmar timezone/demais
  formatos antes de dados reais; vencidos são inválidos conforme criação atual.
- PERFIL não identifica proxy. Critérios internos de duplicata e conflitos são
  conservadores; conflitos não garantem unicidade/alocação/reserva. Consultas podem
  mudar por concorrência; a prévia não promete estado persistente de confirmação.
- Todos os metadados textuais retornados são mascarados/representados por presença.
  Nenhum dado original é armazenado para troca de página; reupload obrigatório.
- Parser limitado ao dialeto documentado; não suporta Excel binário, UTF-16,
  Latin-1, delimitador tab, comentários ou aspas arbitrariamente tolerantes.
- MFA, TLS confiável, logging/limites do futuro host de produção e demais condições
  das ADRs anteriores permanecem pendentes; não houve deploy.

**Backend pronto para iniciar a Etapa 2.3B, após autorização específica de escopo.**
Nenhuma implementação da próxima etapa foi iniciada. Importação definitiva,
confirmação/persistência, migrations homologadas, integrações, dados reais, commits
e deploys continuam fora do escopo.
