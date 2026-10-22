# ADR 0008 — Perfis operacionais e alocações exclusivas

## Escopo e separação

Etapa 2.4A: apenas Domain e testes unitários. Nenhum endpoint, caso de uso Application,
mapeamento EF, migration, importação, frontend ou integração foi implementado.

OperationalProfile possui UUID, ProfileName, Platform, ProfileStatus e timestamps UTC.
ProfileName é Value Object imutável, com igualdade por valor, limite de 200 caracteres,
recusa de vazio/controles e remoção de espaços externos. Nomes iguais não identificam
o mesmo perfil. Platform permanece texto validado até aprovação do catálogo; não foi
inventado enum, segunda plataforma ou vínculo AdsPower. Estados mínimos Active e
Closed, separados dos estados Active/Suspended/Disabled de Proxy. Encerramento é terminal.

ProxyAllocation registra UUID próprio, ProfileId, ProxyId, ActorId, IdempotencyKey,
estado, criação/alteração, limite explícito da reserva, confirmação e encerramento.
IDs dos recursos são imutáveis; reutilização cria outra alocação, nunca reatribui a
antiga. ToString expõe somente UUIDs, sem nomes, plataformas ou credenciais.

AllocationDomainService concentra as regras entre as entidades. Nenhum agregado
carrega credenciais duplicadas nem referencia Identity, EF, HTTP ou SDK externo.
Proxy existente e seu mecanismo criptográfico permanecem intactos. O domínio usa
tempo recebido explicitamente; não acessa relógio global ou infraestrutura.

## Invariantes e transições

- Uma proxy pode existir sem alocação; um perfil pode existir sem proxy.
- Reserved e Confirmed ocupam exclusivamente proxy e perfil. No conjunto completo
  fornecido ao serviço, uma segunda alocação ativa de qualquer lado é recusada.
- Reserva exige perfil Active e proxy disponível pelas regras existentes:
  ProxyStatus.Active, criada no instante considerado, não vencida e sem vínculo ativo.
  Suspended e Disabled representam os estados não operacionais existentes; não foi
  criado novo status Blocked nem confundido status do perfil com status da proxy.
- O prazo da reserva é explícito, futuro e não ultrapassa ExpiresAt da proxy; nenhum
  TTL padrão foi inventado. Confirmação revalida identidade, estado do perfil,
  operacionalidade/validade da proxy e prazo da reserva. No instante exato do prazo,
  confirmar é recusado.
- Reserved → Confirmed, Reserved/Confirmed → Released e Reserved → Expired.
  Released/Expired são terminais. Não há reabertura, exclusão ou substituição do histórico.
- Reserva com prazo ultrapassado continua ocupando os recursos até ExpireReservation
  mudar explicitamente o estado. Isso evita divergência com índices por status.
  Expirar antes do prazo ou expirar uma alocação Confirmed é recusado.
- CloseProfile valida previamente timestamps e consistência do conjunto, finaliza
  a alocação Reserved/Confirmed com motivo ProfileClosed e encerra o perfil. Falha
  de validação não muda parcialmente os objetos. Perfil sem vínculo pode encerrar.
  Repetir encerramento válido preserva a data original.
- Liberação não encerra o perfil. Encerrar/liberar não altera ProxyStatus, validade
  ou credenciais. Reutilização continua sujeita às regras existentes da proxy.
- Histórico preserva datas e motivo Released/ProfileClosed/ReservationExpired.
  Datas normalizadas em UTC; regressão temporal é recusada inclusive frente ao
  histórico encerrado relacionado aos recursos.
- Reutilização de UUID ou ActorId/IdempotencyKey é recusada no conjunto fornecido.
  Replay HTTP/persistente fica para Application, sem alterar a idempotência CSV atual.

Vencimento/bloqueio posterior da proxy impede novas reservas e confirmação, mas não
finaliza silenciosamente vínculos confirmados. Uma política de finalização automática,
jobs e notificações dependerá de autorização futura.

## Persistência futura: proposta, não executada

OperationalProfile e Proxy são raízes com identidade independente. ProxyAllocation
possui identidade e ciclo de vida próprios, referenciando ambas por UUID. Relações
históricas 1:N; simultaneamente ativas no máximo 1:1. Não adicionar ProfileId obrigatório
à tabela existente de proxies nem inferir vínculos a partir de nomes/host.

No futuro schema allocations, propor dois índices únicos parciais:

```sql
-- Proposta documental; não foi executada e não constitui migration.
CREATE UNIQUE INDEX "UX_ProxyAllocations_ActiveProxy"
ON allocations."ProxyAllocations" ("ProxyId")
WHERE "Status" IN ('Reserved', 'Confirmed');

CREATE UNIQUE INDEX "UX_ProxyAllocations_ActiveProfile"
ON allocations."ProxyAllocations" ("ProfileId")
WHERE "Status" IN ('Reserved', 'Confirmed');
```

Também propor PK UUID, FKs para perfil/proxy com DELETE RESTRICT, índice único por
ActorId/IdempotencyKey e índices de histórico por ProfileId/CreatedAt e ProxyId/CreatedAt.
Constraints deverão validar enum, UUIDs, timestamps finitos/ordenados, prazo da reserva
e coerência entre estado, ConfirmedAt, EndedAt e EndReason. Não criar unicidade global
para nome do perfil. Política de exclusão/retencão de usuários responsáveis exige
decisão antes de mapear eventual FK para Identity.

O serviço de domínio não garante exclusividade entre processos nem detecta snapshots
omitidos/desatualizados. Application deverá carregar o conjunto completo relevante
sob transação e tratar conflito. Encerramento deve salvar perfil, finalização do
vínculo e auditoria juntos; bloquear/revalidar o perfil impede corrida com nova reserva.
Índices, locks em ordem consistente e controle de concorrência são obrigatórios na
etapa de persistência; testes com banco deverão comprovar as duas exclusividades.

GET disponíveis atual não consulta alocações: elas ainda não existem no banco. Na
futura integração, atualizar consulta/projeção para excluir Reserved/Confirmed,
preservando paginação e indicadores. Não publicar funcionalidades de alocação antes
dessa integração. Histórico não deve ser apagado por cascade de perfil/proxy.

## Integrações e decisões pendentes

UUIDs internos e ActorId preparam identificação estável para futuros casos de uso
e auditoria. IDs AdsPower, catálogo de plataformas, segunda plataforma, adaptadores
n8n e eventos de integração só serão definidos em etapa autorizada. Nenhuma conexão,
SDK, chamada externa, credencial adicional ou senha de plataforma foi criada.

Persistência, eventos auditáveis de alocação, autorização, idempotência com replay,
expiração agendada e contratos HTTP permanecem pendentes. Autenticação, auditoria,
AES, importação CSV e chaves existentes não foram modificadas.
