# ADR 0010 — Expiração e recuperação transacional de reservas

Data: 11/10/2026. Decisão implementada na Etapa 2.4B.3, aguardando revisão.

## Contexto

Reserved vencida continuava bloqueando a disponibilidade e os índices parciais.
ExpireAsync não auditava; confirm repetido retornava 409; falhas de CHECK da auditoria
eram classificadas como entrada inválida. A revisão 2.4B.2 bloqueou homologação.

## Decisão

TTL da Application configurado por ProxyAllocations:ReservationMinutes: padrão 15,
intervalo 1..1440. A Infrastructure calcula o prazo depois dos locks e da leitura
do relógio. Prazo omitido é limitado pela validade da proxy; prazo explícito admite
duração menor, mas não ultrapassa o TTL ou a validade. Relógio UTC/TimeProvider.

Não introduzir worker. GETs consultam disponibilidade lógica: Confirmed ocupa;
Reserved ocupa somente enquanto ReservationExpiresAt > agora. Nenhuma leitura
altera registros. O estado persistido pode permanecer Reserved após o prazo até
uma mutação tocar o perfil/proxy; consumidores devem considerar o prazo exibido.

Mutações de alocação adquirem SHARE ROW EXCLUSIVE na tabela antes de FOR UPDATE
perfil/proxy. A recuperação dos recursos envolvidos marca Reserved vencida como
Expired pelo serviço de domínio. Histórico recebe ReservationExpired e EndedAt;
evento recebe AllocationExpired e horário real da recuperação. Ator de sistema UUID
00000000-0000-0000-0000-000000000001, correlation ID reservation-expiry. Esse ator
não é usuário Identity, não concede papel nem representa uma sessão humana.

A reserva recuperada é salva antes do INSERT de novo vínculo, dentro da mesma
transação, liberando índices parciais sem depender da ordem de comandos do EF.
Negócio e eventos só se tornam duráveis no commit final. Concorrência serializada
impede dupla expiração; índices únicos permanecem proteção final contra bypass.
Confirm/release de vínculo expirado retornam 409 depois de confirmar apenas a
recuperação auditada; erro de auditoria interrompe e desfaz toda a transação.

Reconfirmar vínculo ainda Confirmed dos mesmos recursos/perfil ativo reconhece o
sucesso anterior, sem modificar datas, inserir vínculo ou auditar novamente.
Não exige novamente disponibilidade futura da proxy: não é uma nova alocação.
Released/Expired/perfil encerrado continuam conflitantes. Confirmações novas
continuam revalidando proxy e prazo. Reserve e release repetidos mantêm seus
contratos de conflito; replay de reserve não foi autorizado nesta etapa.

Falhas de persistência identificadas na auditoria são AuditFailure/HTTP 500 sem
exceção interna, SQL, parâmetros ou detalhes PostgreSQL. Demais invariantes e
conflitos continuam 400/409; indisponibilidade geral de persistência, 503.

## Alternativas e consequências

Worker permitiria materializar todas as expirações sem tráfego, mas adicionaria
coordenação e operação. Não é necessário para disponibilidade/reutilização.
Locks mais granulares exigiriam ordenar locks de perfis cruzados e proteger a
descoberta concorrente das reservas a recuperar. O lock de tabela mantém a solução
simples para o monólito atual; serializar todas as mutações de alocação pode reduzir
throughput. Timeout de 5 segundos continua 409; medir carga antes de futura evolução.
Leituras MVCC comuns não bloqueiam nesse lock. Nenhum benchmark de carga foi feito.

Não há nova migration: Expired, motivo, datas e ação de auditoria já existem.
As duas migrations de 2.4B.1/2.4B.2 continuam necessárias antes de reiniciar a
homologação com esta versão, mediante aprovação separada. Nenhuma foi aplicada.
