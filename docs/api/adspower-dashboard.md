# Dashboard de perfis do AdsPower

Leitura **somente de consulta** da API local do AdsPower. Nada é gravado no PostgreSQL
(sem migration) nem enviado ao AdsPower.

## Endpoints

| Método e caminho | Papéis | Descrição |
|---|---|---|
| `GET /api/v1/adspower/dashboard` | Admin, Manager, Operator | Totais e perfis por grupo. Usa o resultado em memória (padrão 5 minutos). |
| `POST /api/v1/adspower/dashboard/refresh` | Admin, Manager | Nova leitura, ignorando a memória. Sem corpo; CSRF e rate limit `sensitive`. |

Resposta: `generatedAt`, `total`, `fromCache` e três listas:

- `groups[{ groupId, groupName, total, oldestCreatedAt, newestCreatedAt }]`: o AdsPower não informa a criação do
  grupo; as datas são as do perfil mais antigo e do mais recente dele (null se o grupo estiver vazio).
- `tags[{ name, total }]`: tags dos perfis (`fbcc_user_tag`), agrupadas sem diferenciar maiúsculas.
- `profiles[{ profileId, serialNumber, name, groupId, groupName, tags[], createdAt }]`: todos os perfis.

Filtro e ordenação (grupo, tag, nome, quantidade, antiguidade, busca) acontecem no navegador sobre essa lista;
não há nova leitura do AdsPower ao filtrar.
Grupos sem perfis aparecem com total 0; perfis de grupos fora da lista usam o nome informado
no próprio perfil. Respostas `no-store`.

Erros (ProblemDetails com `code`): `Unreachable` e `NotConfigured` → 503; `Unauthorized`
(chave recusada) e `Failed` → 502. Nunca incluem URL, chave ou conteúdo da resposta do AdsPower.

## Escopo atual

Perfis por grupo e por tag, com busca por nome e data de criação (decisões de 12/10/2026). Ativo/inativo foi
removido (o AdsPower não tem esse indicador). **Criador do perfil não existe na API local** (nem v1 nem v2), então
não é exibido; se for necessário, será um cadastro próprio do nosso sistema.

## Como o AdsPower é lido

Documentação: [Query Profile](https://localapi-doc-en.adspower.com/docs/u8m2Ie),
[Query Group](https://localapi-doc-en.adspower.com/docs/zSjKAy),
[visão geral](https://localapi-doc-en.adspower.com/docs/Rdw7Iu).

- `GET /api/v1/group/list` (200 por página) e `GET /api/v1/user/list` (100 por página).
- Cabeçalho `Authorization: Bearer <chave>`. Sem chave o AdsPower responde `Require api-key`.
- Pausa de 500 ms entre chamadas (limite da API local); uma nova tentativa se houver
  "too many requests". Limite de 500 páginas por leitura.
- O conector lê **apenas** `user_id`, `serial_number`, `name`, `group_id`, `group_name`, `created_time` e os nomes em
  `fbcc_user_tag`. A resposta do AdsPower também traz `username`, `password`,
  `remark` e IP da proxy: esses campos são descartados na leitura e nunca guardados, devolvidos ou
  registrados (teste unitário comprova).
- Endereço restrito a `http` em `127.0.0.1`, `localhost` ou `local.adspower.net` (porta 50325)
  até existir um conector seguro aprovado para o ambiente em nuvem.

## Configuração

`appsettings.json`, seção `AdsPower`: `BaseUrl`, `CacheMinutes`,
`RequestIntervalMs`. A chave **nunca** vai para arquivos do repositório:

1. `scripts/Save-LocalAdsPowerKey.ps1` — guarda a chave protegida (DPAPI) em `.local/secrets`.
2. `scripts/Set-LocalAdsPower.ps1` — carrega a chave em `AdsPower__ApiKey` para o processo da API.

A chave foi exposta em conversas; substitua-a por uma nova no AdsPower antes de qualquer uso
fora desta máquina.
