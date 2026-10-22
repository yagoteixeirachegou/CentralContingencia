namespace CentralContingencia.Domain.WorkQueue;

public enum WorkTaskStatus { Pending, Running, Succeeded, Failed, Cancelled, DeadLetter }

/// <summary>Catálogo fechado de tipos. Um tipo novo exige validador de payload e executor próprios.</summary>
public enum WorkTaskType { SimulatedProcessing }

public enum WorkTaskError { InvalidTransition, LeaseLost }

public sealed class WorkTaskStateException(WorkTaskError error) : InvalidOperationException("Operação inválida para o estado atual da tarefa.")
{
    public WorkTaskError Error { get; } = error;
}
