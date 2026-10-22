using System.Security.Cryptography;
using CentralContingencia.Application.AdsPower;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Ingestion;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.UnitTests;

public sealed class ProfileIngestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Perfil 1239", true, 1239)]
    [InlineData("perfil   7", true, 7)]
    [InlineData("  PERFIL 15  ", true, 15)]
    [InlineData("Perfil 999999999", true, 999_999_999)]
    [InlineData("Perfil 1234567890", false, 0)]
    [InlineData("Perfil", false, 0)]
    [InlineData("Perfil 12 (Sávio)", false, 0)]
    [InlineData("Perfil 12a", false, 0)]
    [InlineData("Meu Perfil 12", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void Parses_only_exact_profile_numbers(string? name, bool valid, int expected)
    {
        Assert.Equal(valid, ProfileNumber.TryParse(name, out var number)); Assert.Equal(expected, number);
    }

    [Fact]
    public void Formats_profile_names_and_rejects_out_of_range_numbers()
    {
        Assert.Equal("Perfil 1240", ProfileNumber.Format(1240));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfileNumber.Format(0)); Assert.Throws<ArgumentOutOfRangeException>(() => ProfileNumber.Format(1_000_000_000));
    }

    private static OperationalProfile Profile(string name = "Perfil 1") => OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create(name), null, Now);

    [Fact]
    public void Links_the_adspower_profile_once_and_accepts_the_same_values_again()
    {
        var profile = Profile();
        Assert.True(profile.LinkAdsPower("k1abc12", "1234", Now.AddMinutes(1))); Assert.Equal("k1abc12", profile.AdsPowerId); Assert.Equal("1234", profile.AdsPowerSerialNumber);
        Assert.False(profile.LinkAdsPower("k1abc12", "1234", Now.AddMinutes(2))); Assert.False(profile.LinkAdsPower("k1abc12", null, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => profile.LinkAdsPower("other-id", null, Now.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => profile.LinkAdsPower("k1abc12", "999", Now.AddMinutes(3)));
    }

    [Theory]
    [InlineData("ab")] [InlineData("bad id")] [InlineData("id!")] [InlineData("")] [InlineData("a b c")]
    public void Rejects_invalid_adspower_ids(string id) => Assert.Throws<ArgumentException>(() => Profile().LinkAdsPower(id, null, Now));

    [Theory]
    [InlineData("12ab")] [InlineData("-1")] [InlineData("123456789012345678901")]
    public void Rejects_invalid_serial_numbers(string serial) => Assert.Throws<ArgumentException>(() => Profile().LinkAdsPower("k1abc12", serial, Now));

    [Fact]
    public void Ingestion_marker_is_set_once_and_closed_profiles_cannot_be_linked()
    {
        var profile = Profile(); var batch = Guid.NewGuid();
        profile.MarkIngested(batch); Assert.Equal(batch, profile.IngestionBatchId);
        Assert.Throws<InvalidOperationException>(() => profile.MarkIngested(Guid.NewGuid())); Assert.Throws<ArgumentException>(() => Profile().MarkIngested(Guid.Empty));
        var closed = Profile(); new AllocationDomainService().CloseProfile(closed, [], Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => closed.LinkAdsPower("k1abc12", null, Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(0)] [InlineData(51)] [InlineData(-3)]
    public void Batch_requires_a_quantity_between_1_and_50(int count) =>
        Assert.Throws<ArgumentException>(() => ProfileIngestionBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), count, null, Now));

    [Fact]
    public void Batch_validates_ids_and_platform()
    {
        Assert.Equal(50, ProfileIngestionBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 50, "ESTEIRA DE PERFIL", Now).Requested);
        Assert.Throws<ArgumentException>(() => ProfileIngestionBatch.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 1, null, Now));
        Assert.Throws<ArgumentException>(() => ProfileIngestionBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, null, Now));
        Assert.Throws<ArgumentException>(() => ProfileIngestionBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "INEXISTENTE", Now));
    }

    // ---- caso de uso ----
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Protector(bool fail = false) : IProxyPasswordProtector
    {
        public EncryptedProxyPassword Encrypt(Guid proxyId, string password) => throw new NotSupportedException();
        public string Decrypt(Guid proxyId, EncryptedProxyPassword password) => fail ? throw new CryptographicException() : "secret-" + proxyId.ToString("N");
    }
    private sealed class Store(Func<IngestionBatchRequest, IngestionBatchResult> reserve) : IIngestionStore
    {
        public int Reserves { get; private set; }
        public IngestionBatchRequest? LastRequest { get; private set; }
        public List<(string GroupId, int Floor)> Capacities { get; } = [];
        public Task<IngestionCapacity> CapacityAsync(string groupId, int numberFloor, CancellationToken token) { Capacities.Add((groupId, numberFloor)); return Task.FromResult(new IngestionCapacity(1, numberFloor + 1)); }
        public Task<IngestionBatchResult> ReserveBatchAsync(IngestionBatchRequest request, CancellationToken token) { Reserves++; LastRequest = request; return Task.FromResult(reserve(request)); }
        public Task ConfirmAsync(Guid allocationId, Guid actorId, string adsPowerId, string? serialNumber, string correlationId, CancellationToken token) => Task.CompletedTask;
        public Task ReleaseAsync(Guid allocationId, Guid actorId, string correlationId, CancellationToken token) => Task.CompletedTask;
        public List<(Guid BatchId, Guid ActorId)> BatchReleases { get; } = [];
        public Task<IngestionBatchRelease> ReleaseBatchAsync(Guid batchId, Guid actorId, string correlationId, CancellationToken token) { BatchReleases.Add((batchId, actorId)); return Task.FromResult(new IngestionBatchRelease(batchId, 2, 1, 0)); }
    }
    private static Proxy MakeProxy(int i) { var proxy = Proxy.Create(Guid.NewGuid(), ProxyHost.Create($"u{i}.synthetic.test"), ProxyPort.Create(8000 + i), "user" + i,
        EncryptedProxyPassword.Create("k", RandomNumberGenerator.GetBytes(12), [1, 2, 3], RandomNumberGenerator.GetBytes(16)), "Brasil", Now.AddDays(10), Now.AddHours(-1)); proxy.ChangeStatus(ProxyStatus.Active, Now.AddHours(-1)); return proxy; }

    private static IngestionAllocation Allocated(int number, ProxyAllocation? confirmed = null, Action<ProxyAllocation>? shape = null)
    {
        var profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create(ProfileNumber.Format(number)), null, Now.AddMinutes(-1));
        var proxy = MakeProxy(number); var allocation = new AllocationDomainService().Reserve(Guid.NewGuid(), profile, proxy, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-1), Now.AddMinutes(14), []);
        shape?.Invoke(allocation);
        return new(profile, allocation, proxy);
    }
    private static IngestionBatchResult Result(int requested, params IngestionAllocation[] allocations) =>
        new(ProfileIngestionBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), requested, null, Now.AddMinutes(-1)), false, allocations);
    private sealed class FakeReader(string groupId = "10906211", string name = "FB Teste | YAGO", int highest = 1243) : IAdsPowerGroupReader
    {
        public List<string> Asked { get; } = []; public AdsPowerFailure? Fail { get; set; }
        public Task<AdsPowerGroupSnapshot> ReadGroupAsync(string groupName, CancellationToken token)
        { Asked.Add(groupName); return Fail is { } failure ? throw new AdsPowerException(failure) : Task.FromResult(new AdsPowerGroupSnapshot(groupId, name, 22, highest)); }
    }
    private static ProfileIngestionService Build(IIngestionStore store, Func<IProxyPasswordProtector> passwords, TimeProvider time, FakeReader? reader = null, ProfileIngestionOptions? options = null) =>
        new(store, passwords, reader ?? new FakeReader(), options ?? new ProfileIngestionOptions(), time);
    private static ProfileIngestionService Service(Store store, Protector? protector = null) => Build(store, () => protector ?? new Protector(), new Clock());
    private static readonly IngestionActor Actor = new(Guid.NewGuid(), "n8n");

    [Fact]
    public async Task Reports_complete_partial_and_empty_with_pending_positions_after_the_delivered_ones()
    {
        var complete = await Service(new Store(_ => Result(2, Allocated(10), Allocated(11)))).ReserveAsync(Actor, new(2, null), Guid.NewGuid(), "c", default);
        Assert.Equal(("Complete", 2, 0), (complete.Status, complete.Reserved, complete.Pending.Count));
        var partial = await Service(new Store(_ => Result(5, Allocated(10), Allocated(11), Allocated(12)))).ReserveAsync(Actor, new(5, null), Guid.NewGuid(), "c", default);
        Assert.Equal(("Partial", 3, 2), (partial.Status, partial.Reserved, partial.Pending.Count));
        Assert.Equal([4, 5], partial.Pending.Select(p => p.Position)); Assert.All(partial.Pending, p => Assert.Equal("NoProxyAvailable", p.Reason));
        var empty = await Service(new Store(_ => Result(3))).ReserveAsync(Actor, new(3, null), Guid.NewGuid(), "c", default);
        Assert.Equal(("Empty", 0, 3), (empty.Status, empty.Reserved, empty.Pending.Count)); Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Delivers_the_connection_only_for_valid_reservations_and_orders_items_by_profile_number()
    {
        var confirmed = Allocated(12, shape: a => a.GetType().GetMethod("Confirm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(a, [Now]));
        var lapsed = Allocated(13, shape: a => { });
        var live = Allocated(11); var reserved = Allocated(10);
        // Reserva vencida: o relógio do serviço está além do prazo.
        var late = Build(new Store(_ => Result(4, confirmed, lapsed, live, reserved)), () => new Protector(), new LateClock());
        var result = await late.ReserveAsync(Actor, new(4, null), Guid.NewGuid(), "c", default);
        Assert.Equal([10, 11, 12, 13], result.Items.Select(i => i.ProfileNumber));
        Assert.Equal(["Expired", "Expired", "Confirmed", "Expired"], result.Items.Select(i => i.Status)); // vencidas e confirmada: nenhuma conexão sai
        Assert.All(result.Items, item => Assert.Null(item.Proxy));
        var fresh = await Service(new Store(_ => Result(3, Allocated(21), Allocated(20), confirmed))).ReserveAsync(Actor, new(3, null), Guid.NewGuid(), "c", default);
        Assert.Equal([12, 20, 21], fresh.Items.Select(i => i.ProfileNumber));
        Assert.Null(fresh.Items[0].Proxy); Assert.Equal("Confirmed", fresh.Items[0].Status);
        Assert.NotNull(fresh.Items[1].Proxy); Assert.StartsWith("secret-", fresh.Items[1].Proxy!.Password); Assert.Equal("Reserved", fresh.Items[1].Status);
    }
    private sealed class LateClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now.AddMinutes(20); }

    [Fact]
    public async Task Redacts_proxy_credentials_from_diagnostic_text()
    {
        var item = (await Service(new Store(_ => Result(1, Allocated(30)))).ReserveAsync(Actor, new(1, null), Guid.NewGuid(), "c", default)).Items[0];
        Assert.DoesNotContain("secret-", item.Proxy!.ToString()); Assert.DoesNotContain("synthetic", item.Proxy.ToString()); Assert.DoesNotContain("pw", new ConfirmIngestionRequest("k1abc12", "1").ToString());
    }

    [Theory]
    [InlineData(0)] [InlineData(51)] [InlineData(-1)]
    public async Task Rejects_invalid_counts_before_touching_the_store(int count)
    {
        var store = new Store(_ => throw new InvalidOperationException());
        var error = await Assert.ThrowsAsync<AllocationPersistenceException>(() => Service(store).ReserveAsync(Actor, new(count, null), Guid.NewGuid(), "c", default));
        Assert.Equal(AllocationPersistenceFailure.Validation, error.Failure); Assert.Equal(0, store.Reserves);
    }

    [Fact]
    public async Task Rejects_invalid_platform_key_and_actor_without_reserving()
    {
        var store = new Store(_ => throw new InvalidOperationException());
        Assert.Equal(AllocationPersistenceFailure.Validation, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => Service(store).ReserveAsync(Actor, new(1, "INEXISTENTE"), Guid.NewGuid(), "c", default))).Failure);
        Assert.Equal(AllocationPersistenceFailure.Validation, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => Service(store).ReserveAsync(Actor, new(1, null), Guid.Empty, "c", default))).Failure);
        Assert.Equal(AllocationPersistenceFailure.Validation, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => Service(store).ReserveAsync(new(Guid.Empty, "x"), new(1, null), Guid.NewGuid(), "c", default))).Failure);
        Assert.Equal(0, store.Reserves);
    }

    [Fact]
    public async Task Refuses_to_reserve_when_the_encryption_key_is_unavailable_and_fails_safely_on_decrypt_errors()
    {
        var store = new Store(_ => Result(1, Allocated(40)));
        var missingKey = Build(store, () => throw new InvalidOperationException("sem chave"), new Clock());
        Assert.Equal(AllocationPersistenceFailure.Unavailable, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => missingKey.ReserveAsync(Actor, new(1, null), Guid.NewGuid(), "c", default))).Failure);
        Assert.Equal(0, store.Reserves); // nada é reservado se não houver como entregar a conexão
        var broken = Service(new Store(_ => Result(1, Allocated(41))), new Protector(fail: true));
        var error = await Assert.ThrowsAsync<AllocationPersistenceException>(() => broken.ReserveAsync(Actor, new(1, null), Guid.NewGuid(), "c", default));
        Assert.Equal(AllocationPersistenceFailure.Unavailable, error.Failure); Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Confirm_and_release_validate_identifiers_and_trim_values()
    {
        string? seen = null; string? serial = "unset";
        var store = new RecordingStore((id, ads, ser) => { seen = ads; serial = ser; });
        var service = Build(store, () => new Protector(), new Clock());
        await service.ConfirmAsync(Actor, Guid.NewGuid(), new("  k1abc12 ", "  "), "c", default);
        Assert.Equal("k1abc12", seen); Assert.Null(serial);
        await service.ConfirmAsync(Actor, Guid.NewGuid(), new("k1abc12", " 1234 "), "c", default); Assert.Equal("1234", serial);
        await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.ConfirmAsync(Actor, Guid.Empty, new("k1abc12", null), "c", default));
        await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.ReleaseAsync(Actor, Guid.Empty, "c", default));
    }
    private sealed class RecordingStore(Action<Guid, string, string?> onConfirm) : IIngestionStore
    {
        public Task<IngestionCapacity> CapacityAsync(string groupId, int numberFloor, CancellationToken token) => throw new NotSupportedException();
        public Task<IngestionBatchResult> ReserveBatchAsync(IngestionBatchRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task ConfirmAsync(Guid allocationId, Guid actorId, string adsPowerId, string? serialNumber, string correlationId, CancellationToken token) { onConfirm(allocationId, adsPowerId, serialNumber); return Task.CompletedTask; }
        public Task ReleaseAsync(Guid allocationId, Guid actorId, string correlationId, CancellationToken token) => Task.CompletedTask;
        public Task<IngestionBatchRelease> ReleaseBatchAsync(Guid batchId, Guid actorId, string correlationId, CancellationToken token) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Batch_release_forwards_the_batch_and_credential_and_rejects_empty_identifiers()
    {
        var store = new Store(_ => throw new InvalidOperationException()); var service = Service(store); var batch = Guid.NewGuid();
        var result = await service.ReleaseBatchAsync(Actor, batch, "c", default);
        Assert.Equal([(batch, Actor.Id)], store.BatchReleases); Assert.Equal((2, 1, 0), (result.Released, result.Confirmed, result.AlreadyEnded));
        await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.ReleaseBatchAsync(Actor, Guid.Empty, "c", default));
        await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.ReleaseBatchAsync(new(Guid.Empty, "x"), batch, "c", default));
        Assert.Single(store.BatchReleases);
    }

    [Theory]
    [InlineData("Perfil 1250", 1250)] [InlineData("Perfil 1250 (revisar)", 1250)] [InlineData("perfil  77 - Sávio", 77)] [InlineData("Perfil 12/13", 12)]
    public void Leading_parser_reads_the_number_even_with_suffixes(string name, int expected)
    { Assert.True(ProfileNumber.TryParseLeading(name, out var number)); Assert.Equal(expected, number); }

    [Theory]
    [InlineData("Perfil")] [InlineData("Perfil X")] [InlineData("Meu Perfil 12")] [InlineData("Perfil 1234567890")] [InlineData("")] [InlineData(null)]
    public void Leading_parser_ignores_names_without_a_leading_number(string? name) => Assert.False(ProfileNumber.TryParseLeading(name, out _));

    [Fact]
    public async Task Test_mode_numbers_and_creates_only_in_the_test_group_and_production_only_in_the_esteira_group()
    {
        var testReader = new FakeReader("10906211", "FB Teste | YAGO", 1250); var testStore = new Store(request => Result(request.Count, Allocated(1251)));
        var test = await Build(testStore, () => new Protector(), new Clock(), testReader).ReserveAsync(Actor, new(1, null), Guid.NewGuid(), "c", default);
        Assert.Equal(["FB Teste | YAGO"], testReader.Asked); Assert.Equal(("10906211", 1250), (testStore.LastRequest!.GroupId, testStore.LastRequest.NumberFloor));
        Assert.Equal(new IngestionGroup("10906211", "FB Teste | YAGO", "Test"), test.Group);

        var prodReader = new FakeReader("10477650", "03 ESTEIRA DE PERFIL", 1243); var prodStore = new Store(request => Result(request.Count, Allocated(1244)));
        var production = new ProfileIngestionOptions { Mode = "Production" };
        var result = await Build(prodStore, () => new Protector(), new Clock(), prodReader, production).ReserveAsync(Actor, new(1, null), Guid.NewGuid(), "c", default);
        Assert.Equal(["03 ESTEIRA DE PERFIL"], prodReader.Asked); Assert.Equal(("10477650", 1243), (prodStore.LastRequest!.GroupId, prodStore.LastRequest.NumberFloor));
        Assert.Equal("Production", result.Group.Mode); Assert.Equal("10477650", result.Group.Id);
    }

    [Fact]
    public async Task Availability_reads_the_active_group_and_reports_the_next_name()
    {
        var store = new Store(_ => throw new InvalidOperationException()); var reader = new FakeReader("10477650", "03 ESTEIRA DE PERFIL", 1243);
        var availability = await Build(store, () => new Protector(), new Clock(), reader, new ProfileIngestionOptions { Mode = "Production" }).GetAvailabilityAsync(default);
        Assert.Equal([("10477650", 1243)], store.Capacities); Assert.Equal(1244, availability.NextProfileNumber); Assert.Equal("Perfil 1244", availability.NextProfileName);
        Assert.Equal("Production", availability.Group.Mode); Assert.Equal(["03 ESTEIRA DE PERFIL"], reader.Asked);
    }

    [Theory]
    [InlineData(AdsPowerFailure.GroupNotFound)] [InlineData(AdsPowerFailure.Unreachable)] [InlineData(AdsPowerFailure.Unauthorized)]
    public async Task Does_not_reserve_anything_when_the_adspower_group_cannot_be_read(AdsPowerFailure failure)
    {
        var store = new Store(_ => throw new InvalidOperationException()); var reader = new FakeReader { Fail = failure };
        var service = Build(store, () => new Protector(), new Clock(), reader);
        Assert.Equal(failure, (await Assert.ThrowsAsync<AdsPowerException>(() => service.ReserveAsync(Actor, new(1, null), Guid.NewGuid(), "c", default))).Failure);
        Assert.Equal(failure, (await Assert.ThrowsAsync<AdsPowerException>(() => service.GetAvailabilityAsync(default))).Failure);
        Assert.Equal(0, store.Reserves);
    }

    [Fact]
    public void Options_default_to_test_mode_with_distinct_groups_and_reject_invalid_values()
    {
        var defaults = new ProfileIngestionOptions(); defaults.Validate();
        Assert.Equal(("Test", "FB Teste | YAGO"), (defaults.Mode, defaults.ActiveGroupName)); Assert.Equal("03 ESTEIRA DE PERFIL", new ProfileIngestionOptions { Mode = "Production" }.ActiveGroupName);
        Assert.Throws<InvalidOperationException>(() => new ProfileIngestionOptions { Mode = "production-ish" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ProfileIngestionOptions { TestGroupName = " " }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ProfileIngestionOptions { TestGroupName = "03 esteira de perfil" }.Validate()); // teste e produção não podem apontar para o mesmo grupo
    }
}
