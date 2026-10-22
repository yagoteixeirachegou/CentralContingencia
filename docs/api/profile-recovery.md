# Recuperação controlada de perfis — API

**Não executar na homologação nesta etapa.** Implementação testada somente com
dados sintéticos e PostgreSQL descartável. Migration pendente; API local em execução
permanece na versão homologada anterior.

## Transporte e permissões

POST `/api/v1/profiles/recovery/preview` e POST `/api/v1/profiles/recovery/commit`.
Somente Admin, cookie HTTPS e `X-CSRF-TOKEN` atual. Manager/Operator recebem 403;
anônimo 401. Respostas no-store; rate limiting sensitive existente.

Multipart com exatamente um arquivo no campo `file`; leitura em memória, sem
ReadForm/buffering em arquivos. UTF-8 com/sem BOM, delimitadores ; ou , e aspas.
Limites ProxyImportPreview compartilhados: 2 MiB, 5.000 registros, 10.000 linhas
físicas, 4.096 caracteres/campo, 32 colunas. Configuração existente é autoridade.

Prévia: `page` 1..10000, `pageSize` 1..100, defaults 1/20. Todas as páginas exigem
reenvio do arquivo. `skipRowsWithoutConnection` booleano opcional, default false,
deve coincidir no commit. Região/ano de referência não são aceitos: esta recuperação
nunca atualiza dados de proxies. Query desconhecida é rejeitada.

Cabeçalhos obrigatórios: PERFIL, PROXY, PORTA. Atribuições opcionais: PLATAFORMA 1
e PLATAFORMA 2, ou uma/duas colunas PLATAFORMA por posição. USUARIO/USUÁRIO e SENHA
só verificam ausência total de conexão para skip; valores não identificam a proxy
nem entram nas respostas. Demais campos existentes são ignorados com aviso.
Catálogo de atribuições: o mesmo de OperationalProfile; sem conversões aproximadas.
INFOPRODUTO é uma atribuição legítima para ambos os slots opcionais. O rollout
exige ampliar CK_Profiles_Platform1/2 pela migration AddInfoproductProfileAssignment;
essa migration ainda depende de autorização para aplicação na homologação.
Não há seleção/exclusão de linhas conflitantes na confirmação: o lote é atômico.

## Prévia

Retorna `summary`, `headerIssues`, `page`, `pageSize`, `items`, `confirmationToken`
e `confirmationExpiresAt`. Resumo global:

- total: registros analisados, excluindo cabeçalho;
- proxiesFound: linhas com correspondência única encontrada;
- profilesIdentified: nomes distintos válidos, case-insensitive;
- assignmentsRecoverable: slots preenchidos de operações propostas;
- ignored: Ignored/AlreadyLinked;
- invalid/conflicts: impedimentos;
- proposed: linhas que criariam perfil e vínculo confirmado.

Cada item contém line original, outcome, proxyId?, profileName?, platform1/2?,
profileId?/allocationId? quando AlreadyLinked, issues e operations.
`Proposed` tem operations `["CreateProfile","CreateConfirmedAllocation"]`;
demais resultados têm operations vazias. Propostas não criam UUIDs de perfis ainda.
`Conflict` e `Invalid` bloqueiam o lote inteiro. Sem linhas ou com impedimentos,
token é null. Cabeçalhos/linhas têm mensagens por código e número original,
sem valores inválidos, filename ou conteúdo de credenciais ecoados.

Prova protegida expira em 15 minutos e vincula administrador, bytes/opção e plano
com o estado de proxies/perfis/alocações. Paginação não muda a semântica da prova.
Nunca salvar token, arquivo ou credenciais no navegador persistente ou em logs.

## Confirmação explícita

Depois de revisar as propostas, reenviar exatamente arquivo/opção para commit com
`X-Import-Confirmation` da prévia e `Idempotency-Key` UUID não vazio, por operação.
Não enviar itens da prévia como comandos confiáveis. Backend revalida sob transação.
Sem confirmação neste endpoint, a prévia não causa qualquer gravação.

Resposta: recoveryId, replayed, total, recovered, ignored, items. Item contém somente
line, outcome (`Recovered`, `Ignored`, `AlreadyLinked`), proxyId?/profileId?/allocationId?.
Nomes/atribuições ficam no perfil, não no recibo. Perfil novo recebe vínculo atual
confirmado, sem reconstruir timestamps históricos. Nenhuma proxy é criada/alterada.

Mesma chave/ator/arquivo/opção após sucesso recupera recibo original sem nova
auditoria/alocação, mesmo com prova expirada. Chave com outro arquivo/opção: 409.
Outra chave e vínculo atual exato: AlreadyLinked, sem sobrescrever ou duplicar.
Liberação posterior não é desfeita pelo replay da chave original.

## Erros e concorrência

| HTTP | Significado |
|---|---|
| 200 | Prévia com diagnósticos, ou recuperação/recibo concluído |
| 400 | Multipart/CSV/opções/paginação/headers/CSRF inválidos |
| 401/403 | Sessão ausente / papel não autorizado |
| 409 | Prova expirada/alterada, plano concorrente mudou, chave divergente ou conflito de persistência |
| 413/415/429 | Limites / media type / rate limit |
| 422 | Lote inválido na validação final; nenhuma gravação parcial |
| 500 | Falha interna/persistência/auditoria; rollback e resposta sanitizada |

Qualquer mudança relevante depois da prévia exige consulta/nova prévia. Sem retry
automático. Em resposta incerta, conservar arquivo/opção/chave para consultar o
resultado pelo commit idempotente; nunca trocar a chave cegamente.
Reservas expiradas deixam de bloquear a prévia e são finalizadas/auditadas somente
na confirmação transacional. Histórico anterior e exclusividade são preservados.
