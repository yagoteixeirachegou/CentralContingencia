# ADR 0013 — Ingestão de perfis pelo n8n

Data: 12/10/2026. Status: implementada e testada localmente.

## Contexto

O fluxo de ingestão do n8n lia a planilha HTTP - FACEBOOK para (1) calcular o próximo número de perfil, (2) achar proxies livres
e (3) marcar a proxy como em uso escrevendo o nome do perfil na linha. Queremos que o n8n use apenas a API da Central, sem Google
Sheets, preservando as regras da planilha.

## Decisões

1. **Regras reproduzidas da planilha:** proxy escolhida na ordem das linhas (usamos a linha do CSV importado, preservada na auditoria de importação).
   **Numeração (revisada em 13/10/2026 pelo usuário):** próximo número = maior `Perfil N` do grupo de ingestão no AdsPower + 1, sem olhar outros grupos nem
   os perfis da Central (que divergiam: 1598 contra 1244 na esteira). Teste usa o grupo `FB Teste | YAGO`; produção usa `03 ESTEIRA DE PERFIL`. Nomes
   repetidos entre grupos são aceitos. O AdsPower é a fonte da verdade; a Central soma apenas as reservas ainda invisíveis a ele, no mesmo grupo.
2. **Duas fases:** reservar (15 minutos) → o n8n cria o perfil no AdsPower → confirmar ou liberar. Nada fica preso se o n8n cair.
3. **Entrega parcial** (decisão do usuário): atende as proxies possíveis e lista as posições pendentes com o motivo.
4. **Senha da proxy para o n8n** (decisão do usuário): devolvida apenas na resposta da reserva, só das proxies do lote, só para
   a credencial de serviço, enquanto a reserva está válida; HTTPS e `no-store`. Exceção consciente à regra "nunca devolver senha":
   o fluxo hoje precisa da senha para montar o perfil no AdsPower. Alternativa futura: a Central criar o perfil no AdsPower
   (o n8n nunca veria a senha), o que exige escrita no AdsPower e aprovação própria.
5. **Credencial própria (`IntegrationKey`)**: chave de serviço com hash SHA-256 na configuração, escopo `profile-ingestion`,
   sem acesso à API das pessoas, comparação em tempo constante e auditoria com o Id da chave. Sem cookie, portanto sem CSRF
   somente nesses endpoints (política que aceita só a chave). Primeira versão sem tabela de chaves; gestão por tela fica para depois.
6. **Número reaproveitado:** perfis de ingestão nunca confirmados (liberados ou vencidos) são encerrados e não contam na
   numeração, como a planilha, onde uma falha não deixa rastro. Só contam os lotes do mesmo grupo (`IngestionBatches.AdsPowerGroupId`).
7. **Vínculo com o AdsPower:** `AdsPowerId` (único) e `AdsPowerSerialNumber` no perfil, gravados na confirmação.
8. **Idempotência por lote** (`IngestionBatches`, único por credencial + chave) com resultado reconstruído do banco; a repetição
   não reatende posições pendentes.

## Riscos e mitigação

- API acessível pelo Docker (`host.docker.internal`) exige escutar além de `localhost`: chave forte, HTTPS, rate limit; manter
  o firewall do Windows restrito à rede privada. Em produção (Railway) a chave de criptografia DPAPI precisa migrar para um cofre.
- A numeração é serializada entre ingestões e demais alocações, mas um perfil criado manualmente na tela no mesmo instante pode
  repetir um número (nomes não são únicos); o AdsPower recusa nome duplicado no fluxo.
- A senha trafega para o n8n e fica no armazenamento de execuções dele: ajustar a retenção/ocultação de dados de execução.
