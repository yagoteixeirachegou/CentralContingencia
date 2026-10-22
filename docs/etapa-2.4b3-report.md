# Etapa 2.4B.3 — Confiabilidade das reservas e auditoria

Data: 11/10/2026. Implementação e testes concluídos; **aguardando revisão antes
da homologação**. Não foram aplicadas migrations nem modificados registros do
banco homologado. Nenhum frontend, integração, importação real, commit ou deploy.

## Inspeção e decisões

Lidos integralmente AGENTS.md, CLAUDE.md e revisão 2.4B.2. Inspecionados domínio,
Application, repositórios EF, disponibilidade de proxies, DbContext, auditoria,
endpoints/OpenAPI e testes. SDK .NET 10.0.300. Preservadas as dependências DDD/Clean
Architecture e o mecanismo AES-256-GCM/DPAPI; nenhuma chave lida, regenerada ou trocada.
ADR: [0010-reservation-reliability.md](decisions/0010-reservation-reliability.md).

Recuperação transacional sob demanda atende à disponibilidade/reutilização sem
worker. Consultas continuam sem efeitos persistentes. O banco usa os estados,
timestamps, motivo e evento de expiração já previstos, sem alterar o schema.

## Correções implementadas

- `ProxyAllocations:ReservationMinutes`: padrão 15 minutos, configuração validada
  entre 1 e 1440. Configuração inválida impede inicialização. A variável equivalente
  é `ProxyAllocations__ReservationMinutes`; não contém segredo.
- `reservationExpiresAt` passou a ser opcional em reserve. O prazo é calculado após
  os locks, limitado à validade da proxy. Prazo explícito menor permanece aceito;
  prazo superior ao TTL/validade ou não futuro é recusado. Não prolonga proxies.
- Availability em listagem, disponíveis, detalhe e respostas de proxies desconsidera
  Reserved quando `ReservationExpiresAt <= agora`. Confirmed continua ocupando.
- Reserve, confirm, release, expire interno e close recuperam reservas vencidas dos
  recursos envolvidos. Expiração usa o serviço de domínio e preserva histórico.
- Recuperação e operação têm uma única transação. SaveChanges da expiração antecede
  INSERT de nova reserva para liberar os índices parciais; não existe commit intermediário.
- As mutações obtêm SHARE ROW EXCLUSIVE na tabela de alocações antes de FOR UPDATE
  perfil/proxy. Recuperação/reutilização concorrentes não duplicam finalizações.
  Índices únicos parciais de proxy e perfil e unicidade por ator/chave permanecem.
- Confirmação de reserva vencida retorna 409, mantendo Expired e sua auditoria.
  Não cria alocação confirmada. Repetição da confirmação de vínculo ainda Confirmed
  retorna 204 sem mudar datas, criar vínculo ou duplicar evento, inclusive depois
  do vencimento/bloqueio posterior da proxy. Recursos diferentes/Released/Expired/
  perfil encerrado continuam conflitantes.
- Falhas internas de persistência da auditoria têm categoria própria AuditFailure,
  HTTP 500 e ProblemDetails estático. Não retêm SQL, parâmetros ou exceção interna.
  Negócio e auditoria sofrem rollback juntos. CHECK/FK de negócio e conflitos
  mantêm classificação própria, sem converter indiscriminadamente erros para 500.
- Atualizados OpenAPI, contrato REST, AGENTS.md e CLAUDE.md. Endpoints e políticas
  continuam existentes: Admin/Manager escrevem; Operator consulta; cookies/CSRF.

## Política de expiração e auditoria

No instante exato do prazo, a reserva deixa de ocupar logicamente o recurso.
GETs não recuperam fisicamente nem auditam: Status pode ainda ser Reserved com
prazo vencido até uma mutação tocar o recurso. Esse comportamento é documentado,
e não exige agendamento para liberar disponibilidade ou permitir nova reserva.

