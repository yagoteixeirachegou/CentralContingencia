# API — Confirmação de importação CSV

`POST /api/v1/proxies/import/commit?defaultRegion=...`

Requer sessão Admin, HTTPS, cookie antiforgery e `X-CSRF-TOKEN` da identidade atual.
Utiliza os mesmos limites e rate limiting da [prévia](proxy-csv-preview.md).
Enviar multipart/form-data, exatamente um arquivo no campo `file`, sem outros campos.

1. Enviar o arquivo para `/api/v1/proxies/import/preview`, com `defaultRegion`
   opcional, explícito e validado pelo domínio.
2. Conferir o relatório e conservar `confirmationToken` somente em memória.
3. Reenviar os mesmos bytes e a mesma opção para commit, com o token no header
   `X-Import-Confirmation` e UUID novo no header `Idempotency-Key`.
4. Em falha transitória, repetir com a mesma chave. Em mudança de arquivo/opção,
   gerar nova prévia e nova chave. Prova expirada exige nova prévia.

A prova expira em 15 minutos e pertence ao administrador. Não enviar linhas JSON
como confirmação. Nunca armazenar CSV, senhas ou prova em storage do navegador,
logs ou arquivos de configuração. As respostas usam no-store.

Opção query `skipRowsWithoutConnection=true` (padrão false), na prévia e no commit:
permite ignorar apenas linhas com PROXY, PORTA, USUARIO/SENHA todos vazios ou em
branco. Headers ambíguos/ausentes ou quantidade de campos errada impedem esse
tratamento. Linhas parcialmente preenchidas continuam inválidas. A opção integra
a prova/fingerprint; alterá-la exige nova prévia e operação. Query repetida ou não
booleana retorna 400. Nenhum ano é inferido automaticamente.

`referenceYear` opcional aceita um único inteiro entre 1 e 9999 na prévia e no
commit. Aplica-se somente a dd/MM sem ano; datas completas não são substituídas.
Sem opção, dd/MM é inválido. Calendário e vencimento futuro são revalidados pelo
mesmo parser/domínio; 29/02 exige ano bissexto. O ano participa da prova e do
fingerprint: alterar ou omitir a opção causa 409; opção inválida retorna 400.
Mesmo em arquivo com datas completas, as opções devem coincidir. Sem ano, o
fingerprint existente permanece idêntico; não há migration ou novas chaves.

Linhas ignoradas por essa opção recebem EmptyRow e aviso NoConnectionRowSkipped
na prévia; summary.empty inclui vazios e linhas sem conexão. Relatório e auditoria
persistem número original, resultado Empty e contagem ignored. Empty significa
linha vazia ou sem conexão explicitamente ignorada, sem guardar seus metadados.
O formato persistido existente foi reutilizado, sem migration. Com opção false,
fingerprint legado permanece byte a byte compatível; true adiciona marcador próprio.

Região ausente permanece inválida. `defaultRegion` preenche apenas valores vazios,
sem substituir região existente. PERFIL/PLATAFORMA/ADsPower Lote não criam entidades.

## Resultado

`CsvImportReport`: `importId`, `replayed`, `total`, `imported`, `ignored`, `invalid`,
`conflicting`, `items`. Cada item contém `line`, `proxyId` opcional e `outcome`.
Os resultados são Imported, Empty, Duplicate, Conflict, Invalid ou NotImported.
Nenhuma credencial, host em claro ou conteúdo de célula aparece no relatório.
Total é a soma das quatro contagens. No erro 422, registros elegíveis não gravados
aparecem como NotImported/ignored; todo o arquivo deixa de ser persistido.

Conflitos existentes são ignorados, nunca atualizados. Vazios/duplicatas válidas
são ignorados. Qualquer outro erro de linha/cabeçalho aborta a transação inteira.
Replay devolve o relatório histórico e não duplica proxies ou auditoria.

| HTTP | Significado |
| --- | --- |
| 200 | Concluído ou replay idempotente |
| 400 | Headers/opções/upload/CSRF inválidos |
| 401/403 | Sem sessão ou sem papel Admin |
| 409 | Prova inválida/expirada, arquivo alterado ou chave reutilizada com conteúdo diferente |
| 413/415 | Limite excedido ou transporte incompatível |
| 422 | Validação impede importação; ProblemDetails inclui relatório sanitizado |
| 429 | Limite de requisições |
| 503 | Criptografia/persistência indisponível ou timeout de bloqueio; nenhuma escrita parcial |

Contrato também disponível em `/openapi/v1.json` no ambiente Development.
Decisões e limites operacionais na [ADR 0007](../decisions/0007-controlled-csv-import.md).
