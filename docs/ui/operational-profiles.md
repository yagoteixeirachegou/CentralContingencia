# Perfis operacionais e alocações — interface

Entrada operacional: **https://localhost:5173/proxies**, menu **Proxies**.
Administração secundária: ação **Administrar perfis**, `/profiles`. Detalhes em
`/profiles/{UUID}`. Usar sessão de homologação existente; cookies e CSRF são os
mesmos da aplicação. Admin/Manager cadastram e alocam; Operator apenas consulta.

## Uso

1. Listagem: filtros de nome/plataforma **exatos**, status, ordenação e paginação
   enviados ao servidor. Não existe busca parcial no contrato atual.
2. Novo perfil: nome, duas atribuições opcionais do catálogo homologado e plataforma
   textual legada opcional. Perfil
   criado não recebe proxy automaticamente; abre sua página de detalhes.
3. Reservar proxy: consultar a seleção paginada de `/proxies/available`, pesquisar
   host exato e escolher uma linha. A API revalida disponibilidade/exclusividade e
   devolve o prazo. Não se envia TTL arbitrário nem status presumido.
4. Confirmar reserva: envio JSON `{}` ao endpoint existente. Backend suporta
   repetição idempotente de Confirmed; cliente evita duplo envio e não repete sozinho.
5. Liberar proxy: abrir a confirmação, revisar o efeito e confirmar ou cancelar.
   A disponibilidade posterior é recalculada pela API, sem alterar status/validade.
6. Histórico: estado, UUID da proxy, datas e motivo, com filtro e paginação próprios.
   O vínculo atual é consultado separadamente e não depende da página do histórico.

Datas em horário local, incluindo segundos. Prazo vencido segundo o relógio local
é indicado e não dispara confirmação/liberação automática. Pode existir Reserved
vencida no histórico até a recuperação transacional do backend. API é autoridade,
inclusive se relógio local ou dados consultados estiverem desatualizados.

## Recuperação de erros

HTTP 409: estado mudou, reserva pode estar vencida ou recurso ocupado. Consultas
são invalidadas; conferir/atualizar antes de nova ação. Nenhum retry automático.
Erro de cadastro com resultado incerto bloqueia reenvio no mesmo formulário: fechar
e consultar a listagem antes de iniciar outro cadastro, pois nome não é chave única.
Sessão expirada segue o fluxo existente de login; 403 exibe acesso negado, e falhas
de leitura oferecem atualização. Não renderizar ProblemDetails bruto ou exceções.

Cache TanStack Query por sessão, descartado ao sair. Queries atuais/histórico
atualizam a cada 15 segundos e após mutações; listagens/proxies são invalidadas.
Dados de operação permanecem somente em memória. Sem tokens/credenciais em storage,
console, exportações ou campos novos. Usuário de proxy exibido como `***`.

Atualizar dados não reserva nem confirma recursos. Leituras independentes podem
detectar snapshots incompatíveis: interface mostra conflito e exige atualização,
sem escolher arbitrariamente uma alocação. UUIDs históricos são mostrados conforme
contrato; não se inventam hosts nem se faz busca N+1 para todas as linhas.

Cadastro/listagem/detalhes e alocações compõem esta etapa. Edição/encerramento de
perfil por interface não foram adicionados, pois não constam da lista autorizada.
Perfil Closed continua consultável e não oferece nova reserva.

Escritas automatizadas de validação somente em banco descartável. Evidências e
comandos em [etapa-2.4c-report.md](../etapa-2.4c-report.md). Aguardar homologação manual.
