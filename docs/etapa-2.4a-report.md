# Etapa 2.4A — Domínio de Perfis Operacionais e Alocação Exclusiva

Data: 11/10/2026. Escopo concluído: exclusivamente domínio, testes unitários e
documentação. Instruções AGENTS/CLAUDE e relatórios anteriores inspecionados.
SDK confirmado: .NET 10.0.300; nenhuma dependência adicionada.

## Implementação

- Profiles/ProfileName.cs: Value Object imutável que protege nome não vazio, limite,
  caracteres de controle e igualdade por valor, sem tratar nome como chave única.
- Profiles/OperationalProfile.cs: UUID, nome, plataforma textual validada, estados
  Active/Closed, criação/alteração/encerramento UTC. Sem credenciais de plataformas.
- Allocations/ProxyAllocation.cs: vínculo histórico imutável quanto aos recursos,
  UUIDs perfil/proxy/ator/chave, Reserved/Confirmed/Released/Expired, datas e motivo
  de finalização. Reservas e confirmações são ativas; histórico não é reatribuído.
- Allocations/AllocationDomainService.cs: reserva exclusiva nos dois lados,
  confirmação com revalidação, liberação, expiração explícita e encerramento do perfil
  com finalização da alocação. Valida antes de mutar; reutilização preserva histórico.
- tests/CentralContingencia.UnitTests/AllocationDomainTests.cs: regressões sintéticas
  de ciclos de vida, exclusividade, tempo, validade, estado e encerramento.

Proxy e seus Value Objects foram reutilizados sem alteração. Suspended/Disabled
impedem novas alocações; proxy vencida também. Encerrar perfil não bloqueia nem
reativa uma proxy. O catálogo de plataformas segue pendente; nenhuma inferência de
enum, PLATAFORMA 2 ou integração foi introduzida.

Decisões, relacionamentos, índices parciais propostos e responsabilidades futuras:
[ADR 0008](decisions/0008-operational-profiles-exclusive-allocations.md).

## Validação

```powershell
dotnet build CentralContingencia.slnx --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4a/
dotnet test tests/CentralContingencia.UnitTests --no-restore -p:BaseOutputPath=C:/PROJETOS/CENTRALCONTINGENCIA/.local/build-2.4a/
```

| Verificação final | Resultado |
| --- | --- |
| Build completo da solução | Aprovado; zero erros e avisos |
| Suíte unitária .NET | 179 aprovados; zero falhas e ignorados |
| Novos casos de domínio | 20 aprovados, além dos 159 anteriores |

Build isolado evita substituir
DLLs da API local em execução. Testes puramente em memória; nenhuma conexão ao banco,
gravação, geração/aplicação de migrations ou reinício de servidores foi necessário.

Cobertura nova: exclusividade de proxy e perfil tanto Reserved quanto Confirmed;
proxy Suspended/Disabled/vencida; confirmação no limite do prazo; expiração antecipada
ou indevida; expiração explícita liberando recurso; identidade errada; mudança do
estado da proxy após reserva; encerramento reservado/confirmado/sem vínculo;
reutilização e histórico; perfis encerrados; liberação e estados terminais; UUID/chave
repetidos; prazo inválido; normalização UTC; timestamps regressivos; falha sem mutação
parcial; snapshot inconsistente e preservação de vínculos de outros perfis; nomes iguais
com UUIDs diferentes, metadados inválidos e ToString sem nome de perfil.

## Limitações e pendências

A exclusividade foi validada no domínio para o conjunto completo fornecido, não
homologada contra transações concorrentes: isso exige os dois índices únicos parciais
e transações futuras. Snapshot incompleto não pode ser detectado por um domínio sem
persistência. Não usar o serviço como garantia isolada contra concorrência.

Preservação durável do histórico, atomicidade entre perfil/alocação/auditoria,
persistência, expiração agendada, replay e atualização de /available permanecem para
etapa posterior autorizada. Nenhuma funcionalidade nova está exposta pela API/UI.
Testes de integração e frontend não foram necessários nem executados nesta etapa;
os testes unitários anteriores, incluindo arquitetura/criptografia/importação,
foram executados junto com os novos. O build verificou todos os projetos existentes.

Sem alteração de banco homologado, chaves, autenticação, contratos HTTP, importador,
layout, commits ou deploys. Nenhuma etapa seguinte iniciada automaticamente.
