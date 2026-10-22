# Criptografia de senhas de proxy — execução local

Leia a [ADR 0003](../decisions/0003-proxy-domain-encryption.md).
Na Etapa 2.2A existem endpoints de proxies, mas não telas. A autenticação existente
e consultas de proxies funcionam sem a chave. Criação/troca de senha exige a chave
e a migration de auditoria. Ambas as migrations foram aplicadas localmente após aprovação.

## Chave de desenvolvimento no Windows

Na raiz, uma única vez, sem transcrição de terminal nem logging de variáveis:

```powershell
./scripts/Initialize-LocalProxyEncryption.ps1
```

O script gera 32 bytes aleatórios, grava somente o valor protegido por DPAPI em
.local/secrets/proxy-development-v1.dpapi e recusa sobrescrita. Não imprime a chave.
O arquivo depende do usuário Windows atual e deve permanecer fora do Git.
Não apague/recrie uma chave que já cifrou registros: perder a chave impede a leitura.
Faça backup protegido separado do banco; DPAPI exige também acesso ao perfil Windows.

Para iniciar a API com essa chave em um terminal dedicado:

```powershell
. ./scripts/Set-LocalProxyEncryption.ps1
try {
    ./scripts/Start-LocalApi.ps1
} finally {
    [Environment]::SetEnvironmentVariable('ProxyEncryption__Keys__development-v1', $null, 'Process')
    Remove-Item Env:ProxyEncryption__ActiveKeyId -ErrorAction SilentlyContinue
}
```

O carregador descriptografa somente em memória e define configuração no processo;
o filho .NET a herda. Não imprima env, config, argumentos de depuração ou dumps.
Somente encerre o terminal depois de parar a API. Start-LocalApi não carrega
chaves automaticamente nem aplica migrations. Não há senha fixa de proxy/Admin.

Em outro ambiente, configure ProxyEncryption__ActiveKeyId e
ProxyEncryption__Keys__{KeyId} por um mecanismo protegido, com chave aleatória
Base64 de exatamente 32 bytes. Evite KeyId diferentes apenas pela capitalização,
pois providers de configuração .NET podem ser case insensitive.
Use chaves diferentes por ambiente e finalidade. Produção requer secret manager,
controle de acesso e plano de recuperação próprios; não foi configurada.

## Migration para revisão, sem execução automática

SQL: [etapa-2.1-migration.sql](etapa-2.1-migration.sql).
Antes de aplicar ao banco homologado: aprovação explícita, novo backup e revisão.
A aprovação da Etapa 1.1 não autoriza esta migration. Não use o antigo script
Migrate-LocalDatabase.ps1 sem revisão: ele agora aplicaria também a migration nova.

Comando de geração, sem alteração de banco:

```powershell
dotnet ef migrations script 20261011015548_InitialIdentityAndAuthenticationAudit `
  --project src/CentralContingencia.Infrastructure `
  --startup-project src/CentralContingencia.Infrastructure `
  --output docs/security/etapa-2.1-migration.sql
```

Após aprovação, preparar a conexão local e aplicar somente a migration revisada:

```powershell
. ./scripts/Set-LocalDatabase.ps1
dotnet ef database update AddProxyPersistence `
  --project src/CentralContingencia.Infrastructure `
  --startup-project src/CentralContingencia.Infrastructure
```

Esse último comando foi autorizado e executado na Etapa 2.2A após backup/revisão.
AddProxyAudit foi gerada separadamente em docs/security/etapa-2.2a-migration.sql;
foi aplicada ao banco homologado após aprovação específica e novo backup. Não aplicar
automaticamente todas as migrations com base na autorização somente da Etapa 2.1.
Não reprovisionar Admin, não executar Down/rollback nem remover volume.

## Validação

```powershell
dotnet build CentralContingencia.slnx --no-restore
./scripts/Test-LocalPostgres.ps1
```

Os testes usam chaves e credenciais sintéticas em memória, bancos locais
p6_auth_test_UUID criados/removidos pela fixture, nunca importam dados reais.
Não precisam da chave DPAPI. Sem CENTRAL_TEST_POSTGRES os testes PostgreSQL
são ignorados; use o script para executá-los de fato.
Os limites de nonce, metadados legíveis e rotação estão na ADR 0003.
