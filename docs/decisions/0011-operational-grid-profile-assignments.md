# ADR 0011 — Consulta operacional e duas atribuições opcionais

Data: 12/10/2026. Implementação autorizada na Etapa 2.4C.1; aplicação homologada
da migration e frontend não autorizados.

Uma linha representa uma Proxy. A visão é um read model de Application, consumido
por caso de uso autorizado e implementado em Infrastructure, sem novo agregado,
tabela de junção duplicada ou CQRS completo. EF Core SqlQueryRaw compõe um SQL fixo
e parametrizado com filtros/paginação; não materializa credenciais ou agregados.

LEFT JOIN só inclui Confirmed e Reserved com prazo futuro. Mesmo snapshot readonly
REPEATABLE READ e instante de TimeProvider para Count/página. Histórico permanece
independente. Confirmed prevalece como InUse mesmo se a proxy posteriormente vencer
ou bloquear; sem vínculo efetivo, elegibilidade operacional distingue Available
de Unavailable. GET não expira fisicamente/audita reservas; preserva política 2.4B.3.

Platform1/Platform2 são strings opcionais validadas por catálogo central do domínio
e CHECKs PostgreSQL. O catálogo finito protege a invariante; não introduzir VO ou
enum numérico apenas para envolver rótulos. Ausência é null; segundo slot pode existir
sem primeiro e repetição é permitida, respeitando a independência solicitada.
As classificações pertencem ao perfil e não alteram Status ou disponibilidade.

Platform legado continua separado e livre, preservando valores/clientes existentes.
Nenhum valor é convertido por inferência. Permitir null na coluna legada evita
inventar uma plataforma em cadastros novos sem atribuição; resposta legada mantém
tipo string usando vazio quando ausente. Slots são acrescentados aos DTOs. PATCH
detecta presença para distinguir omissão de limpeza explícita por null.

Migration é expansão não destrutiva: duas colunas nullable e CHECKs, mais relaxamento
de NOT NULL em coluna existente. Não é DDL exclusivamente de objetos novos. Nenhum
UPDATE, DROP de coluna, recifragem ou alteração de Proxy/Identity/alocação no Up.
Down perde atribuições e falha fechado se há Platform legado null: nunca substitui
null por valor inventado. Aplicação/recuperação exigem revisão e autorização próprias.

Contains case-insensitive escapa curingas SQL; filtros globais precedem paginação.
Testes medem quantidade fixa de SELECTs com 2000 proxies sintéticas. Trigram/índices
específicos ficam para medição adicional; nenhuma extensão ou índice desnecessário
foi adicionado. Integrações e conciliação CSV permanecem fora deste escopo.
