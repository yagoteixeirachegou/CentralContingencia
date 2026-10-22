using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Proxies;

public sealed record OperationalGridQuery(int Page = 1, int PageSize = 20, string? Profile = null,
    string? Region = null, string? Platform = null, string? Availability = null,
    string Sort = "expiresAt", string Direction = "asc");
public sealed record OperationalGridRow(Guid ProxyId, Guid? ProfileId, string? ProfileName,
    string? Platform1, string? Platform2, string Host, int Port, string Region,
    DateTimeOffset ExpiresAt, string Status, string Availability, Guid? AllocationId,
    DateTimeOffset? ReservationExpiresAt);
public sealed record OperationalGridPage(IReadOnlyList<OperationalGridRow> Items, int Page, int PageSize,
    int Total, DateTimeOffset EvaluatedAt);
public interface IOperationalGridReader
{
    Task<OperationalGridPage> QueryAsync(OperationalGridQuery query, CancellationToken token);
}
public interface IOperationalGridService
{
    Task<OperationalGridPage> ListAsync(ProxyActor actor, OperationalGridQuery query, CancellationToken token);
}
public sealed class OperationalGridService(IOperationalGridReader reader) : IOperationalGridService
{
    public Task<OperationalGridPage> ListAsync(ProxyActor actor, OperationalGridQuery query, CancellationToken token)
    {
        if (actor.Id == Guid.Empty || !actor.Roles.Any(role => role is AccessRoles.Admin or AccessRoles.Manager or AccessRoles.Operator))
            throw new AllocationPersistenceException(AllocationPersistenceFailure.Forbidden);
        if (query.Page is < 1 or > 10000 || query.PageSize is < 1 or > 100 || query.Sort != "expiresAt" ||
            query.Direction is not ("asc" or "desc") || query.Availability is not (null or "Available" or "Reserved" or "InUse" or "Unavailable"))
            throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation);
        try
        {
            if (query.Profile is not null) ProxyRules.ValidateText(query.Profile, 200);
            if (query.Region is not null) ProxyRules.ValidateText(query.Region, 100);
            if (query.Platform is not null) ProfileAssignments.Validate(query.Platform);
        }
        catch (ArgumentException) { throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation); }
        return reader.QueryAsync(query with { Profile = query.Profile?.Trim(), Region = query.Region?.Trim(), Platform = query.Platform?.Trim() }, token);
    }
}