A recuperação grava Expired, EndReason=ReservationExpired e EndedAt/UpdatedAt.
Auditoria grava AllocationExpired, UUID da reserva/perfil e horário UTC efetivo
da recuperação. A ação identifica expiração; o motivo detalhado está no histórico
referenciado pelo UUID. Ator de sistema reservado
`00000000-0000-0000-0000-000000000001`, correlation ID `reservation-expiry`.
Não é conta Identity nem papel/autorização. Não registra credenciais ou payloads.

ExpireAsync interno também audita e é idempotente após Expired. Fechar perfil com
reserva vencida conserva o motivo ReservationExpired; não o troca por ProfileClosed
nem gera liberação duplicada. Fechar perfil com vínculo ainda ativo conserva a regra
anterior de finalização coordenada. Confirmed não expira pelo prazo da antiga reserva.

Erros normais de negócio desfazem também a recuperação da transação. A rejeição
confirm/release de vínculo expirado é tratada explicitamente após o commit da
recuperação auditada, retornando 409. Falha de auditoria nesse caminho retorna 500
e impede o commit, mantendo o estado anterior.

## Comportamento HTTP

| Situação | Resultado |
| --- | --- |
| Nova reserva válida | 201, prazo efetivo na resposta |
| Prazo explícito inválido ou acima dos limites | 400 |
| Primeira confirmação válida | 204 |
| Reconfirmação de vínculo ainda Confirmed | 204, sem novo evento |
| Confirmação vencida ou conflitante | 409 |
| Release de reserva já vencida | 409 após recuperação auditada |
| Falha interna de auditoria | 500 sanitizado, rollback |
| Indisponibilidade geral de persistência | 503 |
| Conflito de exclusividade/concorrência/timeout de lock | 409 |
| Sem autenticação / papel insuficiente | 401 / 403 |
| CSRF inválido | 400 |

Reserve com chave repetida e release repetido mantêm 409; não foi adicionado replay
dessas operações. Contrato completo em
[operational-profiles-allocations.md](api/operational-profiles-allocations.md).

## Arquivos envolvidos

Domain: AllocationDomainService (replay de confirmação).
Application/Allocations: ReservationOptions novo; AllocationPersistenceContracts;
OperationalManagementService. Application/Proxies: ProxyContracts e
ProxyManagementService (instante explícito na verificação de ocupação).
Infrastructure: EfAllocationStore, AllocationPersistenceErrors, EfProxyStore,
DependencyInjection. API: OperationalEndpoints, Program e appsettings.json.
Testes: novos ReservationReliabilityTests nos dois projetos; regressões em
OperationalEndpointTests e ajuste da assinatura do fake em ProxyApplicationTests.
Documentação: este relatório, ADR 0010, contrato REST, AGENTS.md e CLAUDE.md.
Nenhum código-fonte frontend foi alterado.

## Verificações e resultados reais

| Verificação | Resultado final |
| --- | --- |
| Build .NET completo | Aprovado, zero erros/avisos |
| Testes unitários .NET | 200 aprovados, zero falhas/ignorados |
| Testes integração PostgreSQL/HTTP | 161 aprovados, zero falhas/ignorados |
| Revalidação dedicada do OpenAPI após novas asserções | 1 aprovado |
| EF has-pending-model-changes | Nenhuma mudança desde a última migration |
| Frontend lint | Aprovado |
| Frontend typecheck | Aprovado |
| Frontend Vitest | 104 aprovados em 4 arquivos |
| Frontend build | Aprovado; dois avisos Rollup em comentários de dependência Zod |
| Comparação homologada em leitura | Contagens/fingerprints preservados |
| Bancos descartáveis restantes após testes | 0 |

Suite .NET completa: **361 testes**, preservando as regressões de autenticação,
CSRF, autorização, criptografia/adulteração, proxies e CSV. Cobertura nova inclui
TTL padrão/configurado/limites, disponibilidade sem escrita, recuperação simultânea,
confirmações concorrentes/idempotentes, corrida de confirmação vencida com reutilização,
expiração explícita concorrente, fechamento do perfil, histórico e motivo preservados.

