# ADR 0016 — Preparação para implantação em contêiner (Railway)

Data: 21/10/2026. Status: aceita (Etapa 2A autorizada pelo usuário). Detalhes operacionais: `docs/RAILWAY_DEPLOYMENT.md`.

## Decisões
- Um único contêiner: a API serve o build do React (`wwwroot`) na mesma origem, preservando cookies `__Host-`/`SameSite=Strict` e CSRF.
- Proxy reverso: `ReverseProxy:Enabled` (desligado por padrão) com lista explícita de IPs/redes (`KnownProxies`/`KnownNetworks`); `ForwardLimit=1`;
  sem lista a API recusa iniciar. O rate limit usa o IP real somente quando o proxy é confiável.
- Data Protection: `DataProtection:Store=Database` (tabela `security.DataProtectionKeys`, migration aditiva) com chaves cifradas por certificado X.509
  vindo de variável de ambiente. Padrão `Default` mantém o desenvolvimento local inalterado.
- Fora de Development a API valida a configuração na inicialização (conexão, Data Protection, proxy reverso, `AllowedHosts`) e nunca aplica migrations sozinha.
- Integrações locais (AdsPower/n8n) ficam atrás de `LocalIntegrations:Enabled` (padrão: ligado só em Development); desligadas respondem 503 `LocalIntegrationUnavailable`.
- `/health/ready` (porta `IReadinessProbe`, implementada na Infrastructure) verifica banco e migrations pendentes, sem detalhes na resposta.
- `System.Security.Cryptography.Xml`/`Pkcs` fixados em 10.0.13 por avisos de vulnerabilidade trazidos pelo pacote de Data Protection.

## Consequências
Funcionalidades locais deixam de operar na nuvem até existir o worker (Etapa 2B). A origem do proxy da Railway precisa ser confirmada antes do primeiro deploy.
Nenhuma regra de negócio, contrato de API existente ou criptografia de proxies foi alterada.
