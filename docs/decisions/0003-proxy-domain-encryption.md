# ADR 0003 — Domínio, persistência e criptografia de proxies

Status: aceita para a Etapa 2.1, autorizada pelo usuário.

## Limites e arquitetura

Domain contém o agregado Proxy, ProxyHost, ProxyPort e EncryptedProxyPassword.
Application contém somente a porta IProxyPasswordProtector. Infrastructure contém
o adapter criptográfico e o mapeamento EF Core. O contexto existente é mantido,
com schema proxies separado de identity/audit. Não há novos pacotes, endpoints,
casos de uso CRUD, repositórios genéricos, interfaces React ou integrações.
Um contrato de repositório será definido quando houver um caso de uso que precise dele.

Host aceita DNS ASCII, IPv4 canônico e IPv6 sem colchetes/zone ID. DNS é convertido
para minúsculas e IP para representação canônica, sem consulta DNS ou conexão externa.
URLs, userinfo, caminhos, hosts em branco e portas embutidas são recusados.
DNS tem até 253 caracteres, rótulos até 63. Porta é um valor de 1 a 65535.
Os value objects não admitem construção pública inválida.

UUID é fornecido antes de cifrar a senha, não gerado pelo banco. Usuário é obrigatório,
até 128 caracteres; região obrigatória, até 100. Ambos recusam branco e controles;
espaços significativos no usuário são preservados. Senha não vazia, até 1024 bytes
UTF-8 válido; texto claro nunca pertence ao agregado. Região permanece texto livre:
nesta etapa não há catálogo geográfico autorizado. Vencimento é um instante obrigatório,
não uma data civil sem fuso. Timestamps são UTC; datas extremas mapeadas para infinity
no PostgreSQL são recusadas. Atualização não pode retroceder no tempo.
Registros já vencidos podem existir, mas não são elegíveis.

## Estados e disponibilidade

- Disabled: indisponível por decisão operacional; estado inicial conservador.
- Active: habilitado operacionalmente, sem representar teste de conectividade.
- Suspended: impedimento temporário até decisão operacional.

IsAvailable(now, hasActiveAllocation) exige Active, now >= CreatedAt,
now < ExpiresAt e ausência de alocação ativa. No instante do vencimento já é falso.
Não persistimos um booleano Available nem um status Expired que envelheceria sozinho.
Não há máquina de transições adicionais: os três estados podem ser alterados entre si
com enum válido e timestamp monotônico.

Alocações ainda não existem. O booleano deve vir do futuro caso de uso que consulte
Reserved/Confirmed dentro da transação; não tem valor padrão. Essa função avalia
elegibilidade, **não garante reserva nem exclusão mútua**. A futura etapa de alocações
precisa do índice único parcial e controle transacional descritos em CLAUDE.md.

## Modelo de ameaça e formato criptográfico

Protegemos a senha contra leitura do banco/backup sem acesso às chaves e contra
alteração do ciphertext. AES-256-GCM usa chave aleatória de 32 bytes, nonce CSPRNG
de 12 bytes a cada cifragem, ciphertext bytea e tag de 16 bytes. KeyId não é secreto.
AAD UTF-8: CentralContingencia:ProxyPassword:v1:{UUID em N}:{KeyId}.
O formato v1 é fixo nesta etapa; alteração futura exige estratégia explícita de formato.
UUID, finalidade e KeyId são autenticados: mover o envelope para outro proxy ou
adulterar qualquer componente falha, sem retorno parcial de texto claro.
As operações usam AesGcm descartável, zeram buffers temporários de texto/chave,
e o singleton zera o keyring ao ser descartado. Strings gerenciadas e variáveis de
ambiente não podem ser apagadas com garantia; não afirmamos proteção contra leitura
da memória do processo ou comprometimento do usuário Windows.

Nonce é gerado novamente em cada operação; não é derivado da senha, hora ou UUID.
O índice único (PasswordKeyId, PasswordNonce) recusa reutilização **persistida**.
Aleatoriedade não é garantia matemática de ausência de colisões: uma eventual
violação exige recifrar com nonce novo e repetir a transação futura, sem usar o
envelope recusado. Sem caso de uso de gravação nesta etapa, esse retry não existe.
Planejar rotação antes de 2^20 operações por chave como limite operacional conservador;
a contagem global ainda não é implementada. Não reutilizar material sob outro KeyId,
entre ambientes, nem para outra finalidade. O keyring recusa aliases de uma mesma chave.
Antes de escala/produção, definir e monitorar orçamento global de operações por chave
e, se necessário, alocação durável de nonces entre instâncias.

