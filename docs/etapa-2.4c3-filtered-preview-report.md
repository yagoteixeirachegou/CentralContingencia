# Recuperação 2.4C.3 — cópia filtrada e prévia dos 129 registros

Data: 12/10/2026. **Prévia limpa: 129 propostas, zero conflitos, zero inválidos.**
Nenhuma confirmação, migration ou escrita no banco homologado.

## Estado verificado antes da execução

* `__EFMigrationsHistory`: 8 migrations; `20261012174401_AddInfoproductProfileAssignment`
  **continua pendente** (consulta em `BEGIN READ ONLY` … `ROLLBACK`).
* `proxies.Proxies`: 131; perfis, alocações e recibos de recuperação: 0.
* `scripts/Compare-LocalDatabaseSnapshot.ps1` contra `.local/24c3-update-preparation-before.txt`:
  18 tabelas com dados e estruturas idênticos.

## Arquivos

| Arquivo | Bytes | Registros | SHA-256 |
|---|---:|---:|---|
| Original `Downloads\Untitled spreadsheet - HTTP - FACEBOOK UNIFICADA.csv` | 15.734 | 132 | `A7441ABD055FC9277A7C0C11F182ED0BFE77AA73686BC43A579A08BAA13A37AE` |
| Cópia `Downloads\HTTP - FACEBOOK UNIFICADA - recuperacao-129.csv` | 15.467 | 129 | `30DF1F964A2FFE48FBA9A8BD2B5C5D07420FC6AFFE8622128F9302E66C49BAF2` |

Original não editado (hash conferido antes e depois). A cópia contém credenciais:
fica fora do repositório, ao lado do original, marcada como somente leitura.
Qualquer alteração de bytes invalida a prova e exige nova prévia.

A cópia foi gerada removendo apenas as faixas físicas de bytes das três linhas
(267 bytes). Os demais registros foram reanalisados pelo parser do projeto:
**0 campos divergentes** em relação ao original.

## Exclusões (somente na cópia)

| Linha original | Motivo | Resultado na prévia do original |
|---|---|---|
| 22 | `Perfil 1218`, 1ª ocorrência | Conflict / DuplicateProfile |
| 23 | `Perfil 1218`, 2ª ocorrência, proxy distinta da linha 22 | Conflict / DuplicateProfile |
| 131 | Host, porta, usuário e senha vazios | Ignored / NoConnectionSkipped |

Nenhum vínculo do Perfil 1218 foi escolhido; ele permanece pendência operacional
independente. As duas proxies envolvidas continuam sem perfil.

Mapa de linhas original → cópia: 2–21 → 2–21; 24–130 → 22–128; 132–133 → 129–130
(verificado registro a registro).

## Prévia somente leitura

Executada pelo caso de uso `IProfileRecoveryService.PreviewAsync` (o mesmo do
endpoint `POST /api/v1/profiles/recovery/preview`), compilado com o código atual
(catálogo com INFOPRODUTO), conexão restrita a `127.0.0.1:55432/central_contingencia_dev`
com `default_transaction_read_only=on`. Harness: `.local/24c3-filtered-preview/`.
Saída sanitizada: `.local/24c3-filtered-preview-summary.json`.

**Limitação:** não é uma resposta HTTP da API 7090. A API em execução usa o catálogo
anterior e não deve ser reiniciada com código novo enquanto a migration estiver pendente.

| Indicador | Original (skip=true) | Cópia (skip=false) |
|---|---:|---:|
| Registros | 132 | 129 |
| Proxies encontradas | 131 | 129 |
| Perfis identificados | 130 | 129 |
| Propostas | 129 | **129** |
| Conflitos | 2 | **0** |
| Inválidos | 0 | **0** |
| Ignorados | 1 | **0** |
| Plataforma 2 atribuída | 0 | 0 |

