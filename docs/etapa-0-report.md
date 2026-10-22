# Relatório — Etapa 0

AGENTS.md e CLAUDE.md foram lidos integralmente antes das alterações.
O diretório inicialmente continha apenas esses dois arquivos e não era um
repositório Git. Nenhum commit, deploy ou operação em produção foi realizado.

## Ambiente confirmado

| Ferramenta | Versão |
| --- | --- |
| .NET SDK | 10.0.300 |
| ASP.NET Core runtime | 10.0.8 |
| Node | 22.22.3 |
| npm | 10.9.8 |
| Docker | 29.5.2 |
| Docker Compose | 5.1.3 |
| Git | 2.54.0.windows.1 |

Pacotes efetivamente instalados: React/React DOM 19.3.0, TypeScript 5.9.3,
Vite 7.3.7, Tailwind 4.3.3 e Vitest 5.0.3. O package-lock.json fixa a resolução.
ASP.NET Core OpenAPI e Mvc.Testing usam 10.0.8; Npgsql EF Core usa 10.0.0.

## Entregas

- Solution CentralContingencia.slnx, quatro camadas e referências voltadas para dentro.
- Projetos xUnit para arquitetura, integração HTTP e conexão com PostgreSQL real.
- DbContext EF Core/Npgsql vazio, sem entidades, registro na API ou migrations.
- API com /health, OpenAPI em Development e ProblemDetails.
- React/TypeScript/Vite com Tailwind, Lucide, componente Badge na convenção
  shadcn/ui, aliases e configuração de componentes.
- Tela estática responsiva identificada como desenvolvimento, sem dados operacionais.
- Compose PostgreSQL 17 com volume persistente, healthcheck e porta no loopback.
- .gitignore, .env.example, global.json, README, ADR e documentação técnica.
- Script Test-LocalPostgres.ps1 para executar testes usando exclusivamente o banco local.

## Resultados reais

| Comando | Resultado final |
| --- | --- |
| dotnet build CentralContingencia.slnx | Sucesso, zero erros e zero avisos |
| dotnet test CentralContingencia.slnx --no-restore | 5 aprovados e 1 ignorado inicialmente, sem variável PostgreSQL |
| ./scripts/Test-LocalPostgres.ps1 | 6 aprovados, zero falhas e zero ignorados, com PostgreSQL real |
| npm run typecheck | Sucesso |
| npm run lint | Sucesso |
| npm test | 1 aprovado |
| npm run build | Sucesso; artefatos em dist |
| docker compose --env-file .env.example config --quiet | Sucesso |
| docker compose up -d --wait postgres | Sucesso após ajuste de porta |
| docker compose ps | PostgreSQL healthy em 127.0.0.1:55432 |
| dotnet list CentralContingencia.slnx package --vulnerable --include-transitive --no-restore | Nenhuma vulnerabilidade conhecida nas fontes consultadas |
| Instalação npm final com auditoria | Zero vulnerabilidades conhecidas |

Os testes HTTP verificam /health, ausência do CRUD de proxies e disponibilidade
do documento OpenAPI somente em Development. O teste PostgreSQL apenas conecta,
sem criar tabelas ou aplicar migrations. O teste de UI verifica a identificação
de desenvolvimento e a ausência de dados operacionais.

## Problemas encontrados e resolvidos

- Sandbox inicialmente impediu acesso ao pipe Docker e npm estava em modo offline.
  Acesso autorizado permitiu validar Docker e baixar os pacotes.
- Houve resets de conexão durante os downloads npm; as novas tentativas concluíram.
- A porta 5432 estava indisponível no Windows. O padrão local foi alterado para 55432.
- O typecheck inicial apontou ausência dos tipos Node na configuração Vite;
  @types/node foi adicionado e os checks foram repetidos com sucesso.
- O template .NET dependia transitivamente de Microsoft.OpenApi vulnerável.
  A referência direta 2.7.5 removeu o aviso NU1903.
- A auditoria inicial do Vitest 3 identificou três dependências vulneráveis.
  Vitest 5.0.3 foi instalado após confirmar compatibilidade com Node/Vite.

O npm também emitiu avisos de depreciação para eslint 9 e whatwg-encoding,
dependência transitiva de jsdom. Eles não impediram os checks e não foram
apontados como vulnerabilidades na auditoria final.

## Estado e limites

O container local permanece em execução. O .env ignorado contém senha aleatória
exclusivamente local; o volume foi preservado. Use docker compose stop postgres
para parar sem remover dados.

Não houve verificação visual em navegador; a UI foi validada por teste de componente,
typecheck, lint e build. /health verifica somente o processo da API.
Não há autenticação, CRUD, migrations, importação ou integrações externas.
A etapa encerra aqui; funcionalidades seguintes dependem de nova autorização.