Host, usuário e região permanecem metadados legíveis no banco. O usuário permite
identificar a credencial operacional e não é exposto por HTTP nesta etapa. Isso é
aceitável somente para os dados sintéticos locais atuais; futura importação real
deve reavaliar sensibilidade/criptografia desses identificadores. ToString dos
objetos não divulga credenciais; não há logging no adapter. Sensitive data logging
do EF e detalhes de erro PostgreSQL não foram habilitados.
AES-GCM não autentica Host/Port/Username/Status/Region; comprometimento do banco pode
alterar esses metadados. Não substitui autorização, auditoria, TLS e controle de acesso.
Nenhuma senha clara deve entrar em DTOs de listagem, logs, UI, exportações ou MCP.

## Configuração e falhas

ProxyEncryption:ActiveKeyId seleciona a versão para escrita.
ProxyEncryption:Keys:{KeyId} contém 32 bytes em Base64 provenientes de provider
seguro de configuração. Chaves não ficam no banco nem em appsettings/.env.example.
O adapter é resolvido sob demanda: configuração ausente/inválida recusa criptografia,
sem interromper login/sessão existentes. Não existe chave fallback.
Chave desconhecida, tag inválida ou contexto incorreto falham com erro genérico,
sem valores de configuração, segredo ou inner exception criptográfica.

No Windows local, os scripts geram chave com RNG e salvam somente DPAPI cifrado
em .local/secrets/proxy-development-v1.dpapi, ignorado pelo Git e vinculado ao
usuário Windows. Carregamento passa a chave para variáveis do processo apenas.
DPAPI não protege contra outro processo executando como o mesmo usuário.
Não usar essa estratégia como secret manager de produção. Leia o guia de execução.

## Persistência e migrations

proxies.Proxies contém todos os campos, timestamps timestamptz e envelope bytea
na mesma linha via OwnsOne; o objeto cifrado não tem ciclo de vida independente.
Buffers são copiados defensivamente; EF acessa campos privados, sem setters públicos.
Host/Port usam conversores. O banco reforça nulabilidade, limites, estados, porta,
UUID não vazio, timestamps finitos/ordenados e tamanhos/KeyId do envelope.
A validação completa de DNS/IP está no domínio; a constraint de host é uma barreira
básica de formato, não um parser completo de DNS/IPv6 em SQL.
Índices: (Host, Port), (Status, ExpiresAt), Region e único (PasswordKeyId, PasswordNonce).
Não presumimos host ou host/porta únicos: provedores podem compartilhar endpoint
com credenciais diferentes. Unicidade operacional futura requer decisão própria.

Up cria somente schema/tabela/índices novos. Não altera Identity/auditoria nem linhas
existentes. Down remove Proxies e é destrutivo; não foi executado. Nenhum bootstrap
ou startup aplica migrations. Banco homologado depende de aprovação explícita e backup.

## Estratégia futura de rotação

1. Gerar nova chave aleatória independente, novo KeyId imutável, guardar em secret manager.
2. Distribuir keyring antigo+novo a todas as instâncias antes de mudar a versão ativa.
3. Selecionar nova ActiveKeyId; novas escritas usam só essa versão, leituras usam KeyId.
4. Recifrar em lotes autorizados: ler/decriptar com chave antiga, cifrar com nova chave
   e novo nonce, substituir envelope via ReplaceEncryptedPassword, salvar transacionalmente.
   Implementar controle de concorrência/retry antes desse job, para não sobrescrever
   alterações simultâneas. Falhas mantêm envelope anterior; nunca relabelar ciphertext.
5. Verificar contagens por versão, integridade, retomada idempotente, auditoria sanitizada
   e backups antes de retirar a chave antiga do runtime.
6. Conservar chaves necessárias aos backups pelo período de retenção e testar restauração
   em ambiente isolado. Retirada definitiva depende da expiração desses backups.

Compatibilidade de leitura/escrita, substituição persistida e recusa de chave removida
são testadas. Job, comando de rotação, auditoria de proxies e secret manager são futuros,
não implementados nesta etapa. Não rotacionamos chaves Data Protection/Identity.

## Referências técnicas

- [AesGcm.Encrypt — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-10.0).
- [Owned entity types — EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities).
- ADR 0001 (arquitetura), ADR 0002 (autenticação) e CLAUDE.md.