Comparação linha a linha: **129/129 pareamentos idênticos** (ProxyId, nome do perfil,
Plataforma 1 e Plataforma 2), 129 proxies distintas, 129 perfis distintos.
Plataforma 1 nas propostas: ESTEIRA DE PERFIL 115, ANUNCIANTE 8, INFOPRODUTO 4,
X1 1, MODERAÇÃO 1. A prova de confirmação é emitida para a cópia (não para o
original); não foi registrada nem exibida, e nenhum commit foi chamado.

## Integridade após a execução

Comparação com o baseline repetida: 18 tabelas idênticas; 131 proxies antes e depois.

## Bloqueio para a confirmação

Os 4 registros INFOPRODUTO violariam `CK_Profiles_Platform1` no schema atual; como o
commit é transacional, a recuperação inteira seria revertida. Sequência necessária,
cada passo com autorização explícita:

1. Aplicar `20261012174401_AddInfoproductProfileAssignment` (SQL revisado em
   `docs/security/etapa-2.4c3-infoproduto-migration.sql`), após novo snapshot.
2. Reiniciar a API com o código atual e validar health/login/CSRF.
3. Nova prévia HTTP Admin da cópia (`skipRowsWithoutConnection=false`), exigir os
   mesmos 129/0/0/0 e reenviar exatamente os mesmos bytes com a prova e uma chave
   de idempotência ao commit.
4. Conferir 129 perfis, 129 alocações confirmadas, recibo, 131 proxies inalteradas.

## Execução autorizada (12/10/2026)

1. **Backup** `.local/backups/etapa-2.4c3-recovery-20261012T181818Z/` (database.dump
   73.769 bytes, SHA-256 `74801B24…FF24A`, 18 tabelas, decodificação integral).
   ACL restrita não aplicada (bloqueada pelo classificador local).
2. **Migration INFOPRODUTO** aplicada pelo usuário com o SQL revisado: 9 migrations.
   Snapshot vs baseline: só `__EFMigrationsHistory` (+1) e estrutura de
   `OperationalProfiles` mudaram.
   **Incidente:** o pipe do PowerShell 5.1 (`$OutputEncoding` ASCII) gravou
   `MODERA??O`, `HOST P?GINAS` e `MINERA??O` nos checks. O primeiro commit retornou
   500 com rollback integral (0 gravações). Corrigido pelo usuário reaplicando somente
   os quatro `ALTER` com `$OutputEncoding` UTF-8; checks conferidos com 18 valores
   corretos. Aplicações futuras via PowerShell 5.1 devem definir `$OutputEncoding`
   UTF-8 ou usar `psql -f` dentro do container.
3. **API** reiniciada a partir de `.local/build-24c3-recovery` (0 avisos/erros),
   mesma chave DPAPI, Development, 7090/5041. Health 200, `/auth/me` anônimo 401,
   OpenAPI com INFOPRODUTO.
4. **Confirmação** via `.local/24c3-recovery-commit/`: prévia HTTP Admin/CSRF da cópia
   129/0/0/0, commit único com Idempotency-Key persistida em
   `.local/24c3-recovery-commit-idempotency-key.txt`. Resposta 200:
   recoveryId `ce98b7ed-f338-4006-88ad-c05739abc76b`, replayed=false, 129 Recovered,
   129 perfis/alocações/proxies distintos, proxies idênticas às da prévia.

## Estado final verificado (READ ONLY)

| Item | Valor |
|---|---:|
| Perfis | 129 (ESTEIRA DE PERFIL 115, ANUNCIANTE 8, INFOPRODUTO 4, MODERAÇÃO 1, X1 1) |
| Platform2 preenchida | 0 |
| Alocações Confirmed | 129 (129 proxies e 129 perfis distintos) |
| Recibos / linhas de recuperação | 1 / 129 |
| OperationalEvents | 387 |
| Proxies | 131, fingerprint `f1f2b42e10fc6573a75f93b777e7fcf6` (inalterado) |
| Proxies sem perfil | 2 (as do Perfil 1218) |
| Perfil 1218 | não criado — pendência operacional |
