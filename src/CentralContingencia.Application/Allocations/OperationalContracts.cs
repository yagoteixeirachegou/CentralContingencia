using System.Text.Json.Serialization;
using CentralContingencia.Application.Proxies;

namespace CentralContingencia.Application.Allocations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateProfileRequest
{
    public string Name { get; init; } = "";
    public string? Platform { get; init; }
    public string? Platform1 { get; init; }
    public string? Platform2 { get; init; }
    public override string ToString() => "[create profile]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateProfileRequest
{
    public string? Name { get; init; }
    public string? Platform { get; init; }
    private string? platform1;
    private string? platform2;
    public string? Platform1 { get => platform1; init { platform1 = value; Platform1Specified = true; } }
    public string? Platform2 { get => platform2; init { platform2 = value; Platform2Specified = true; } }
    [JsonIgnore] public bool Platform1Specified { get; private set; }
    [JsonIgnore] public bool Platform2Specified { get; private set; }
    public override string ToString() => "[update profile]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReserveAllocationRequest
{
    public Guid ProfileId { get; init; }
    public Guid ProxyId { get; init; }
    public DateTimeOffset? ReservationExpiresAt { get; init; }
}
public sealed record ProfileResponse(Guid Id, string Name, string Platform, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ClosedAt, string? Platform1 = null, string? Platform2 = null);
public sealed record AllocationResponse(Guid Id, Guid ProfileId, Guid ProxyId, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset ReservationExpiresAt, DateTimeOffset? ConfirmedAt, DateTimeOffset? EndedAt, string? EndReason);
public sealed record ProfilePage(IReadOnlyList<ProfileResponse> Items, int Page, int PageSize, int Total);
public sealed record AllocationPage(IReadOnlyList<AllocationResponse> Items, int Page, int PageSize, int Total);
public sealed record ProfileQuery(int Page = 1, int PageSize = 20, string? Name = null, string? Platform = null, string? Status = null, string Sort = "createdAt", string Direction = "desc");
public sealed record AllocationQuery(int Page = 1, int PageSize = 20, Guid? ProfileId = null, Guid? ProxyId = null, string? Status = null, string Sort = "createdAt", string Direction = "desc");
public interface IOperationalManagementService
{
    Task<ProfileResponse> CreateAsync(ProxyActor actor, CreateProfileRequest request, string correlation, CancellationToken token);
    Task<ProfileResponse> GetAsync(ProxyActor actor, Guid id, CancellationToken token);
    Task<ProfilePage> ListAsync(ProxyActor actor, ProfileQuery query, CancellationToken token);
    Task<ProfileResponse> UpdateAsync(ProxyActor actor, Guid id, UpdateProfileRequest request, string correlation, CancellationToken token);
    Task CloseAsync(ProxyActor actor, Guid id, string correlation, CancellationToken token);
    Task<AllocationResponse> ReserveAsync(ProxyActor actor, ReserveAllocationRequest request, Guid key, string correlation, CancellationToken token);
    Task<AllocationResponse> GetAllocationAsync(ProxyActor actor, Guid id, CancellationToken token);
    Task<AllocationPage> ListAllocationsAsync(ProxyActor actor, AllocationQuery query, CancellationToken token);
    Task<AllocationPage> HistoryAsync(ProxyActor actor, Guid profileId, AllocationQuery query, CancellationToken token);
    Task ConfirmAsync(ProxyActor actor, Guid id, string correlation, CancellationToken token);
    Task ReleaseAsync(ProxyActor actor, Guid id, string correlation, CancellationToken token);
}
