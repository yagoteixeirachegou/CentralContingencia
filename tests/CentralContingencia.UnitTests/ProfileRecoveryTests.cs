using System.Security.Cryptography;
using System.Text;
using CentralContingencia.Application.Profiles.Recovery;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.AspNetCore.DataProtection;

namespace CentralContingencia.UnitTests;

public class ProfileRecoveryTests
{
    private static readonly ProxyActor Admin = new(Guid.NewGuid(), ["Admin"]);
    private const string Header = "PERFIL;PLATAFORMA;PLATAFORMA;PROXY;PORTA;USUÁRIO;SENHA;STATUS;DATA DE VENCIMENTO;REGIÃO;ADsPower Lote";
    private const string Row = "Perfil 1;DARKPOST;TIKTOK;synthetic.test;8080;private-user;private-secret;ATIVO;31/12;;derived";
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static Proxy Proxy()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var proxy = CentralContingencia.Domain.Proxies.Proxy.Create(Guid.NewGuid(), ProxyHost.Create("synthetic.test"), ProxyPort.Create(8080), "private-user",
            EncryptedProxyPassword.Create("test", RandomNumberGenerator.GetBytes(12), [1], RandomNumberGenerator.GetBytes(16)), "BR", now.AddDays(1), now);
        proxy.ChangeStatus(ProxyStatus.Active, now); return proxy;
    }
    private static (ProfileRecoveryService Service, Store Store) Setup()
    {
        var store = new Store { Snapshot = new([Proxy()], [], []) };
        return (new(store, new DataProtectionImportConfirmation(new EphemeralDataProtectionProvider()), new(), TimeProvider.System), store);
    }
    private sealed class Store : IProfileRecoveryStore, IProfileRecoveryTransaction
    {
        public RecoverySnapshot Snapshot = new([], [], []);
        public ProfileRecoveryBatch? Batch;
        public Task<RecoverySnapshot> ReadAsync(IReadOnlyCollection<RecoveryCandidate> candidates, CancellationToken token) => Task.FromResult(Snapshot);
        public Task<IProfileRecoveryTransaction> BeginAsync(CancellationToken token) => Task.FromResult<IProfileRecoveryTransaction>(this);
        public Task<ProfileRecoveryBatch?> FindAsync(Guid actor, Guid key, CancellationToken token) => Task.FromResult(Batch?.ActorId == actor && Batch.IdempotencyKey == key ? Batch : null);
        public Task SaveAsync(ProfileRecoveryBatch batch, IReadOnlyList<RecoveryCandidate> candidates, CancellationToken token) { Batch = batch; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [Theory]
    [InlineData("DARKPOST", "TIKTOK")]
    [InlineData("INFOPRODUTO", "")]
    [InlineData("", "INFOPRODUTO")]
    [InlineData("INFOPRODUTO", "INFOPRODUTO")]
    [InlineData("DARKPOST", "")]
    [InlineData("", "TIKTOK")]
    [InlineData("", "")]
    public async Task DuplicatePlatformColumnsPreserveIndependentSlots(string first, string second)
    {
        var (service, _) = Setup(); var csv = Header + "\n" + Row.Replace("DARKPOST;TIKTOK", first + ";" + second);
        var preview = await service.PreviewAsync(Admin, Bytes(csv), false, 1, 20, default);
        Assert.Equal(1, preview.Summary.Proposed); var row = Assert.Single(preview.Items);
        Assert.Equal(string.IsNullOrEmpty(first) ? null : first, row.Platform1); Assert.Equal(string.IsNullOrEmpty(second) ? null : second, row.Platform2);
        Assert.NotNull(preview.ConfirmationToken);
        foreach (var secret in new[] { "private-user", "private-secret", "synthetic.test" }) Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(preview));
    }
    [Theory]
    [InlineData("PLATAFORMA 1;PLATAFORMA 2")]
    [InlineData("PLATAFORMA 1;PLATAFORMA 1")]
    [InlineData("PLATAFORMA;PLATAFORMA 2")]
    public async Task ExplicitHeadersAndAmbiguities(string headers)
    {
        var (service, _) = Setup(); var preview = await service.PreviewAsync(Admin, Bytes(Header.Replace("PLATAFORMA;PLATAFORMA", headers) + "\n" + Row), false, 1, 20, default);
        if (headers == "PLATAFORMA 1;PLATAFORMA 2") Assert.Equal(1, preview.Summary.Proposed);
        else { Assert.Null(preview.ConfirmationToken); Assert.Contains(preview.HeaderIssues, i => i.Code == "DuplicateHeader"); }
    }
    [Theory]
    [InlineData("PROFILE_DUPLICATE")][InlineData("ENDPOINT_DUPLICATE")][InlineData("AMBIGUOUS")][InlineData("NOT_FOUND")][InlineData("EXISTING_PROFILE")][InlineData("BLOCKED")]
    public async Task ConflictsAreExplicitAndNeverProduceProof(string kind)
    {
        var (service, store) = Setup(); var csv = Header + "\n" + Row;
        if (kind.EndsWith("DUPLICATE")) csv += "\n" + (kind == "PROFILE_DUPLICATE" ? Row.Replace("synthetic.test", "other.test") : Row.Replace("Perfil 1", "Perfil 2"));
        if (kind == "AMBIGUOUS") store.Snapshot = store.Snapshot with { Proxies = [Proxy(), Proxy()] };
        if (kind == "NOT_FOUND") store.Snapshot = new([], [], []);
        if (kind == "EXISTING_PROFILE") store.Snapshot = store.Snapshot with { Profiles = [OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("perfil 1"), null, DateTimeOffset.UtcNow)] };
        if (kind == "BLOCKED") store.Snapshot.Proxies[0].ChangeStatus(ProxyStatus.Disabled, DateTimeOffset.UtcNow);
        var preview = await service.PreviewAsync(Admin, Bytes(csv), false, 1, 20, default);
        Assert.True(preview.Summary.Conflicts > 0); Assert.Null(preview.ConfirmationToken); Assert.Null(store.Batch);
    }
    [Fact]
    public async Task IdempotencyAndProofBindAdministratorFileOptionsAndConcurrentState()
    {
        var (service, store) = Setup(); var csv = Bytes(Header + "\n" + Row); var preview = await service.PreviewAsync(Admin, csv, false, 1, 20, default);
        foreach (var (actor, bytes, skip) in new[] { (Admin, csv, true), (new ProxyActor(Guid.NewGuid(), ["Admin"]), csv, false), (Admin, Bytes(Header + "\n" + Row.Replace("TIKTOK", "BM")), false) })
            Assert.Equal(RecoveryFailure.Confirmation, (await Assert.ThrowsAsync<RecoveryException>(() => service.CommitAsync(actor, bytes, skip, preview.ConfirmationToken!, Guid.NewGuid(), "unit", default))).Failure);
        store.Snapshot.Proxies[0].ChangeStatus(ProxyStatus.Suspended, DateTimeOffset.UtcNow);
        Assert.Equal(RecoveryFailure.Confirmation, (await Assert.ThrowsAsync<RecoveryException>(() => service.CommitAsync(Admin, csv, false, preview.ConfirmationToken!, Guid.NewGuid(), "unit", default))).Failure);
        store.Snapshot.Proxies[0].ChangeStatus(ProxyStatus.Active, DateTimeOffset.UtcNow);
        preview = await service.PreviewAsync(Admin, csv, false, 1, 20, default); var key = Guid.NewGuid();
        var report = await service.CommitAsync(Admin, csv, false, preview.ConfirmationToken!, key, "unit", default);
        var replay = await service.CommitAsync(Admin, csv, false, "expired proof", key, "unit", default);
        Assert.True(replay.Replayed); Assert.Equal(report.RecoveryId, replay.RecoveryId);
        Assert.Equal(RecoveryFailure.IdempotencyConflict, (await Assert.ThrowsAsync<RecoveryException>(() => service.CommitAsync(Admin, Bytes(Header + "\n" + Row + "\n"), false, "", key, "unit", default))).Failure);
    }
    [Fact]
    public async Task MissingConnectionRequiresExplicitSkipAndLineNumbersAreOriginal()
    {
        var (service, _) = Setup(); var csv = Bytes(Header + "\n" + Row + "\nPerfil incompleto;DARKPOST;;;;;;;;;\n");
        var rejected = await service.PreviewAsync(Admin, csv, false, 1, 20, default); Assert.Equal(1, rejected.Summary.Invalid);
        var accepted = await service.PreviewAsync(Admin, csv, true, 2, 1, default); Assert.Equal(3, Assert.Single(accepted.Items).Line);
        Assert.Equal(1, accepted.Summary.Ignored); Assert.NotNull(accepted.ConfirmationToken);
    }
    [Theory]
    [InlineData("Manager")][InlineData("Operator")]
    public async Task OnlyAdminMayRecover(string role)
    {
        var (service, store) = Setup(); var actor = new ProxyActor(Guid.NewGuid(), [role]);
        Assert.Equal(RecoveryFailure.Forbidden, (await Assert.ThrowsAsync<RecoveryException>(() => service.PreviewAsync(actor, ReadOnlyMemory<byte>.Empty, false, 1, 20, default))).Failure);
        Assert.Equal(RecoveryFailure.Forbidden, (await Assert.ThrowsAsync<RecoveryException>(() => service.CommitAsync(actor, ReadOnlyMemory<byte>.Empty, false, "", Guid.NewGuid(), "unit", default))).Failure);
        Assert.Null(store.Batch);
    }
    [Theory]
    [InlineData("")][InlineData("PERFIL;PROXY;PORTA\n\"broken")]
    public async Task MalformedCsvFailsSafely(string csv)
    { var (service, _) = Setup(); await Assert.ThrowsAsync<CsvPreviewException>(() => service.PreviewAsync(Admin, Bytes(csv), false, 1, 20, default)); }
    [Fact]
    public async Task BomCommaQuotedMetadataAndOriginalPhysicalLinesAreSupported()
    {
        var (service, _) = Setup(); var csv = "\uFEFFPERFIL,PLATAFORMA 1,PLATAFORMA 2,PROXY,PORTA\r\n\"Perfil,\r\nmultilinha\",DARKPOST,,synthetic.test,8080";
        // Domain rejects controls inside operational profile names, despite a structurally valid CSV.
        var invalid = await service.PreviewAsync(Admin, Bytes(csv), false, 1, 20, default); Assert.Equal(2, invalid.Items[0].Line); Assert.Equal(1, invalid.Summary.Invalid);
        var valid = await service.PreviewAsync(Admin, Bytes(csv.Replace(",\r\nmultilinha", ", válido")), false, 1, 20, default);
        Assert.Equal(1, valid.Summary.Proposed); Assert.Equal("Perfil, válido", valid.Items[0].ProfileName);
    }
    [Fact]
    public async Task ExpiredProofCannotCreateRecoveryReceipt()
    {
        var (_, store) = Setup(); var clock = new Clock(); var service = new ProfileRecoveryService(store, new DataProtectionImportConfirmation(new EphemeralDataProtectionProvider()), new(), clock);
        var csv = Bytes(Header + "\n" + Row); var preview = await service.PreviewAsync(Admin, csv, false, 1, 20, default); clock.Now = clock.Now.AddMinutes(16);
        Assert.Equal(RecoveryFailure.Confirmation, (await Assert.ThrowsAsync<RecoveryException>(() => service.CommitAsync(Admin, csv, false, preview.ConfirmationToken!, Guid.NewGuid(), "unit", default))).Failure);
        Assert.Null(store.Batch);
    }
    [Fact]
    public async Task LegitimateAssignmentDoesNotUnblockDuplicateProfileOrIncompleteConnection()
    {
        var (service, store) = Setup();
        var proxy = Proxy();
        var now = DateTimeOffset.UtcNow;
        var other = CentralContingencia.Domain.Proxies.Proxy.Create(Guid.NewGuid(), ProxyHost.Create("other.test"), ProxyPort.Create(8080), "private-user", proxy.Password, "BR", now.AddDays(1), now);
        other.ChangeStatus(ProxyStatus.Active, now);
        store.Snapshot = new([proxy, other], [], []);
        var csv = Bytes(Header + "\n" + Row.Replace("Perfil 1", "1218").Replace("DARKPOST;TIKTOK", "INFOPRODUTO;") + "\n" + Row.Replace("Perfil 1", "1218").Replace("synthetic.test", "other.test") + "\n;INFOPRODUTO;;;;;;;;;");
        var preview = await service.PreviewAsync(Admin, csv, true, 1, 20, default);
        Assert.Equal(2, preview.Summary.Conflicts); Assert.Equal(1, preview.Summary.Ignored);
        Assert.Equal(0, preview.Summary.Invalid); Assert.Null(preview.ConfirmationToken);
        Assert.All(preview.Items.Take(2), r => Assert.Contains(r.Issues, i => i.Code == "DuplicateProfile"));
        var ignored = preview.Items[2]; Assert.Equal(4, ignored.Line); Assert.Empty(ignored.Operations);
        Assert.Contains(ignored.Issues, i => i.Code == "NoConnectionSkipped"); Assert.Null(store.Batch);
        var partial = await service.PreviewAsync(Admin, Bytes(Header + "\n;INFOPRODUTO;;synthetic.test;;;;;;;"), true, 1, 20, default);
        Assert.Equal(0, partial.Summary.Ignored); Assert.Equal(1, partial.Summary.Invalid);
    }
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
