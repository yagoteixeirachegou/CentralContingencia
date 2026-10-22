# Gestão de proxies — interface local

Acesse https://localhost:5173/proxies com sua sessão interna. O frontend permanece
no proxy HTTPS de mesma origem; não há cadastro público ou senha persistida legível.
Admin e Manager cadastram/editam; Operator consulta. A API revalida permissões.

## Consulta

- Host e região são pesquisas exatas; DNS/IP é normalizado pelo backend e região
  diferencia capitalização. Porta, status e vencimento máximo são opcionais.
- “Somente disponíveis” consome GET /api/v1/proxies/available. Não testa conexão
  de rede nem controla alocações futuras. Ativo e disponível são conceitos distintos.
- Ordenação: host, porta, região, status, vencimento, criação ou atualização;
  crescente/decrescente. Cabeçalhos ordenáveis também acionam ordenação no servidor.
- Páginas de 10/20/50/100; limite da API de 10000 páginas preservado. “Resultados”
  é total filtrado informado pela API; a tabela não filtra uma página em memória.
- Cartões são totais globais consultados separadamente, independentes dos filtros.
  Não somam linhas visíveis. Vencidos corresponde a expiresBefore inclusivo com
  instante UTC informado pelo navegador. Não pressupor snapshot atômico entre
  cartões. Atualização a cada 60s, ao voltar à página/aba conforme staleTime e manual.
- Datas são exibidas no horário local. O selo de disponibilidade é sempre o valor
  do servidor na última consulta; o relógio local apenas marca vencimento na linha.

## Cadastro e edição

Host DNS ASCII/IPv4 canônico/IPv6 sem protocolo ou porta; porta 1–65535;
usuário 1–128 caracteres válidos; região 1–100; senha 1–1024 bytes UTF-8;
novo vencimento futuro. Status: Ativo, Suspenso ou Desativado. Cadastro inicia
como Desativado para exigir escolha explícita antes de disponibilizar uma conexão.

A edição consulta o detalhe, mostra o usuário mascarado e deixa novas credenciais
em branco. Campo vazio preserva a credencial. Campos iguais são omitidos; vencimento
não editado conserva toda a precisão original, inclusive se já vencido. PATCH vazio
é impedido no cliente. Não há opção de apagar senha ou recuperar senha persistida.
Cadastro/edição usam o CSRF existente e invalidam listagem, detalhe e indicadores.

Após 409, feche o formulário e reabra para consultar o estado atualizado antes de
decidir alterar novamente. A API não aceita ETag/versão no PATCH nesta etapa:
não há promessa de prevenir toda edição baseada em dados antigos. Sem repetição
automática de gravações; após falha de rede, consulte a lista antes de repetir POST.

## Desenvolvimento e validação

Executar os comandos do README para API com chave local, frontend e testes.
Nenhuma migration nova nesta etapa. Para avaliação da interface vazia, o ambiente
homologado permanece sem proxies. Não cadastre nele durante uma validação automatizada.

```powershell
cd src/central-contingencia-web
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run dev
# outro terminal, na raiz:
node scripts/Verify-ProxyUi.cjs
./scripts/Test-ProxyUiIntegration.ps1
```

O teste de integração exige PostgreSQL local 127.0.0.1:55432 e .env configurado,
SDK .NET 10 e Chrome. Cria role/banco p6_ui_test_UUID, aplica migrations apenas
nesse banco, gera credenciais e chave temporárias, inicia API 7091/Vite 5174,
valida e remove o ambiente isolado. Recusa nomes/destinos não previstos ou porta
7091 ocupada. Vite 5174 também usa strictPort. Permissão local CREATE ROLE/DATABASE
é necessária. Rate limits são ampliados somente na instância descartável para os
cenários sequenciais; os limites originais continuam cobertos pelos testes .NET.
Resultados/capturas identificados em .local/verification-2.2b. Chrome ignora a
confiança dos certificados autoassinados apenas nesse contexto de teste.
