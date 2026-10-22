using System.Text.Json;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Application.WorkQueue;

public sealed class WorkTaskPayloadException() : Exception("Payload da tarefa inválido.");

public sealed record ValidatedPayload(int Version, string Json);

public enum SimulatedBehavior { Succeed, FailTransient, FailPermanent, FailUntilAttempt }

/// <summary>
/// Payload do tipo SimulatedProcessing, versão 1. Validação estrita: só os campos conhecidos são aceitos, então não há onde
/// transportar credenciais, cookies ou tokens. Dados sensíveis devem ser referenciados por id de registro protegido, nunca embutidos.
/// </summary>
public sealed record SimulatedPayload(int Version, int Steps, int StepDelayMs, SimulatedBehavior Behavior, int? SucceedOnAttempt)
{
    public const int CurrentVersion = 1;
    private static readonly HashSet<string> Allowed = ["version", "steps", "stepDelayMs", "behavior", "succeedOnAttempt"];

    public static SimulatedPayload FromElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new WorkTaskPayloadException();
        foreach (var property in element.EnumerateObject())
            if (!Allowed.Contains(property.Name)) throw new WorkTaskPayloadException();
        var version = Int(element, "version", required: true, 0) ;
        if (version != CurrentVersion) throw new WorkTaskPayloadException();
        var steps = Int(element, "steps", required: false, 1);
        var delay = Int(element, "stepDelayMs", required: false, 0);
        if (steps is < 1 or > 50 || delay is < 0 or > 5000) throw new WorkTaskPayloadException();
        var behavior = SimulatedBehavior.Succeed;
        if (element.TryGetProperty("behavior", out var raw))
        {
            if (raw.ValueKind != JsonValueKind.String || raw.GetString() is not { } name || !Enum.GetNames<SimulatedBehavior>().Contains(name)) throw new WorkTaskPayloadException();
            behavior = Enum.Parse<SimulatedBehavior>(name);
        }
        int? succeedOn = null;
        if (element.TryGetProperty("succeedOnAttempt", out _)) succeedOn = Int(element, "succeedOnAttempt", required: true, 0);
        if (behavior == SimulatedBehavior.FailUntilAttempt != succeedOn.HasValue || succeedOn is < 2 or > WorkTask.MaxAttemptsLimit) throw new WorkTaskPayloadException();
        return new(version, steps, delay, behavior, succeedOn);
    }

    public static SimulatedPayload FromJson(string json)
    {
        try { using var document = JsonDocument.Parse(json); return FromElement(document.RootElement); }
        catch (JsonException) { throw new WorkTaskPayloadException(); }
    }

    public string ToCanonicalJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version); writer.WriteNumber("steps", Steps); writer.WriteNumber("stepDelayMs", StepDelayMs);
            writer.WriteString("behavior", Behavior.ToString());
            if (SucceedOnAttempt is { } attempt) writer.WriteNumber("succeedOnAttempt", attempt);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static int Int(JsonElement element, string name, bool required, int fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return required ? throw new WorkTaskPayloadException() : fallback;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw new WorkTaskPayloadException();
    }
}

public static class WorkTaskPayloads
{
    public const int MaxCanonicalBytes = 2048;

    public static bool TryParseType(string? name, out WorkTaskType type)
    {
        // Só o nome exato do catálogo: números e variações de caixa não são tipos.
        type = default;
        if (name is null || !Enum.GetNames<WorkTaskType>().Contains(name)) return false;
        type = Enum.Parse<WorkTaskType>(name);
        return true;
    }

    public static ValidatedPayload Validate(WorkTaskType type, JsonElement payload)
    {
        var validated = type switch
        {
            WorkTaskType.SimulatedProcessing => ValidateSimulated(payload),
            _ => throw new WorkTaskPayloadException(),
        };
        if (System.Text.Encoding.UTF8.GetByteCount(validated.Json) > MaxCanonicalBytes) throw new WorkTaskPayloadException();
        return validated;
    }

    private static ValidatedPayload ValidateSimulated(JsonElement payload)
    {
        var parsed = SimulatedPayload.FromElement(payload);
        return new(parsed.Version, parsed.ToCanonicalJson());
    }
}
