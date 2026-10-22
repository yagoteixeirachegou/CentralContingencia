namespace CentralContingencia.Application.Allocations;

public sealed class ReservationOptions
{
    public int ReservationMinutes { get; set; } = 15;

    public void Validate()
    {
        if (ReservationMinutes is < 1 or > 1440)
            throw new InvalidOperationException("ProxyAllocations:ReservationMinutes deve estar entre 1 e 1440.");
    }

    public DateTimeOffset Deadline(DateTimeOffset now, DateTimeOffset proxyExpiresAt, DateTimeOffset? requested)
    {
        Validate();
        var maximum = now.AddMinutes(ReservationMinutes);
        if (proxyExpiresAt < maximum) maximum = proxyExpiresAt;
        if (maximum <= now) throw new AllocationPersistenceException(AllocationPersistenceFailure.Conflict);
        if (requested is { } deadline && (deadline <= now || deadline > maximum))
            throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation);
        return requested ?? maximum;
    }
}
