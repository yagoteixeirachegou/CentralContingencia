using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Infrastructure.Identity;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class AuthenticationTests(AuthFixture fixture)
{
    [PostgresFact]
    public async Task ProvisioningCommandCreatesFirstAdminAndRefusesSecondRun()
    {
        var isolated = new AuthFixture { ProvisionAdmin = false };
        await isolated.InitializeAsync();
        try
        {
            async Task<(int code, string output)> RunCommand()
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root is not null && !File.Exists(Path.Combine(root.FullName, "CentralContingencia.slnx"))) root = root.Parent;
                Assert.NotNull(root);
                var start = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = root.FullName
                };
                start.ArgumentList.Add(Path.Combine(root.FullName, "src", "CentralContingencia.Api", "bin", "Debug", "net10.0", "CentralContingencia.Api.dll"));
                start.ArgumentList.Add("--provision-admin");
                // Sem ASPNETCORE_ENVIRONMENT o host assume Production: o comando roda com a mesma configuração mínima exigida na implantação.
                foreach (var (key, value) in DeploymentSettings.Production(isolated.ConnectionString)) start.Environment[key.Replace(":", "__")] = value;
                start.Environment["CENTRAL_ADMIN_EMAIL"] = isolated.AdminEmail;
                start.Environment["CENTRAL_ADMIN_PASSWORD"] = isolated.Password;
                using var process = System.Diagnostics.Process.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
                var output = await stdout + await stderr;
                Assert.DoesNotContain(isolated.Password, output);
                return (process.ExitCode, output);
            }
            Assert.Equal(0, (await RunCommand()).code);
            Assert.Equal(1, (await RunCommand()).code);
            using var scope = isolated.Factory.Services.CreateScope();
            Assert.Single(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetUsersInRoleAsync("Admin"));
        }
        finally { await isolated.DisposeAsync(); }
    }
    [PostgresFact]
    public async Task GlobalRateLimitAppliesToAllRequests()
    {
        await using var limited = new AuthFactory(fixture.ConnectionString, 100, null, 3);
        using var client = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/health")).StatusCode);
    }
    [PostgresFact]
    public async Task InsecureAuthRequestsAreRejectedBeforeAnyLogin()
    {
        using var client = fixture.Factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/auth/csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = fixture.AdminEmail, password = fixture.Password })).StatusCode);
    }
    [PostgresFact]
    public async Task SessionExpiresAfterThirtyMinutesEvenWhenUsed()
    {
        var clock = new TestClock();
        await using var factory = new AuthFactory(fixture.ConnectionString, 100, clock);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await Login(client);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        clock.Advance(TimeSpan.FromMinutes(21));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [PostgresFact]
    public async Task AnonymousCsrfTokenCannotBeReusedAfterLogin()
    {
        using var client = fixture.Client();
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        await Login(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [PostgresFact]
    public async Task TamperedSessionCookieIsRejected()
    {
        using var client = fixture.Client();
        var response = await Post(client, "/auth/login", new { email = fixture.AdminEmail, password = fixture.Password });
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Central.Session=")).Split(';')[0];
        var split = cookie.IndexOf('=') + 1;
        cookie = cookie[..split] + (cookie[split] == 'A' ? 'B' : 'A') + cookie[(split + 1)..];
        using var other = fixture.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        other.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [PostgresFact]
    public async Task ConcurrentFailuresCannotBypassLockout()
    {
        var email = await CreateUser(AccessRoles.Operator);
        var clients = Enumerable.Range(0, 5).Select(_ => fixture.Client()).ToArray();
        try
        {
            var responses = await Task.WhenAll(clients.Select(client =>
                Post(client, "/auth/login", new { email, password = "Incorrect!123" })));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
            using var final = fixture.Client();
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(final, "/auth/login", new { email, password = fixture.Password })).StatusCode);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
    [PostgresTheory]
    [InlineData("Admin", "Administration", true)]
    [InlineData("Manager", "Administration", false)]
    [InlineData("Operator", "Administration", false)]
    [InlineData("Admin", "Management", true)]
    [InlineData("Manager", "Management", true)]
    [InlineData("Operator", "Management", false)]
    [InlineData("Admin", "Operations", true)]
    [InlineData("Manager", "Operations", true)]
    [InlineData("Operator", "Operations", true)]
    public async Task PoliciesEnforceExplicitRoleMatrix(string role, string policy, bool expected)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)], "test"));
        Assert.Equal(expected, (await auth.AuthorizeAsync(principal, null, policy)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(new System.Security.Claims.ClaimsPrincipal(), null, policy)).Succeeded);
    }
    [PostgresFact]
    public async Task MigrationPreservesExistingTablesAndIsIdempotent()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        await db.Database.MigrateAsync();
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT value FROM public.foundation_sentinel", connection);
        Assert.Equal("synthetic-preserved", await command.ExecuteScalarAsync());
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1" + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private async Task Login(HttpClient client, string? email = null)
    {
        var response = await Post(client, "/auth/login", new { email = email ?? fixture.AdminEmail, password = fixture.Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private async Task<string> CreateUser(string role)
    {
        using var admin = fixture.Client();
        await Login(admin);
        var email = $"synthetic-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Created, (await Post(admin, "/users", new { email, password = fixture.Password, role })).StatusCode);
        return email;
    }
    [PostgresFact]
    public async Task ValidLoginHasSecureCookieAndAuthenticatedUser()
    {
        using var client = fixture.Client();
        var response = await Post(client, "/auth/login", new { email = fixture.AdminEmail, password = fixture.Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Central.Session="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        var me = await client.GetFromJsonAsync<AuthenticatedUser>("/api/v1/auth/me");
        Assert.Contains(AccessRoles.Admin, me!.Roles);
        Assert.DoesNotContain("password", (await client.GetStringAsync("/api/v1/auth/me")).ToLowerInvariant());
    }
    [PostgresFact]
    public async Task InvalidAndUnknownCredentialsHaveSameResponse()
    {
        using var client = fixture.Client();
        var existing = await CreateUser(AccessRoles.Operator);
        var first = await Post(client, "/auth/login", new { email = existing, password = "Incorrect!123" });
        var second = await Post(client, "/auth/login", new { email = "absent@example.test", password = "Incorrect!123" });
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<JsonElement>();
        var b = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [PostgresFact]
    public async Task FiveFailuresLockAccountEvenWithCorrectPassword()
    {
        var email = await CreateUser(AccessRoles.Operator);
        using var client = fixture.Client();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/auth/login", new { email, password = "Incorrect!123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/auth/login", new { email, password = fixture.Password })).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.True(await manager.IsLockedOutAsync(user!));
    }
    [PostgresFact]
    public async Task AnonymousCannotCreateUsersAndThereIsNoRegistration()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/users", new { email = "example@example.test", password = fixture.Password, role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/auth/register", new { })).StatusCode);
    }
    [PostgresFact]
    public async Task AdminCanCreateUsersButInvalidRolesAndDuplicatesAreRejected()
    {
        using var client = fixture.Client(); await Login(client);
        var email = $"new-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Created, (await Post(client, "/users", new { email, password = fixture.Password, role = "Manager" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/users", new { email, password = fixture.Password, role = "Manager" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/users", new { email = "role@example.test", password = fixture.Password, role = "Integration" })).StatusCode);
    }
    [PostgresTheory]
    [InlineData("Manager")]
    [InlineData("Operator")]
    public async Task NonAdminsCannotCreateUsers(string role)
    {
        var email = await CreateUser(role);
        using var client = fixture.Client(); await Login(client, email);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/users", new { email = "denied@example.test", password = fixture.Password, role = "Operator" })).StatusCode);
    }
    [PostgresFact]
    public async Task LoginRequiresCsrfAndRejectsMismatchedCookieToken()
    {
        using var first = fixture.Client(); using var second = fixture.Client();
        var body = new { email = fixture.AdminEmail, password = fixture.Password };
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/v1/auth/login", body)).StatusCode);
        var token = (await first.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        await second.GetAsync("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await second.SendAsync(request)).StatusCode);
    }
    [PostgresTheory]
    [InlineData("/auth/logout")]
    [InlineData("/auth/change-password")]
    [InlineData("/users")]
    public async Task AuthenticatedMutationsRequireCsrf(string path)
    {
        using var client = fixture.Client(); await Login(client);
        var response = await client.PostAsJsonAsync("/api/v1" + path, new {
            currentPassword = fixture.Password, newPassword = fixture.Password + "new",
            email = "csrf@example.test", password = fixture.Password, role = "Operator"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [PostgresFact]
    public async Task LogoutRevokesCurrentAndPreviouslyCopiedCookies()
    {
        var email = await CreateUser(AccessRoles.Operator);
        using var client = fixture.Client(); using var another = fixture.Client();
        await Login(client, email); await Login(another, email);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await another.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [PostgresFact]
    public async Task PasswordChangeRequiresCurrentPasswordAndRevokesSessions()
    {
        var email = await CreateUser(AccessRoles.Operator);
        using var client = fixture.Client(); using var another = fixture.Client();
        await Login(client, email); await Login(another, email);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/auth/change-password", new { currentPassword = "wrong", newPassword = fixture.Password + "new" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "/auth/change-password", new { currentPassword = fixture.Password, newPassword = fixture.Password + "new" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await another.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/auth/login", new { email, password = fixture.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "/auth/login", new { email, password = fixture.Password + "new" })).StatusCode);
    }
    [PostgresFact]
    public async Task WeakPasswordAndInvalidEmailAreRejected()
    {
        using var client = fixture.Client(); await Login(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/users", new { email = "not-email", password = "short", role = "Operator" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/users", new { email = "weak@example.test", password = "abcdefghijkl", role = "Operator" })).StatusCode);
    }
    [PostgresFact]
    public async Task ProvisioningCannotOverwriteExistingAdministrator()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAdminProvisioner>().ProvisionAsync("second@example.test", fixture.Password));
    }
    [PostgresFact]
    public async Task RateLimitRejectsExcessiveAttempts()
    {
        await using var limited = new AuthFactory(fixture.ConnectionString, 2);
        using var client = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/auth/login", new { email = "absent@example.test", password = "Incorrect!123" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(client, "/auth/login", new { email = "absent@example.test", password = "Incorrect!123" })).StatusCode);
    }
    [PostgresFact]
    public async Task AuditContainsOnlyDefinedSanitizedFields()
    {
        using var client = fixture.Client(); await Login(client);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var events = await db.AuthenticationAuditEvents.ToListAsync();
        Assert.Contains(events, e => e.Action == AuthenticationAction.LoginSucceeded);
        Assert.Contains(events, e => e.Action == AuthenticationAction.AdminProvisioned);
        var json = JsonSerializer.Serialize(events);
        Assert.DoesNotContain(fixture.Password, json);
        Assert.DoesNotContain(fixture.AdminEmail, json);
        Assert.All(events, e => Assert.Equal(TimeSpan.Zero, e.OccurredAt.Offset));
    }
    [PostgresFact]
    public async Task ConcurrentBootstrapCreatesExactlyOneAdministrator()
    {
        var isolated = new AuthFixture { ProvisionAdmin = false };
        await isolated.InitializeAsync();
        try
        {
            async Task<bool> Provision(string email)
            {
                using var scope = isolated.Factory.Services.CreateScope();
                try { await scope.ServiceProvider.GetRequiredService<IAdminProvisioner>().ProvisionAsync(email, isolated.Password); return true; }
                catch (InvalidOperationException) { return false; }
            }
            var results = await Task.WhenAll(Provision("first@example.test"), Provision("second@example.test"));
            Assert.Equal(1, results.Count(r => r));
            using var scope = isolated.Factory.Services.CreateScope();
            Assert.Single(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetUsersInRoleAsync("Admin"));
        }
        finally { await isolated.DisposeAsync(); }
    }
}
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset current = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan duration) => current += duration;
}
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CENTRAL_TEST_POSTGRES")))
            Skip = "Configure CENTRAL_TEST_POSTGRES para testar PostgreSQL local real.";
    }
}
