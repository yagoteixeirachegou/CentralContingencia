# Etapa 2.5 — Ações rápidas no grid operacional

Data: 12/10/2026. Somente frontend; nenhum endpoint, contrato ou migration alterado.

## Ações por linha em /proxies (Admin/Manager; Operator só consulta)

| Situação da linha | Ação | Endpoints existentes usados |
|---|---|---|
| Disponível, sem perfil | **Vincular** — novo perfil (nome + Plataforma 1/2) ou perfil ativo existente | `GET /profiles?name=` (bloqueia nome ativo duplicado), `POST /profiles`, `POST /allocations/reserve` (Idempotency-Key), `POST /allocations/{id}/confirm` |
| Com perfil | **Plataformas** — altera Plataforma 1/2 | `PATCH /profiles/{id}` só com slots alterados; vazio envia `null` |
| Reservada | **Confirmar** | `POST /allocations/{id}/confirm` |
| Reservada ou em uso | **Liberar** — exige confirmação humana | `POST /allocations/{id}/release` |

O vínculo não é atômico entre chamadas: se falhar no meio, o diálogo informa o ponto
(perfil criado sem proxy / reserva não confirmada) e "Tentar novamente" retoma do passo
que falhou, reaproveitando o perfil criado e a mesma Idempotency-Key. Falha incerta na
criação do perfil (5xx/rede) bloqueia nova tentativa até conferir o grid. Sem retries
automáticos. Liberar mantém o perfil ativo, sem proxy (encerrar perfil continua na
página do perfil). Um endpoint atômico de vínculo é candidato natural para a futura
integração AdsPower/n8n, não implementado.

Removidos: botão "Reservar" (substituído por "Vincular") e link "Alocação" (o nome do
perfil já leva à página de alocação/histórico). `ReserveProfileForm.tsx` excluído.

## Arquivos

- `src/central-contingencia-web/src/features/proxies/grid/LinkProfileForm.tsx` (novo)
- `src/central-contingencia-web/src/features/proxies/grid/AssignmentsForm.tsx` (novo)
- `src/central-contingencia-web/src/features/proxies/grid/OperationalGridTable.tsx`
- `src/central-contingencia-web/src/features/proxies/ProxiesPage.tsx`
- `src/central-contingencia-web/src/features/profiles/api.ts` (`updateProfileAssignments`)
- `src/central-contingencia-web/src/features/proxies/grid/OperationalGridPage.test.tsx`

## Validação

- `npm run typecheck`, `npm run lint`: sem erros.
- `vitest run`: **151/151** (6 arquivos). Grid: 18 testes, incluindo sequência
  criar→reservar→confirmar, nome duplicado, perfil existente, retomada após 409 sem
  recriar perfil e com a mesma chave, confirmar, liberar com confirmação, PATCH parcial
  com `null` e permissões por papel.
- `vite build`: aprovado (saída fora do repositório).
- Chrome real (`.local/25-e2e/verify.cjs`) contra API 7090 e PostgreSQL local, com sessão
  Admin temporária (10 min, arquivo removido após uso): vincular "Teste 2.5 190731" a uma
  proxy livre → Em uso com BM → Plataforma 2 TIKTOK → liberar → proxy volta a Disponível.
  Respostas 201/201/204/200/204; auditoria +5 eventos; fingerprint das 131 proxies inalterado.
  Capturas em `.local/25-e2e/artifacts/`.

Resíduo de teste: perfil ativo "Teste 2.5 190731" sem proxy (dados locais não autoritativos).
Testes .NET não executados: backend não alterado.
