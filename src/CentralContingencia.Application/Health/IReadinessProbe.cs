namespace CentralContingencia.Application.Health;

/// <summary>Verifica se o banco responde e se todas as migrations conhecidas já foram aplicadas. Somente leitura.</summary>
public interface IReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken token);
}
