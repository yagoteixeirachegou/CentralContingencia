# Perfis operacionais e alocações — API v1

Sessão Identity HTTPS/cookie existente. Admin/Manager escrevem e consultam;
Operator somente consulta. Outros papéis são recusados. POST/PATCH exigem
X-CSRF-TOKEN da identidade atual e rate limiting sensitive. Respostas no-store.

| Endpoint | Sucesso | Papel |
| --- | --- | --- |
| GET /api/v1/profiles | 200 ProfilePage | Admin/Manager/Operator |
| GET /api/v1/profiles/{id} | 200 ProfileResponse | Leitura |
| POST /api/v1/profiles | 201 e Location | Admin/Manager |
| PATCH /api/v1/profiles/{id} | 200 ProfileResponse | Escrita |
| POST /api/v1/profiles/{id}/close | 204 | Escrita |
| GET /api/v1/profiles/{id}/allocations | 200 AllocationPage | Leitura |
| GET /api/v1/allocations | 200 AllocationPage | Leitura |
| GET /api/v1/allocations/{id} | 200 AllocationResponse | Leitura |
| POST /api/v1/allocations/reserve | 201 e Location | Escrita |
| POST /api/v1/allocations/{id}/confirm | 204 | Escrita |
| POST /api/v1/allocations/{id}/release | 204 | Escrita |

Cadastro: JSON `{ "name": "Perfil sintético", "platform": "Sintética" }`.
Nome usa ProfileName, até 200 caracteres; platform legado textual até 100.
Etapa 2.4C.1 acrescenta platform1/platform2 opcionais do catálogo, sem converter
platform legado. Contrato em [operational-grid.md](operational-grid.md); exige a nova
migration, já aplicada/validada na homologação local; não repetir. Nome não é chave única. PATCH admite
name/platform e os dois slots; ausentes preservados, null limpa somente slots novos.
Null em name/platform, duplicatas/campos desconhecidos/objeto vazio são recusados.
Perfil Closed não admite atualização nem nova alocação.
Close encerra o perfil e sua alocação atomicamente; repetir retorna 204 sem novo evento.

Reserva: JSON com profileId e proxyId UUID; reservationExpiresAt ISO com Z/offset é
opcional. Exige Idempotency-Key UUID não vazio. Sem prazo explícito, utiliza o menor
entre agora + TTL configurado e a validade da proxy. `ProxyAllocations:ReservationMinutes`
tem padrão 15 e aceita 1..1440; configuração inválida impede inicialização. Prazo
explícito deve ser futuro e não ultrapassar esses limites. Confirm revalida prazo
e recursos. Close/confirm/release exigem JSON vazio `{}`. JSON limitado a 16 KiB.

Confirmed e Reserved com prazo futuro ocupam recursos; Released/Expired são históricos.
No instante do prazo, Reserved deixa de impedir disponibilidade. Proxy válida e operacional
pode ser reutilizada após finalização, criando outro vínculo. Proxy Suspended/Disabled/
vencida/ocupada e perfil ocupado/encerrado recebem 409. Chave repetida recebe 409 sem
replay, inclusive após liberação. Após resposta incerta, consultar histórico antes de
iniciar outra operação. Confirm repetido de vínculo ainda Confirmed retorna 204,
preservando datas e sem novo evento, mesmo se a proxy depois vencer/bloquear.
Confirm de Released/Expired ou perfil encerrado retorna 409. Uma reserva vencida é
recuperada e auditada antes de devolver 409 à confirmação. Release de reserva vencida
também recebe 409 após recuperação; o motivo histórico fica ReservationExpired.

Recuperação sob demanda: reserve/confirm/release/expire interno/close recuperam
reservas vencidas dos recursos envolvidos dentro da mesma transação. Não há job.
GETs não gravam; listagens/histórico podem ainda mostrar Reserved com prazo vencido
até a recuperação, mas disponibilidade já desconsidera esse vínculo. Erro de negócio
normal desfaz a transação, incluindo recuperação; a rejeição de vínculo expirado
em confirm/release é a exceção explícita que confirma somente recuperação + auditoria.
Índices únicos parciais permanecem iguais. Mutações adquirem lock de tabela
SHARE ROW EXCLUSIVE antes dos locks de perfil/proxy, com timeout de 5s, serializando
recuperação e novas alocações. Expiração é salva antes de inserção na mesma transação.

Listagens: page 1..10000, pageSize 1..100, padrões 1/20. Filtros exatos de perfis:
name, platform, status Active/Closed; ordenações name/platform/status/createdAt/updatedAt.
Alocações: profileId, proxyId, status Reserved/Confirmed/Released/Expired; ordenações
status/createdAt/updatedAt. direction asc/desc, padrão desc; desempate UUID.
Histórico impõe perfil da rota. Page contém items/page/pageSize/total. Leituras de
total/itens são separadas e podem divergir durante escritas concorrentes.

ProfileResponse: id/name/platform/status/createdAt/updatedAt/closedAt, com
platform1/platform2 nullable acrescentados na Etapa 2.4C.1.
AllocationResponse: id/profileId/proxyId/status/createdAt/updatedAt,
reservationExpiresAt/confirmedAt/endedAt/endReason. Sem credenciais, host, envelope,
chaves ou token. Datas UTC; PostgreSQL persiste precisão de microssegundos.

ProblemDetails estático: 400 validação/CSRF, 401 sem sessão, 403 sem papel, 404 recurso
ausente, 409 conflito, 415 transporte, 429 limite, 500 falha interna de auditoria,
503 persistência indisponível.
Não expõe SQL, constraints ou exceções internas.

Auditoria atômica: criação/atualização/encerramento do perfil e reserva/confirmação/
liberação, somente UUIDs, ação, responsável, UTC e correlation ID. Sem nomes,
plataformas, credenciais ou payloads. Fechar perfil audita também finalização do vínculo.
Falha ao salvar auditoria desfaz a operação. Expiração usa AllocationExpired,
ID da reserva/perfil e horário efetivo da recuperação; motivo ReservationExpired
fica no histórico. Ator de sistema reservado `00000000-0000-0000-0000-000000000001`
(não é usuário Identity) e correlation ID `reservation-expiry`. Repetições/concorrência
não duplicam expiração ou confirmação.

Proxies conservam contratos. available em listagem/detalhe/PATCH e GET /available
considera Confirmed e Reserved não vencida; consulta não constitui reserva. CSV continua importando
somente proxies. AES/DPAPI e prova/idempotência CSV permanecem inalterados.

Depende das migrations 2.4B.1 e AddOperationalAudit, já aplicadas e homologadas localmente.
Não reaplicá-las. Evidências em docs/etapa-2.4b-homologacao-report.md. OpenAPI em
/openapi/v1.json, Development. Aplicação em outro ambiente exige autorização separada.
