using System.Security.Cryptography;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class ProxyPersistenceTests(AuthFixture fixture)
{
    private CentralContingenciaDbContext Context() => new(new DbContextOptionsBuilder<CentralContingenciaDbContext>()
        .UseNpgsql(fixture.ConnectionString).Options);
    private static IConfiguration Config(string active, params (string id, byte[] key)[] keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys.ToDictionary(k => $"ProxyEncryption:Keys:{k.id}",
            k => (string?)Convert.ToBase64String(k.key)).Append(new("ProxyEncryption:ActiveKeyId", active))).Build();
    private static Proxy Create(AesGcmProxyPasswordProtector protector)
    {
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        return Proxy.Create(id, ProxyHost.Create("proxy.example.test"), ProxyPort.Create(8080), "synthetic-user",
            protector.Encrypt(id, "synthetic-proxy-secret"), "BR", now.AddDays(1), now);
    }

    [PostgresFact]
    public async Task RoundTripPersistsOnlyCiphertextAndAllowsStatusAndKeyRotation()
    {
        var key = RandomNumberGenerator.GetBytes(32); var nextKey = RandomNumberGenerator.GetBytes(32);
        using var protector = new AesGcmProxyPasswordProtector(Config("test-v1", ("test-v1", key)));
        using var next = new AesGcmProxyPasswordProtector(Config("test-v2", ("test-v1", key), ("test-v2", nextKey)));
        var original = Create(protector);
        await using (var context = Context()) { context.Proxies.Add(original); await context.SaveChangesAsync(); }
        await using (var context = Context())
        {
            var loaded = await context.Proxies.SingleAsync(p => p.Id == original.Id);
            Assert.Equal(original.Host, loaded.Host); Assert.Equal(original.Port, loaded.Port);
            Assert.Equal(original.Username, loaded.Username); Assert.Equal(original.Region, loaded.Region);
            Assert.Equal(original.Password, loaded.Password);
            Assert.Equal(TimeSpan.Zero, loaded.ExpiresAt.Offset);
            Assert.InRange((loaded.CreatedAt - original.CreatedAt).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(10));
            Assert.InRange((loaded.ExpiresAt - original.ExpiresAt).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(10));
            Assert.Equal("synthetic-proxy-secret", protector.Decrypt(loaded.Id, loaded.Password));
            loaded.ChangeStatus(ProxyStatus.Active, DateTimeOffset.UtcNow);
            loaded.ReplaceEncryptedPassword(next.Encrypt(loaded.Id, next.Decrypt(loaded.Id, loaded.Password)), DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        await using (var context = Context())
        {
            var loaded = await context.Proxies.AsNoTracking().SingleAsync(p => p.Id == original.Id);
            Assert.Equal("test-v2", loaded.Password.KeyId);
            Assert.True(loaded.IsAvailable(DateTimeOffset.UtcNow, false));
            Assert.Equal("synthetic-proxy-secret", next.Decrypt(loaded.Id, loaded.Password));
        }
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT row_to_json(p)::text FROM proxies.\"Proxies\" p WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", original.Id);
        var stored = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain("synthetic-proxy-secret", stored);
        Assert.DoesNotContain(Convert.ToBase64String(key), stored);
        Assert.DoesNotContain(Convert.ToBase64String(nextKey), stored);
    }

    [PostgresFact]
    public async Task ReusingPersistedNonceWithSameKeyVersionIsRejected()
    {
        using var protector = new AesGcmProxyPasswordProtector(Config("test-v1", ("test-v1", RandomNumberGenerator.GetBytes(32))));
        var first = Create(protector);
        await using (var context = Context()) { context.Add(first); await context.SaveChangesAsync(); }
        var second = Proxy.Create(Guid.NewGuid(), first.Host, first.Port, first.Username, first.Password, "BR", first.ExpiresAt, DateTimeOffset.UtcNow);
        await using var duplicate = Context(); duplicate.Add(second);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("UX_Proxies_PasswordKeyId_Nonce", postgres.ConstraintName);
    }

    [PostgresFact]
    public async Task TamperedPersistedCiphertextAndSwappedEnvelopeFailAuthentication()
    {
        using var protector = new AesGcmProxyPasswordProtector(Config("test-v1", ("test-v1", RandomNumberGenerator.GetBytes(32))));
        var first = Create(protector); var second = Create(protector);
        await using (var context = Context()) { context.AddRange(first, second); await context.SaveChangesAsync(); }
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var tamper = new NpgsqlCommand("UPDATE proxies.\"Proxies\" SET \"PasswordCiphertext\" = set_byte(\"PasswordCiphertext\", 0, get_byte(\"PasswordCiphertext\", 0) # 1) WHERE \"Id\" = @id", connection);
        tamper.Parameters.AddWithValue("id", first.Id); await tamper.ExecuteNonQueryAsync();
        await using var contextRead = Context();
        var loaded = await contextRead.Proxies.SingleAsync(p => p.Id == first.Id);
        Assert.Throws<CryptographicException>(() => protector.Decrypt(loaded.Id, loaded.Password));
        var other = await contextRead.Proxies.SingleAsync(p => p.Id == second.Id);
        Assert.Throws<CryptographicException>(() => protector.Decrypt(first.Id, other.Password));
    }

    [PostgresTheory]
    [InlineData("\"Port\" = 0", "CK_Proxies_Port")]
    [InlineData("\"Status\" = 'Available'", "CK_Proxies_Status")]
    [InlineData("\"Host\" = ''", "CK_Proxies_Host")]
    [InlineData("\"Username\" = ''", "CK_Proxies_Username")]
    [InlineData("\"Region\" = ''", "CK_Proxies_Region")]
    [InlineData("\"PasswordNonce\" = decode('00', 'hex')", "CK_Proxies_Password")]
    [InlineData("\"PasswordTag\" = decode('00', 'hex')", "CK_Proxies_Password")]
    [InlineData("\"PasswordCiphertext\" = decode('', 'hex')", "CK_Proxies_Password")]
    [InlineData("\"PasswordKeyId\" = 'bad key'", "CK_Proxies_Password")]
    [InlineData("\"UpdatedAt\" = \"CreatedAt\" - interval '1 day'", "CK_Proxies_Timestamps")]
    [InlineData("\"ExpiresAt\" = 'infinity'::timestamptz", "CK_Proxies_Timestamps")]
    [InlineData("\"Id\" = '00000000-0000-0000-0000-000000000000'::uuid", "CK_Proxies_Id")]
    public async Task DatabaseRejectsInvalidDirectWrites(string assignment, string constraint)
    {
        using var protector = new AesGcmProxyPasswordProtector(Config("test-v1", ("test-v1", RandomNumberGenerator.GetBytes(32))));
        var proxy = Create(protector);
        await using (var context = Context()) { context.Add(proxy); await context.SaveChangesAsync(); }
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        // assignment provém exclusivamente dos casos constantes acima, nunca de entrada da aplicação.
        await using var command = new NpgsqlCommand($"UPDATE proxies.\"Proxies\" SET {assignment} WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", proxy.Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    [PostgresFact]
    public void MissingProxyKeyDoesNotPreventExistingAuthenticationServices()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CentralContingencia.Application.Identity.IAuthenticationService>());
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>());
    }

    [PostgresFact]
    public async Task UpgradeFromHomologatedSchemaPreservesIdentityAuditAndExistingTables()
    {
        var isolated = new AuthFixture { InitialMigration = "20261011015548_InitialIdentityAndAuthenticationAudit" };
        await isolated.InitializeAsync();
        try
        {
            using var scope = isolated.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            var users = await context.Users.AsNoTracking().ToListAsync();
            var audits = await context.AuthenticationAuditEvents.AsNoTracking().ToListAsync();
            Assert.Contains("20261011024141_AddProxyPersistence", await context.Database.GetPendingMigrationsAsync());
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(users.Select(u => (u.Id, u.PasswordHash, u.SecurityStamp)),
                (await context.Users.AsNoTracking().ToListAsync()).Select(u => (u.Id, u.PasswordHash, u.SecurityStamp)));
            Assert.Equal(audits.Select(a => a.Id), (await context.AuthenticationAuditEvents.AsNoTracking().ToListAsync()).Select(a => a.Id));
            Assert.Equal(3, await context.Roles.CountAsync());
            await using var connection = new NpgsqlConnection(isolated.ConnectionString); await connection.OpenAsync();
            await using var sentinel = new NpgsqlCommand("SELECT value FROM public.foundation_sentinel", connection);
            Assert.Equal("synthetic-preserved", await sentinel.ExecuteScalarAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }
}
