# ADR 0012 — Recuperação controlada de perfis da planilha

Status: implementada/testada em ambiente descartável; homologação e recuperação real pendentes.

## Contexto

Importação 2.3 persistiu proxies, mas não PERFIL/PLATAFORMA/lote. O banco não permite
reconstruir esses textos descartados. Recuperação exige reenvio da fonte, sem
reimportar proxies, alterar credenciais ou deduzir pares pela ordem das linhas.

## Decisão

Criar casos de uso Application e endpoints próprios de prévia/commit Admin.
Compartilhar `BoundedCsvParser`, limites multipart/CSRF e mecanismo Data Protection.
Mapeamento de metadados separado do importador de proxies: requisitos diferentes
não devem relaxar suas validações existentes. Normalização aplica-se a cabeçalhos;
valores de atribuição usam o catálogo exato do domínio após trim.

O CSV aceita slots explícitos PLATAFORMA 1/2 ou uma/duas ocorrências de PLATAFORMA,
associadas à primeira/segunda posição respectivamente. Segunda coluna vazia gera
slot null; primeira vazia não recebe o valor da segunda. Misturas dos formatos,
mais de duas ocorrências ou outros cabeçalhos duplicados bloqueiam a prévia.

Proxy é identificada por host canônico do Value Object + porta, com exatamente uma
correspondência. Host/porta não são únicos no domínio: múltiplos resultados são
conflito, sem desempatar por usuário, senha, região, nome ou ordem. Nome repetido
no arquivo (case-insensitive) e endpoint repetido bloqueiam todas as linhas
envolvidas. Nome existente não prova identidade e impede criação/reuso automático.
Única exceção sem escrita: vínculo Confirmed da proxy já corresponde ao perfil
Active, nome exato e slots exatos; reportar AlreadyLinked. Não alterar atribuições
de perfil existente nem reassociar históricos automaticamente.

Sem perfil e sem atribuições: ignorar. Atribuições sem perfil: inválido. Linha com
metadados e nenhuma conexão exige skipRowsWithoutConnection explícito. Dados
parciais de conexão não recebem esse tratamento. Proxy não localizada, indisponível
ou com vínculo/reserva ativa é impedimento, não um registro silenciosamente pulado.

Status, validade, região e credenciais da proxy vêm do banco. Datas dd/MM da fonte
não são reinterpretadas neste fluxo, pois não há atualização de vencimento.
Nenhum ATIVO da planilha é convertido em disponibilidade. Lote permanece ignorado.

Prévia tem snapshot REPEATABLE READ/READ ONLY e resumo global/página por linha.
Prova de 15 minutos vincula ator, arquivo completo, opção e plano (UUIDs, estados,
timestamps, prazos, resultados). Confirmação reenvia o arquivo e recalcula o plano
sob locks. Qualquer divergência exige nova prévia. Não existe cache de CSV/credenciais.
Namespaces dos fingerprints de recuperação são distintos dos da importação 2.3.

Lote é estrito e atômico: qualquer inválido/conflito impede todas as propostas.
Linhas ignoradas/AlreadyLinked são mantidas no recibo. Novos perfis recebem vínculo
Reserved → Confirmed na mesma transação, usando AllocationDomainService e TTL
existente. Timestamps são os da recuperação atual; nenhum histórico é inventado.
Reservas expiradas são recuperadas com o helper transacional homologado e auditoria,
liberando índices parciais antes de inserir o novo vínculo. Falha reverte tudo.

Locks começam por ProxyAllocations, como no store existente; depois perfis, proxies
e recibos. SHARE ROW EXCLUSIVE permite leitura e serializa mutações relevantes;
lock_timeout é cinco segundos. Índices exclusivos continuam garantia final.
Timeout/deadlock/unicidade/serialização: 409; falha interna/auditoria: 500 sanitizado.

Recibo separado em audit.ProfileRecoveryBatches/Lines evita reutilizar incorretamente
recibos de criação de proxies. Armazena somente ator/chave, fingerprint protegido,
data/correlação, contagens e UUIDs/resultados por linha. Unicidade ator/chave permite
replay da mesma operação concluída, inclusive depois da expiração da prova, mediante
mesmo arquivo/opção e sessão Admin. Não reaplicar regras do estado atual ao replay
histórico; não recriar vínculo após liberação. Arquivo/opção diferente com mesma
chave retorna 409. Outra chave exige nova prévia e respeita o estado atual.

Auditoria operacional (criação, reserva, confirmação, expiração) e recibo são atômicos.
Nenhum CSV, host, usuário ou senha é copiado para auditoria/recibo. Proxies são
somente consultadas, sem decrypt/recifragem. Chaves existentes permanecem inalteradas.

## Consequências

Migration aditiva precisa de revisão/aprovação separada. Sem recuperação real ou
reinício da nova API na homologação até disponibilizar schema autorizado. Restrições
de banco descartável e sintético permanecem. Não existe UI nova nesta etapa.
Nomes existentes conflitantes precisam de conciliação humana futura; não criar
vínculos artificiais para contornar indisponibilidade. Locks limitam paralelismo;
volumes maiores exigem medição. Recibos dependem da preservação do keyring e não
possuem expurgo. Strings gerenciadas não garantem apagamento físico da memória.
