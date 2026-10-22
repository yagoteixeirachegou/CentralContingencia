# ADR 0007 — Persistência controlada de CSV

## Decisão

Reenviar o CSV na confirmação é mais simples que manter uploads temporários: não
exige armazenamento de arquivos, cache de credenciais ou limpeza de sessões.
A prévia emite uma prova protegida pelo ASP.NET Core Data Protection, válida por
15 minutos e vinculada ao administrador, aos bytes exatos e à região explícita.
A prova não contém o CSV. Alterações exigem nova prévia. A confirmação nunca usa
linhas validadas pelo frontend como fonte de verdade.

Application reutiliza o mesmo parser e as invariantes existentes, consulta novamente
os conflitos e cria os agregados. Infrastructure implementa criptografia, proteção
da prova, transação e persistência. Domain não depende de EF ou transporte HTTP.

## Semântica

Uma linha inválida ou cabeçalho inválido aborta toda a operação, com relatório 422
e nenhuma gravação. Linhas vazias e duplicatas internas válidas são ignoradas;
uma duplicata com outro erro continua inválida. Conflitos por host/porta existentes
são ignorados e contabilizados separadamente: nenhum proxy é sobrescrito.
PERFIL não é identidade de proxy; plataforma/lote não criam vínculos operacionais.

Região continua obrigatória. Sem REGIÃO, a linha é inválida, salvo fornecimento
explícito de `defaultRegion`, validado pelo domínio em prévia e confirmação.
Essa opção preenche somente células ausentes/vazias e integra a prova protegida.
Não há valor padrão inventado.

## Atomicidade e idempotência

A transação PostgreSQL READ COMMITTED adquire `SHARE ROW EXCLUSIVE` em
`proxies."Proxies"`, com espera máxima de cinco segundos. O bloqueio serializa
importações e impede INSERT/UPDATE concorrente do CRUD durante a consulta e gravação.
Leituras normais continuam possíveis. Uma única gravação inclui proxies, eventos
de criação, lote de auditoria e resultados por linha. Falhas provocam rollback.

`Idempotency-Key` é UUID obrigatório, único por administrador. O lote guarda somente
fingerprint protegido, contagens, data, responsável e identificadores/resultados
não sensíveis. Replay válido devolve o relatório original sem novas escritas ou
eventos. Arquivo/opção diferente com a mesma chave retorna 409. Operações já
concluídas não são executadas nem revalidadas novamente; uma prova válida continua
obrigatória. Após expiração, nova prévia do mesmo arquivo permite recuperar o lote.
Falha transitória deve ser repetida com a mesma chave.

## Segurança e consequências

Admin, cookies, CSRF, HTTPS, rate limiting e limites de upload existentes são
reutilizados. Senhas são criptografadas pelo AES-256-GCM existente; arquivo original,
senhas e conteúdo não integram respostas, logs ou auditoria. Hash simples do arquivo
não é persistido: Data Protection usa propósitos separados para prova e fingerprint.
Buffers são limpos; apagar fisicamente strings gerenciadas não pode ser garantido.

Conservar o keyring Data Protection é necessário para recibos persistidos; política
de retenção/expurgo desses recibos ainda não foi implementada. O bloqueio de tabela
privilegia consistência e tem custo de concorrência em grandes volumes. Ausência de
chave AES ou migration resulta em erro sanitizado 503, sem importação parcial.
Infraestrutura de produção deve redigir também os headers de confirmação/CSRF e
corpos multipart nos logs de transporte.

A migration adiciona apenas duas tabelas em audit, índices e constraints, sem
alterar dados existentes. Aplicação na homologação exige autorização e backup.
Nenhuma interface ou integração externa faz parte desta decisão.
