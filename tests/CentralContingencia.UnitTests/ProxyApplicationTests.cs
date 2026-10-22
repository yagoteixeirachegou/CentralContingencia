using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.UnitTests;

public class ProxyApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProxyActor Manager = new(Guid.NewGuid(), [AccessRoles.Manager]);
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Protector : IProxyPasswordProtector
    {
        public Guid LastId; public int Calls;
        public EncryptedProxyPassword Encrypt(Guid id, string password)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException();
            LastId = id; Calls++;
            return EncryptedProxyPassword.Create("test-v1", new byte[12], [1], new byte[16]);
        }
        public string? Plain;
        public string Decrypt(Guid id, EncryptedProxyPassword envelope) => Plain ?? throw new InvalidOperationException("Leituras não podem descriptografar.");
    }
    private sealed class Store : IProxyStore
    {
        public Proxy? Proxy; public ProxyAuditEvent? Audit; public bool Conflict; public int Writes;
        public Task<Proxy?> FindAsync(Guid id, CancellationToken token) => Task.FromResult(Proxy?.Id == id ? Proxy : null);
        public Task<bool> HasActiveAllocationAsync(Guid id, DateTimeOffset now, CancellationToken token) => Task.FromResult(false);
        public Task<ProxyPage> QueryAsync(ValidatedProxyQuery query, DateTimeOffset now, CancellationToken token) =>
            Task.FromResult(new ProxyPage([], query.Page, query.PageSize, 0));
        public List<ProxyAuditEvent> Extra = [];
        public Task AddAuditAsync(ProxyAuditEvent audit, CancellationToken token) { Extra.Add(audit); return Task.CompletedTask; }
        public Task<bool> SaveAsync(Proxy proxy, ProxyAuditEvent audit, bool isNew, CancellationToken token)
        { if (Conflict) return Task.FromResult(false); Proxy = proxy; Audit = audit; Writes++; return Task.FromResult(true); }
    }
    private static CreateProxyRequest Request(string status = "Active", int port = 8080, DateTimeOffset? expiry = null) => new()
        { Host = "PROXY.example.test", Port = port, Username = "synthetic-user", Password = "synthetic-secret", Region = "BR", Status = status, ExpiresAt = expiry ?? Now.AddDays(1) };

    [Fact]
    public async Task CreationEncryptsBeforeSavingAndReturnsOnlySafeFields()
    {
        var store = new Store(); var protector = new Protector();
        var service = new ProxyManagementService(store, () => protector, new FixedClock());
        var response = await service.CreateAsync(Manager, Request(), "unit-correlation", default);
        Assert.Equal(response.Id, protector.LastId); Assert.Equal(1, protector.Calls);
        Assert.Equal("proxy.example.test", response.Host); Assert.Equal("***", response.UsernameMasked);
        Assert.True(response.Available); Assert.Equal(ProxyAuditAction.Created, store.Audit!.Action);
        Assert.Equal(Manager.Id, store.Audit.ActorId); Assert.Equal(response.Id, store.Audit.ProxyId);
        Assert.DoesNotContain("synthetic-secret", response.ToString());
        Assert.DoesNotContain("synthetic-secret", Request().ToString());
    }
    [Theory]
    [InlineData("Operator")][InlineData("Integration")][InlineData("unknown")]
    public async Task UnprivilegedActorsCannotWrite(string role)
    {
        var store = new Store(); var service = new ProxyManagementService(store, () => new Protector(), new FixedClock());
        var actor = new ProxyActor(Guid.NewGuid(), [role]);
        var error = await Assert.ThrowsAsync<ProxyOperationException>(() => service.CreateAsync(actor, Request(), "test", default));
        Assert.Equal(ProxyFailure.Forbidden, error.Failure); Assert.Equal(0, store.Writes);
        error = await Assert.ThrowsAsync<ProxyOperationException>(() => service.UpdateAsync(actor, Guid.NewGuid(), new() { Status = "Active" }, "test", default));
        Assert.Equal(ProxyFailure.Forbidden, error.Failure);
    }
    [Theory]
    [InlineData("Admin")][InlineData("Manager")][InlineData("Operator")]
    public async Task KnownRolesCanReadWithoutResolvingEncryption(string role)
    {
        var service = new ProxyManagementService(new Store(), () => throw new InvalidOperationException(), new FixedClock());
        Assert.Empty((await service.ListAsync(new(Guid.NewGuid(), [role]), new(), false, default)).Items);
    }
    [Fact]
    public async Task InvalidCreationAndMissingEncryptionHaveNoWrites()
    {
        var store = new Store(); var service = new ProxyManagementService(store, () => new Protector(), new FixedClock());
        foreach (var request in new[] { Request("Available"), Request(port: 0), Request(expiry: Now), Request(expiry: DateTimeOffset.MaxValue) })
            Assert.Equal(ProxyFailure.Validation, (await Assert.ThrowsAsync<ProxyOperationException>(() => service.CreateAsync(Manager, request, "test", default))).Failure);
        service = new(store, () => throw new InvalidOperationException("synthetic-config-secret"), new FixedClock());
        var error = await Assert.ThrowsAsync<ProxyOperationException>(() => service.CreateAsync(Manager, Request(), "test", default));
        Assert.Equal(ProxyFailure.EncryptionUnavailable, error.Failure); Assert.DoesNotContain("synthetic-config-secret", error.ToString());
        Assert.Equal(0, store.Writes);
    }
    [Fact]
    public async Task PartialUpdatePreservesEnvelopeAndCredentialsUnlessPasswordIsProvided()
    {
        var store = new Store(); var protector = new Protector(); var service = new ProxyManagementService(store, () => protector, new FixedClock());
        var created = await service.CreateAsync(Manager, Request(), "create", default);
        var envelope = store.Proxy!.Password;
        var updated = await service.UpdateAsync(Manager, created.Id, new() { Region = "AR", Status = "Suspended" }, "update", default);
        Assert.Same(envelope, store.Proxy.Password); Assert.Equal(1, protector.Calls);
        Assert.Equal("synthetic-user", store.Proxy.Username); Assert.Equal("AR", updated.Region); Assert.False(updated.Available);
        Assert.Equal(ProxyAuditAction.Updated, store.Audit!.Action);
        await service.UpdateAsync(Manager, created.Id, new() { Password = "synthetic-next" }, "update", default);
        Assert.Equal(2, protector.Calls);
    }
    private static readonly ProxyActor Admin = new(Guid.NewGuid(), [AccessRoles.Admin]);
    [Fact]
    public async Task AdminRevealsCredentialsAndTheRevealIsAudited()
    {
        var store = new Store(); var protector = new Protector { Plain = "synthetic-secret" };
        var service = new ProxyManagementService(store, () => protector, new FixedClock());
        var created = await service.CreateAsync(Manager, Request(), "create", default);
        var credentials = await service.RevealCredentialsAsync(Admin, created.Id, "reveal-correlation", default);
        Assert.Equal("synthetic-secret", credentials.Password); Assert.Equal("synthetic-user", credentials.Username);
        Assert.Equal("proxy.example.test", credentials.Host); Assert.Equal(8080, credentials.Port);
        var audit = Assert.Single(store.Extra);
        Assert.Equal(ProxyAuditAction.Revealed, audit.Action); Assert.Equal(Admin.Id, audit.ActorId); Assert.Equal(created.Id, audit.ProxyId);
        Assert.DoesNotContain("synthetic-secret", credentials.ToString()); Assert.DoesNotContain("synthetic-secret", audit.CorrelationId);
    }
    [Theory]
    [InlineData("Manager")][InlineData("Operator")][InlineData("Integration")][InlineData("unknown")]
    public async Task OnlyAdminCanRevealCredentials(string role)
    {
        var store = new Store(); var protector = new Protector { Plain = "synthetic-secret" };
        var service = new ProxyManagementService(store, () => protector, new FixedClock());
        var created = await service.CreateAsync(Manager, Request(), "create", default);
        var error = await Assert.ThrowsAsync<ProxyOperationException>(() => service.RevealCredentialsAsync(new(Guid.NewGuid(), [role]), created.Id, "reveal", default));
        Assert.Equal(ProxyFailure.Forbidden, error.Failure); Assert.Empty(store.Extra);
    }
    [Fact]
    public async Task RevealFailuresDoNotAuditOrLeak()
    {
        var store = new Store(); var service = new ProxyManagementService(store, () => new Protector(), new FixedClock());
        Assert.Equal(ProxyFailure.NotFound, (await Assert.ThrowsAsync<ProxyOperationException>(() => service.RevealCredentialsAsync(Admin, Guid.NewGuid(), "reveal", default))).Failure);
        var created = await service.CreateAsync(Manager, Request(), "create", default);
        var error = await Assert.ThrowsAsync<ProxyOperationException>(() => service.RevealCredentialsAsync(Admin, created.Id, "reveal", default));
        Assert.Equal(ProxyFailure.EncryptionUnavailable, error.Failure); Assert.Empty(store.Extra);
    }
    [Fact]
    public async Task NotFoundEmptyPatchAndConflictsAreDistinct()
    {
        var store = new Store(); var service = new ProxyManagementService(store, () => new Protector(), new FixedClock());
        Assert.Equal(ProxyFailure.NotFound, (await Assert.ThrowsAsync<ProxyOperationException>(() => service.GetAsync(Manager, Guid.NewGuid(), default))).Failure);
        Assert.Equal(ProxyFailure.Validation, (await Assert.ThrowsAsync<ProxyOperationException>(() => service.UpdateAsync(Manager, Guid.NewGuid(), new(), "test", default))).Failure);
        store.Conflict = true;
        Assert.Equal(ProxyFailure.Conflict, (await Assert.ThrowsAsync<ProxyOperationException>(() => service.CreateAsync(Manager, Request(), "test", default))).Failure);
        Assert.Equal(0, store.Writes);
    }
    [Theory]
    [InlineData(0,20,"createdAt","desc")][InlineData(10001,20,"createdAt","desc")]
    [InlineData(1,101,"createdAt","desc")][InlineData(1,0,"createdAt","desc")]
    [InlineData(1,20,"password","desc")][InlineData(1,20,"host;drop table","asc")][InlineData(1,20,"host","invalid")]
    public void QueryLimitsAndSortWhitelistAreEnforced(int page, int size, string sort, string direction)
    {
        Assert.Equal(ProxyFailure.Validation, Assert.Throws<ProxyOperationException>(() =>
            ProxyManagementService.ValidateQuery(new(Page: page, PageSize: size, Sort: sort, Direction: direction), false)).Failure);
    }
    [Fact]
    public void AuditRejectsInvalidActorsAndMetadata()
    {
        Assert.Throws<ArgumentException>(() => new ProxyAuditEvent(Guid.Empty, Guid.NewGuid(), ProxyAuditAction.Created, Now, "test"));
        Assert.Throws<ArgumentException>(() => new ProxyAuditEvent(Guid.NewGuid(), Guid.NewGuid(), (ProxyAuditAction)999, Now, "test"));
        Assert.Throws<ArgumentException>(() => new ProxyAuditEvent(Guid.NewGuid(), Guid.NewGuid(), ProxyAuditAction.Created, Now, new string('a',129)));
    }
}
