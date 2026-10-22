# Interface de importação CSV

Acesso: https://localhost:5173/proxies/import, após login com papel Admin.
Na gestão de proxies, usar **Importar CSV**. Manager/Operator não possuem acesso.

1. Selecione CSV UTF-8 (vírgula/ponto e vírgula), até o limite inicial de 2 MiB.
2. Se necessário, informe explicitamente Região complementar; não existe padrão.
3. Clique Gerar prévia. Resumo refere-se ao arquivo completo; tabela mostra
   páginas de 100 linhas e números físicos originais. Cada página reenvia o arquivo.

O campo **Ano de referência (opcional)** começa vazio. Informe um inteiro entre
1 e 9999 para datas dd/MM; sem essa opção, complete cada data no CSV. Datas
dd/MM/yyyy, yyyy-MM-dd e ISO completas são preservadas. A API valida calendário,
anos bissextos e vencimento futuro. Confira **Vencimento interpretado (UTC)** na
tabela; nenhuma data usa automaticamente o ano atual. Alterar o ano descarta
a prévia e a prova. A confirmação repete a opção e o backend verifica seu vínculo.

A opção **Ignorar explicitamente linhas
com host, porta, usuário e senha todos vazios** começa desmarcada; quando marcada,
essas linhas recebem aviso e constam como ignoradas no relatório. Linhas com qualquer
dado de conexão continuam sujeitas a todas as validações. Alterar a opção descarta
a prévia e sua prova. Coluna duplicada inteiramente vazia é ignorada com aviso;
ambiguidades continuam impedindo importação. PERFIL, PLATAFORMA e lote não serão importados.
4. Corrija erros. Quando houver apenas duplicatas/vazios potencialmente ignoráveis,
   revise todas as páginas antes de confirmar. A API revalida o arquivo inteiro.
5. Clique Revisar confirmação e, no diálogo, Confirmar importação. Cancelar
   não grava. Conflitos existentes serão ignorados sem sobrescrever proxies.
6. Leia o relatório e volte à listagem, invalidada automaticamente após sucesso.

A região de cada linha no contrato é apenas um indicador de presença. A tabela
mostra Informada no CSV, Ausente ou a opção complementar fornecida pelo usuário,
sem presumir valores omitidos. Não apresenta usuários/senhas completos.

Arquivo, prova e UUID existem apenas em memória da página. Trocar arquivo/região
descarta a prévia e inicia outra operação. Retry de rede/503 mantém o mesmo UUID;
409 exige nova prévia mantendo o UUID para permitir recuperar um replay. Não há
retry automático. Prova expira após 15 minutos; a API é autoridade final.
Sair da página, recarregar ou trocar sessão perde esse estado; não há retomada
persistente. Em resposta incerta, permaneça na página e tente com a mesma operação.

Prévia inválida não implica gravação parcial: erro 422 apresenta relatório e
nenhum registro é importado. Progresso é indeterminado, sem percentual inventado.
Limites configurados na API podem diferir do limite inicial da seleção; erros
413/429 continuam tratados. Arquivos com muitas páginas podem atingir rate limiting.

Para teste automatizado visual, na raiz, com Vite disponível e Chrome instalado:

```powershell
node scripts/Verify-CsvImportUi.cjs
```

Todas as chamadas /api são interceptadas; mocks possuem banner explícito. Não
faz login nem importa registros no banco homologado. Capturas/results ficam em
.local/verification-2.3c.1. Sem HAR, traces ou conteúdo de arquivo em logs.
Não usar dados reais nem confirmar escrita homologada durante avaliação visual
sem autorização própria. Frontend está implementado, aguardando avaliação manual.
