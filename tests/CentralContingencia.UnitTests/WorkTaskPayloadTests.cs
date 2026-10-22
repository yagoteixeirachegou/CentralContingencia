using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.UnitTests;

public class WorkTaskPayloadTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void ValidPayloadGetsDefaultsAndCanonicalForm()
    {
        var validated = WorkTaskPayloads.Validate(WorkTaskType.SimulatedProcessing, Json("""{ "version": 1 }"""));
        Assert.Equal(1, validated.Version);
        Assert.Equal("""{"version":1,"steps":1,"stepDelayMs":0,"behavior":"Succeed"}""", validated.Json);
    }

    [Fact]
    public void FailUntilAttemptRoundTrips()
    {
        var validated = WorkTaskPayloads.Validate(WorkTaskType.SimulatedProcessing, Json("""{"version":1,"steps":3,"stepDelayMs":10,"behavior":"FailUntilAttempt","succeedOnAttempt":3}"""));
        var parsed = SimulatedPayload.FromJson(validated.Json);
        Assert.Equal(new SimulatedPayload(1, 3, 10, SimulatedBehavior.FailUntilAttempt, 3), parsed);
    }

    [Theory]
    [InlineData("""{"version":1,"password":"x"}""")]
    [InlineData("""{"version":1,"cookies":"x"}""")]
    [InlineData("""{"version":1,"token":"x"}""")]
    [InlineData("""{"version":1,"Steps":2}""")]
    [InlineData("""{"steps":1}""")]
    [InlineData("""{"version":2}""")]
    [InlineData("""{"version":"1"}""")]
    [InlineData("""{"version":1.5}""")]
    [InlineData("""{"version":1,"steps":0}""")]
    [InlineData("""{"version":1,"steps":51}""")]
    [InlineData("""{"version":1,"stepDelayMs":-1}""")]
    [InlineData("""{"version":1,"stepDelayMs":5001}""")]
    [InlineData("""{"version":1,"behavior":"succeed"}""")]
    [InlineData("""{"version":1,"behavior":1}""")]
    [InlineData("""{"version":1,"behavior":"2"}""")]
    [InlineData("""{"version":1,"behavior":"Other"}""")]
    [InlineData("""{"version":1,"succeedOnAttempt":2}""")]
    [InlineData("""{"version":1,"behavior":"FailUntilAttempt"}""")]
    [InlineData("""{"version":1,"behavior":"FailUntilAttempt","succeedOnAttempt":1}""")]
    [InlineData("""{"version":1,"behavior":"FailUntilAttempt","succeedOnAttempt":11}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("""7""")]
    [InlineData("""{"version":1,"steps":null}""")]
    public void InvalidPayloadsAreRejected(string json) =>
        Assert.Throws<WorkTaskPayloadException>(() => WorkTaskPayloads.Validate(WorkTaskType.SimulatedProcessing, Json(json)));

    [Fact]
    public void MissingPayloadIsRejected() =>
        Assert.Throws<WorkTaskPayloadException>(() => WorkTaskPayloads.Validate(WorkTaskType.SimulatedProcessing, default));

    [Fact]
    public void UnknownTypeValueIsRejected() =>
        Assert.Throws<WorkTaskPayloadException>(() => WorkTaskPayloads.Validate((WorkTaskType)42, Json("""{"version":1}""")));

    [Theory]
    [InlineData("SimulatedProcessing", true)]
    [InlineData("simulatedprocessing", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("AdsPowerProfileCheck", false)]
    public void OnlyExactCatalogNamesAreTypes(string? name, bool expected) => Assert.Equal(expected, WorkTaskPayloads.TryParseType(name, out _));

    [Fact]
    public void StoredPayloadWithGarbageIsRejectedByTheExecutorParser() =>
        Assert.Throws<WorkTaskPayloadException>(() => SimulatedPayload.FromJson("não é json"));

    [Theory]
    [InlineData(1, 5, 5)]
    [InlineData(2, 5, 10)]
    [InlineData(3, 5, 20)]
    [InlineData(10, 5, 300)]
    [InlineData(100, 5, 300)]
    public void RetryDelayIsExponentialAndCapped(int attempt, int baseSeconds, int expectedSeconds)
    {
        var options = new WorkQueueOptions { RetryBaseDelaySeconds = baseSeconds, RetryMaxDelaySeconds = 300 };
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.RetryDelay(attempt));
    }

    [Theory]
    [InlineData(4, 5, 300, 3)]
    [InlineData(60, 5, 4, 3)]
    [InlineData(60, 5, 300, 0)]
    [InlineData(60, 5, 300, 11)]
    [InlineData(60, -1, 300, 3)]
    public void InvalidQueueOptionsAreRejected(int lease, int baseDelay, int maxDelay, int attempts) =>
        Assert.Throws<InvalidOperationException>(() => new WorkQueueOptions { LeaseSeconds = lease, RetryBaseDelaySeconds = baseDelay, RetryMaxDelaySeconds = maxDelay, DefaultMaxAttempts = attempts }.Validate());

    [Fact]
    public void DefaultQueueOptionsAreValid() => new WorkQueueOptions().Validate();
}
