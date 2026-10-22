# Grid operacional — API v1

Endpoint novo: `GET /api/v1/proxies/operational-grid`.
Requer a migration `20261012102737_AddProfileAssignments`, já aplicada e validada
na homologação local em 12/10/2026. Não reaplicá-la. Evidências em
[relatório de homologação](../etapa-2.4c1-homologacao-report.md).
Aplicação em outro ambiente exige aprovação e conferência separadas.

Sessão HTTPS/Identity; leitura Admin, Manager e Operator. Outros papéis: 403;
anônimo: 401. GET não exige token CSRF e não grava/recupera reservas. Mutações de
perfis/alocações mantêm CSRF, auditoria e permissões dos contratos anteriores.
Todas as respostas da API mantêm no-store.

| Query | Contrato |
| --- | --- |
| page / pageSize | 1..10000 / 1..100; padrões 1 / 20 |
| profile | Contains literal case-insensitive do nome do perfil atualmente associado; até 200 caracteres |
| region | Contains literal case-insensitive da região; até 100 caracteres |
| platform | Rótulo do catálogo; Platform1 OU Platform2 |
| availability | Available, Reserved, InUse ou Unavailable; ausente inclui todos |
| sort / direction | Somente expiresAt; asc ou desc, padrões expiresAt/asc |

Filtros distintos combinam por AND antes da contagem/paginação. `%`, `_` e `\`
em pesquisa textual são caracteres literais, não curingas. Texto é aparado;
busca vazia/branca, controles, parâmetros repetidos/desconhecidos, números inválidos,
ordem fora da whitelist e classificação desconhecida retornam 400 sanitizado.
Ordem de vencimento desempata por proxyId crescente.

Resposta `OperationalGridPage`: items, page, pageSize, total, evaluatedAt UTC.
Cada item: proxyId, profileId?, profileName?, platform1?, platform2?, host, port,
region, expiresAt UTC, status operacional, availability, allocationId?,
reservationExpiresAt? UTC. Campos sem associação são null, representando vazio;
nenhum usuário/senha/envelope/chave/fingerprint/prova está no DTO.

Uma linha por Proxy, mesmo com histórico extenso. Confirmed ocupa e aparece InUse,
inclusive se posteriormente vencida/Disabled/Suspended; Status/ExpiresAt continuam
independentes. Reserved com prazo futuro aparece Reserved. Reserva vencida não
entra como vínculo atual: proxy pode ser Available se Active, criada e ainda válida,
ou Unavailable caso contrário. Reserva expirada permanece no histórico até a
recuperação transacional homologada. Confirmed não expira pelo prazo da reserva.

Count e página compartilham transação REPEATABLE READ READ ONLY e evaluatedAt
único. A consulta não reserva recursos: mutações revalidam após locks e podem
devolver 409. Não há refresh, liberação ou confirmação automática neste endpoint.

## Contratos de perfis

POST /api/v1/profiles admite name e platform1/platform2 opcionais. Exemplos:

```json
{ "name": "Perfil sintético", "platform1": "DARKPOST", "platform2": "TIKTOK" }
```

```json
{ "name": "Perfil sem atribuições" }
```

Ambos os slots são independentes: segundo sem primeiro e valores iguais são
aceitos; null representa ausência, string vazia é inválida. Labels exatos, após
trim: DARKPOST, MODERAÇÃO, ANUNCIANTE, HOST PÁGINAS, FLAG, BACKUP PIXEL, ESTOURADO,
RESTRITA, BUGADO, ATIVO (LIVRE), ESTEIRA DE PERFIL, MINERAÇÃO, FLAG (LIVRE), TIKTOK,
BM, X1, GOOGLE ADS, INFOPRODUTO. ATIVO (LIVRE) não determina disponibilidade da proxy.

PATCH /api/v1/profiles/{id}: omissão preserva slot; null limpa slot explicitamente.
name/platform legados continuam recusando null no HTTP. Campos desconhecidos,
duplicados (inclusive caixa diferente), tipos incorretos e objeto vazio são inválidos.
Perfil Closed continua terminal; alterações válidas usam a auditoria atômica existente.

O campo `platform` anterior continua texto livre, com regras anteriores, quando
informado; é mantido separadamente para compatibilidade. Não é alias nem preenchimento
automático de platform1. Valores legados fora do catálogo continuam persistidos e
consultáveis. Novos perfis sem platform legada persistem null nessa coluna; a resposta
legada usa string vazia, preservando seu tipo JSON. ProfileResponse acrescenta
platform1/platform2 nullable. Filtro/ordenação platform em GET /profiles continuam
com a semântica legada; filtro platform do grid procura exclusivamente slots novos.

Não há importação/recuperação automática de perfil ou atribuições do CSV.
CRUD de proxies, importador, reserva/confirm/release/close e histórico mantêm seus
endpoints anteriores. Nenhuma senha persistida é disponibilizada.

Erros: 400 parâmetros/entrada, 401 sessão ausente, 403 papel recusado, 503 falha de
persistência; demais erros de operações seguem o contrato de perfis/alocações.
OpenAPI descreve sessão, query, DTOs e slots; GET novo disponível após a nova versão
ser carregada em ambiente com schema atualizado e autorizado.
