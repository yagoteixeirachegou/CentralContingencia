using System.Security.Cryptography;
using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Proxies;

public sealed class ProxyManagementService(IProxyStore store, Func<IProxyPasswordProtector> protector, TimeProvider clock)
    : IProxyManagementService
{
    public async Task<ProxyPage> ListAsync(ProxyActor actor, ProxyQuery query, bool availableOnly, CancellationToken token)
    {
        Authorize(actor, false);
        return await store.QueryAsync(ValidateQuery(query, availableOnly), clock.GetUtcNow(), token);
    }
    public async Task<ProxyResponse> GetAsync(ProxyActor actor, Guid id, CancellationToken token)
    {
        Authorize(actor, false); ValidateId(id);
        var proxy = await store.FindAsync(id, token) ?? throw new ProxyOperationException(ProxyFailure.NotFound);
        return Response(proxy, clock.GetUtcNow(), await store.HasActiveAllocationAsync(id, clock.GetUtcNow(), token));
    }
    public async Task<ProxyResponse> CreateAsync(ProxyActor actor, CreateProxyRequest request, string correlationId, CancellationToken token)
    {
        Authorize(actor, true);
        var now = clock.GetUtcNow(); var id = Guid.NewGuid();
        Proxy proxy;
        try
        {
            var host = ProxyHost.Create(request.Host); var port = ProxyPort.Create(request.Port);
            var status = ParseStatus(request.Status); var expiry = ValidateExpiry(request.ExpiresAt, now);
            ValidateMetadata(request.Username, request.Region);
            proxy = Proxy.Create(id, host, port, request.Username, Encrypt(id, request.Password), request.Region, expiry, now);
            proxy.ChangeStatus(status, now);
        }
        catch (ArgumentException) { throw new ProxyOperationException(ProxyFailure.Validation); }
        var audit = new ProxyAuditEvent(actor.Id, id, ProxyAuditAction.Created, now, correlationId);
        if (!await store.SaveAsync(proxy, audit, true, token)) throw new ProxyOperationException(ProxyFailure.Conflict);
        return Response(proxy, now, await store.HasActiveAllocationAsync(proxy.Id, now, token));
    }
    public async Task<ProxyResponse> UpdateAsync(ProxyActor actor, Guid id, UpdateProxyRequest request, string correlationId, CancellationToken token)
    {
        Authorize(actor, true); ValidateId(id);
        if (request.Host is null && request.Port is null && request.Username is null && request.Password is null &&
            request.Region is null && request.Status is null && request.ExpiresAt is null)
            throw new ProxyOperationException(ProxyFailure.Validation);
        var proxy = await store.FindAsync(id, token) ?? throw new ProxyOperationException(ProxyFailure.NotFound);
        var now = clock.GetUtcNow();
        try
        {
            var host = request.Host is null ? proxy.Host : ProxyHost.Create(request.Host);
            var port = request.Port is null ? proxy.Port : ProxyPort.Create(request.Port.Value);
            var status = request.Status is null ? proxy.Status : ParseStatus(request.Status);
            var expiry = request.ExpiresAt is null ? proxy.ExpiresAt : ValidateExpiry(request.ExpiresAt, now);
            var username = request.Username ?? proxy.Username; var region = request.Region ?? proxy.Region;
            ValidateMetadata(username, region);
            var password = request.Password is null ? null : Encrypt(id, request.Password);
            proxy.UpdateDetails(host, port, username, region, expiry, status, now);
            if (password is not null) proxy.ReplaceEncryptedPassword(password, now);
        }
        catch (ArgumentException) { throw new ProxyOperationException(ProxyFailure.Validation); }
        var audit = new ProxyAuditEvent(actor.Id, id, ProxyAuditAction.Updated, now, correlationId);
        if (!await store.SaveAsync(proxy, audit, false, token)) throw new ProxyOperationException(ProxyFailure.Conflict);
        return Response(proxy, now, await store.HasActiveAllocationAsync(proxy.Id, now, token));
    }
    // Revelação sob demanda: somente Admin; a auditoria é gravada antes de qualquer texto claro sair do serviço.
    public async Task<ProxyCredentials> RevealCredentialsAsync(ProxyActor actor, Guid id, string correlationId, CancellationToken token)
    {
        if (actor.Id == Guid.Empty || !actor.Roles.Contains(AccessRoles.Admin)) throw new ProxyOperationException(ProxyFailure.Forbidden);
        ValidateId(id);
        var proxy = await store.FindAsync(id, token) ?? throw new ProxyOperationException(ProxyFailure.NotFound);
        string password;
        try { password = protector().Decrypt(id, proxy.Password); }
        catch (Exception error) when (error is InvalidOperationException or CryptographicException)
        { throw new ProxyOperationException(ProxyFailure.EncryptionUnavailable); }
        await store.AddAuditAsync(new ProxyAuditEvent(actor.Id, id, ProxyAuditAction.Revealed, clock.GetUtcNow(), correlationId), token);
        return new ProxyCredentials(proxy.Id, proxy.Host.Value, proxy.Port.Value, proxy.Username, password);
    }
    private EncryptedProxyPassword Encrypt(Guid id, string password)
    {
        try { return protector().Encrypt(id, password); }
        catch (Exception error) when (error is InvalidOperationException or CryptographicException)
        { throw new ProxyOperationException(ProxyFailure.EncryptionUnavailable); }
    }
    private static void Authorize(ProxyActor actor, bool write)
    {
        if (actor.Id == Guid.Empty || !actor.Roles.Any(role => role is AccessRoles.Admin or AccessRoles.Manager || (!write && role == AccessRoles.Operator)))
            throw new ProxyOperationException(ProxyFailure.Forbidden);
    }
    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ProxyOperationException(ProxyFailure.Validation);
    }
    public static ProxyResponse Response(Proxy proxy, DateTimeOffset now, bool hasActiveAllocation = false) => new(proxy.Id, proxy.Host.Value,
        proxy.Port.Value, "***", proxy.Status.ToString(), proxy.Region, proxy.ExpiresAt, proxy.CreatedAt,
        proxy.UpdatedAt, proxy.IsAvailable(now, hasActiveAllocation));

    public static ValidatedProxyQuery ValidateQuery(ProxyQuery query, bool availableOnly)
    {
        try
        {
            if (query.Page is < 1 or > 10000 || query.PageSize is < 1 or > 100 ||
                query.Sort is not ("host" or "port" or "status" or "region" or "expiresAt" or "createdAt" or "updatedAt") ||
                query.Direction is not ("asc" or "desc")) throw new ArgumentException();
            var host = query.Host is null ? null : ProxyHost.Create(query.Host);
            if (query.Port is not null) ProxyPort.Create(query.Port.Value);
            if (query.Region is not null) ValidateText(query.Region, 100);
            var status = query.Status is null ? (ProxyStatus?)null : ParseStatus(query.Status);
            if (query.ExpiresBefore is not null) ValidateFinite(query.ExpiresBefore.Value);
            return new(query.Page, query.PageSize, host, query.Port, query.Region, status,
                query.ExpiresBefore?.ToUniversalTime(), query.Sort, query.Direction == "desc", availableOnly);
        }
        catch (ArgumentException) { throw new ProxyOperationException(ProxyFailure.Validation); }
    }
    private static ProxyStatus ParseStatus(string value) => ProxyInputRules.ParseStatus(value);
    private static DateTimeOffset ValidateExpiry(DateTimeOffset? value, DateTimeOffset now) => ProxyInputRules.ValidateExpiry(value, now);
    private static void ValidateFinite(DateTimeOffset value) => ProxyRules.ValidateTimestamp(value);
    private static void ValidateMetadata(string username, string region) { ProxyRules.ValidateUsername(username); ProxyRules.ValidateRegion(region); }
    private static void ValidateText(string value, int length) => ProxyRules.ValidateText(value, length);
}
