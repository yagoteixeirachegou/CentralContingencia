using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Allocations;

public sealed class OperationalManagementService(IOperationalProfileStore profiles, IAllocationStore allocations, TimeProvider clock) : IOperationalManagementService
{
    public async Task<ProfileResponse> CreateAsync(ProxyActor actor, CreateProfileRequest request, string correlation, CancellationToken token)
    {
        Authorize(actor, true);
        OperationalProfile profile;
        try { profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create(request.Name), request.Platform, clock.GetUtcNow(), request.Platform1, request.Platform2); }
        catch (ArgumentException) { throw Failure(AllocationPersistenceFailure.Validation); }
        await profiles.AddAsync(profile, token, new(actor.Id, correlation)); return Response(profile);
    }
    public async Task<ProfileResponse> GetAsync(ProxyActor actor, Guid id, CancellationToken token)
    { Authorize(actor, false); return Response(await FindProfile(id, token)); }
    public Task<ProfilePage> ListAsync(ProxyActor actor, ProfileQuery query, CancellationToken token)
    {
        Authorize(actor, false); ValidatePage(query.Page, query.PageSize, query.Direction);
        if (query.Sort is not ("name" or "platform" or "status" or "createdAt" or "updatedAt") || query.Status is not (null or "Active" or "Closed")) throw Failure(AllocationPersistenceFailure.Validation);
        try { if (query.Name is not null) ProfileName.Create(query.Name); if (query.Platform is not null) ProxyRules.ValidateText(query.Platform, 100); }
        catch (ArgumentException) { throw Failure(AllocationPersistenceFailure.Validation); }
        return profiles.QueryAsync(query, token);
    }
    public async Task<ProfileResponse> UpdateAsync(ProxyActor actor, Guid id, UpdateProfileRequest request, string correlation, CancellationToken token)
    {
        Authorize(actor, true); ValidateId(id);
        if (request.Name is null && request.Platform is null && !request.Platform1Specified && !request.Platform2Specified) throw Failure(AllocationPersistenceFailure.Validation);
        return Response(await profiles.UpdateAsync(id, request.Name, request.Platform, token, new(actor.Id, correlation),
            request.Platform1Specified, request.Platform1, request.Platform2Specified, request.Platform2));
    }
    public Task CloseAsync(ProxyActor actor, Guid id, string correlation, CancellationToken token)
    { Authorize(actor, true); ValidateId(id); return allocations.CloseProfileAsync(id, token, new(actor.Id, correlation)); }
    public async Task<AllocationResponse> ReserveAsync(ProxyActor actor, ReserveAllocationRequest request, Guid key, string correlation, CancellationToken token)
    {
        Authorize(actor, true); ValidateId(request.ProfileId); ValidateId(request.ProxyId); ValidateId(key);
        return Response(await allocations.ReserveAsync(new(Guid.NewGuid(), request.ProfileId, request.ProxyId, actor.Id, key, request.ReservationExpiresAt), token, new(actor.Id, correlation)));
    }
    public async Task<AllocationResponse> GetAllocationAsync(ProxyActor actor, Guid id, CancellationToken token)
    {
        Authorize(actor, false); ValidateId(id);
        return Response(await allocations.FindAsync(id, token) ?? throw Failure(AllocationPersistenceFailure.NotFound));
    }
    public Task<AllocationPage> ListAllocationsAsync(ProxyActor actor, AllocationQuery query, CancellationToken token)
    {
        Authorize(actor, false); ValidatePage(query.Page, query.PageSize, query.Direction);
        if (query.Sort is not ("status" or "createdAt" or "updatedAt") || query.Status is not (null or "Reserved" or "Confirmed" or "Released" or "Expired")) throw Failure(AllocationPersistenceFailure.Validation);
        if (query.ProfileId.HasValue) ValidateId(query.ProfileId.Value); if (query.ProxyId.HasValue) ValidateId(query.ProxyId.Value);
        return allocations.QueryAsync(query, token);
    }
    public async Task<AllocationPage> HistoryAsync(ProxyActor actor, Guid profileId, AllocationQuery query, CancellationToken token)
    { Authorize(actor, false); await FindProfile(profileId, token); return await ListAllocationsAsync(actor, query with { ProfileId = profileId }, token); }
    public Task ConfirmAsync(ProxyActor actor, Guid id, string correlation, CancellationToken token)
    { Authorize(actor, true); ValidateId(id); return allocations.ConfirmAsync(id, token, new(actor.Id, correlation)); }
    public Task ReleaseAsync(ProxyActor actor, Guid id, string correlation, CancellationToken token)
    { Authorize(actor, true); ValidateId(id); return allocations.ReleaseAsync(id, token, new(actor.Id, correlation)); }
    private async Task<OperationalProfile> FindProfile(Guid id, CancellationToken token)
    { ValidateId(id); return await profiles.FindAsync(id, token) ?? throw Failure(AllocationPersistenceFailure.NotFound); }
    private static void Authorize(ProxyActor actor, bool write)
    {
        if (actor.Id == Guid.Empty || !(actor.Roles.Contains(AccessRoles.Admin) || actor.Roles.Contains(AccessRoles.Manager) || !write && actor.Roles.Contains(AccessRoles.Operator)))
            throw Failure(AllocationPersistenceFailure.Forbidden);
    }
    private static void ValidateId(Guid id) { if (id == Guid.Empty) throw Failure(AllocationPersistenceFailure.Validation); }
    private static void ValidatePage(int page, int size, string direction)
    { if (page is < 1 or > 10000 || size is < 1 or > 100 || direction is not ("asc" or "desc")) throw Failure(AllocationPersistenceFailure.Validation); }
    private static AllocationPersistenceException Failure(AllocationPersistenceFailure failure) => new(failure);
    public static ProfileResponse Response(OperationalProfile p) => new(p.Id, p.Name.Value, p.Platform ?? "", p.Status.ToString(), p.CreatedAt, p.UpdatedAt, p.ClosedAt, p.Platform1, p.Platform2);
    public static AllocationResponse Response(ProxyAllocation a) => new(a.Id, a.ProfileId, a.ProxyId, a.Status.ToString(), a.CreatedAt, a.UpdatedAt, a.ReservationExpiresAt, a.ConfirmedAt, a.EndedAt, a.EndReason?.ToString());
}
