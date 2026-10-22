Abra o perfil {{PERFIL}} (user_id: {{PERFIL_ID}}) do grupo {{GRUPO}} no AdsPower.
O Facebook já está autenticado nesse perfil.

### Etapa 1 — Verificação inicial
Carregue UMA página: https://www.facebook.com/ e aguarde terminar de carregar.
Apenas observe o estado da página. Não role, não clique, não abra Reels, não curta,
não comente, não envie mensagens nem solicitações, e não altere nada no perfil.

Classifique o resultado em UMA categoria:
Ok | Captcha | VerificationRequested | LoginRequired | IdentityConfirmation | Checkpoint | AccountRestriction | NavigationError

- Se for diferente de Ok: NÃO tente contornar, confirmar nem repetir.
  NÃO feche o navegador nem chame stop. Deixe a página aberta como está,
  registre a categoria e encerre.
- Se for Ok: prossiga para a Etapa 2.

### Etapa 2 — Sessão de navegação (~{{DURACAO}})
Execute uma sessão com duração total aproximada de {{DURACAO}}.

Durante a sessão:
- Comece navegando normalmente pelo Feed do Facebook.
- Role a página e visualize alguns dos conteúdos encontrados.
- Assista a alguns vídeos que aparecerem durante a navegação.
- Durante toda a sessão, curta aproximadamente {{CURTIDAS}} publicações ou vídeos,
  distribuindo essas interações ao longo do tempo. Não é necessário curtir todos
  os conteúdos visualizados.
- Se a aba Reels estiver disponível, acesse-a durante a sessão.
- Na aba Reels, assista a alguns vídeos e alterne entre eles.
- As ~{{CURTIDAS}} curtidas podem ser distribuídas entre publicações do Feed e Reels.
- NÃO faça comentários, compartilhamentos, solicitações de amizade, envio de
  mensagens ou alterações no perfil durante este teste.
- NÃO execute várias interações simultaneamente ou em sequência imediata.
- Quando o tempo total da sessão atingir aproximadamente {{DURACAO}}, encerre.

### Etapa 3 — Condições de segurança (durante toda a execução)
Interrompa imediatamente a execução caso o Facebook apresente:
CAPTCHA; solicitação de verificação; pedido de login ou nova autenticação;
confirmação de identidade; checkpoint; aviso ou restrição relacionada à conta;
erro que impeça a navegação normal.

Nesses casos: NÃO tente contornar, confirmar automaticamente ou repetir.
Interrompa, deixe a página aberta no estado em que se encontra para análise
e NÃO feche o navegador nem chame stop. Registre a categoria.

### Etapa 4 — Encerramento (somente se outcome = Ok)
Feche o perfil pela API local do AdsPower:
GET {{API_BASE}}/api/v1/browser/stop?user_id={{PERFIL_ID}}
Header: Authorization: Bearer {{ADSPOWER_BEARER}}

Confirme que a resposta trouxe "code": 0.
Se não, aguarde 2 segundos e tente uma única vez mais.

### Etapa 5 — Registro
POST /api/v1/warmup/checks (Admin/Manager, cookie + CSRF):
{
  "adsPowerProfileId": "{{PERFIL_ID}}",
  "profileName": "{{PERFIL}}",
  "adsPowerGroupId": "{{GRUPO}}",
  "outcome": "<categoria final>"
}

### Resposta final (apenas isto)
perfil: {{PERFIL}}
grupo: {{GRUPO}}
categoria: <outcome>
data/hora UTC: <timestamp>
perfil fechado: <sim|não>
mensagem: "Sessão concluída, sem impedimentos, perfil fechado."   # só se outcome = Ok