Falhas provocadas de auditoria cobrem ProfileCreated, ProfileUpdated, ProfileClosed,
AllocationReserved, AllocationConfirmed, AllocationReleased e AllocationExpired.
Verificado rollback de reserva original, criação/reutilização, confirmação, atualização
e encerramento. Falha do evento da nova reserva também desfaz a expiração já salva
na mesma transação. Respostas sanitizadas não expõem constraint/SQL/usuário/envelope;
auditorias não recebem credenciais. Não foi ativado logging de valores sensíveis.

A primeira execução detectou somente uma comparação de timestamp com precisão
superior à do PostgreSQL em teste novo (155/156 integrações aprovadas). A comparação
foi corrigida para microssegundos e a suíte completa reexecutada, com resultado
final acima. Aviso inicial EF1002 em DDL de falha sintética também foi resolvido:
DDL usa somente UUID gerado e ação fixa do teste, sem entrada do usuário.

Comandos executados:

```powershell
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b3/

. ./scripts/Set-LocalDatabase.ps1
$env:CENTRAL_TEST_POSTGRES = $env:ConnectionStrings__DefaultConnection
try {
  dotnet test CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b3/
} finally { Remove-Item Env:CENTRAL_TEST_POSTGRES }

$env:BaseOutputPath = 'C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4b3/'
try {
  dotnet ef migrations has-pending-model-changes --project src/CentralContingencia.Infrastructure --startup-project src/CentralContingencia.Infrastructure --no-build
} finally { Remove-Item Env:BaseOutputPath }

# Dentro de src/central-contingencia-web:
npm run lint
npm run typecheck
npm test
npm run build
```

CENTRAL_TEST_POSTGRES apenas fornece a conexão à fixture, que cria bancos
`p6_auth_test_<UUID>` e aplica migrations exclusivamente nesses bancos. Valores
de conexão não foram impressos. A fixture restringe alvo local e remove o banco
descartável ao terminar. Testes HTTP usam hosts isolados, não a API homologada.

## Migrations e preservação da homologação

**Nenhuma migration adicional necessária ou gerada.** Permanecem pendentes:

1. `20261011223416_AddOperationalProfilesAndAllocations`.
2. `20261011230449_AddOperationalAudit`.

Somente leitura confirmou quatro migrations homologadas, última
`20261011165123_AddProxyImportBatches`, e ausência das tabelas OperationalProfiles,
ProxyAllocations e OperationalEvents. A comparação de contagens/fingerprints dos
schemas existentes identity/audit/proxies com snapshot anterior, incluindo tabelas
CSV, passou novamente ao terminar. Snapshot local em `.local/24b3-before.txt`,
sem conteúdo de registros no relatório. Não executado migration update homologado.

API homologada não foi reiniciada com esta versão, pois o schema continua pendente.
Chaves AES/DPAPI/keyring e os registros anteriores foram preservados. A compatibilidade
do mecanismo de criptografia foi exercitada pela suíte existente, sem usar chaves reais.

## Riscos residuais e revisão

- O lock de tabela serializa mutações de alocação em recursos independentes. Solução
  simples e segura no volume atual; não foi realizado benchmark. Timeout mantém 409.
- Sem tráfego de escrita, estados vencidos podem permanecer Reserved fisicamente;
  disponibilidade já os ignora. Não há evento até a recuperação efetiva. Consultas
  não constituem reserva e totais/itens continuam snapshots separados.
- Reserve/release não ganharam replay idempotente. Confirmação repetida reconhece
  somente vínculo ainda Confirmed; vínculo encerrado mantém conflito.
- R4 da revisão anterior (leitura de disponibilidade após commit de POST/PATCH de
  proxy) permanece fora desta correção específica. Não prometer rollback de escrita
  de proxy quando sua leitura posterior falhar nem orientar retry cego.
- Não foi realizada homologação funcional da nova versão no banco existente,
  benchmark, teste de navegador ou integração n8n/AdsPower. Nenhum deles foi iniciado.
- Aplicação das duas migrations e reinicialização homologada dependem de revisão,
  aprovação separada, backup e conferência do alvo. Esta entrega não concede autorização.

**Etapa 2.4B.3 pronta para revisão técnica; não declarada homologada.** Aguardar
avaliação do usuário antes da homologação ou qualquer próxima etapa.
