using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CentralContingencia.Api.Integration;
using CentralContingencia.Application.Ingestion;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class ProfileUploadEndpointTests(AuthFixture fixture) : IDisposable
{
    private const string Submit = "/api/v1/profile-ingestions/submit", Status = "/api/v1/profile-ingestions/status";
    private const string Secret = "senha-sintetica-do-facebook";
    private readonly AuthFactory baseFactory = new(fixture.ConnectionString, permitLimit: 1000, globalPermitLimit: 10000);
    private readonly List<WebApplicationFactory<Program>> factories = [];
    public void Dispose() { foreach (var factory in factories) factory.Dispose(); baseFactory.Dispose(); }

    private sealed class FakeIngestion : IProfileIngestionService
    {
        public Task<IngestionAvailability> GetAvailabilityAsync(CancellationToken token) =>
            Task.FromResult(new IngestionAvailability(7, 1255, "Perfil 1255", new("10906211", "FB Teste | YAGO", "Test")));
        public Task<IngestionReservation> ReserveAsync(IngestionActor actor, IngestionReserveRequest request, Guid idempotencyKey, string correlationId, CancellationToken token) => throw new NotSupportedException();
        public Task ConfirmAsync(IngestionActor actor, Guid allocationId, ConfirmIngestionRequest request, string correlationId, CancellationToken token) => throw new NotSupportedException();
        public Task ReleaseAsync(IngestionActor actor, Guid allocationId, string correlationId, CancellationToken token) => throw new NotSupportedException();
        public Task<IngestionBatchRelease> ReleaseBatchAsync(IngestionActor actor, Guid batchId, string correlationId, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class FakeN8n(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(string Key, List<(string Field, string File, string Body)> Parts)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var parts = new List<(string, string, string)>();
            if (request.Content is MultipartFormDataContent form)
                foreach (var part in form) parts.Add((part.Headers.ContentDisposition!.Name!.Trim('"'), part.Headers.ContentDisposition.FileName!.Trim('"'), await part.ReadAsStringAsync(token)));
            Calls.Add((request.Headers.GetValues("X-Central-Key").Single(), parts));
            return await respond(request);
        }
    }
    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private const string Report = """
        {"titulo":"Concluído","mensagem":"Criados 2 de 2 perfil(is).","grupo":{"nome":"FB Teste | YAGO","ambiente":"Test"},
         "resumo":{"solicitados":2,"reservados":2,"criados":2,"falhasNoAdsPower":0,"naoPreenchidosPorFaltaDeProxy":0,"arquivosParaRevisao":0},
         "criados":[{"perfil":"Perfil 1255","adsPowerId":"k1abc12","serie":"55","confirmadoNaCentral":true,"aviso":null},{"perfil":"Perfil 1256","adsPowerId":"k1abc13","serie":"56","confirmadoNaCentral":true,"aviso":null}],
         "falhas":[],"naoPreenchidos":[],"revisaoManual":[],"intruso":"não deve passar","proxy":{"password":"vazou"}}
        """;

    private HttpClient Client(FakeN8n? n8n, bool configured = true)
    {
        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProfileIngestionService, FakeIngestion>();
            services.PostConfigure<N8nIngestionOptions>(o => { o.WebhookUrl = configured ? "http://localhost:5678/webhook/p6-teste" : null; o.WebhookKey = configured ? "chave-sintetica" : null; o.MaxFiles = 3; o.MaxFileBytes = 2048; o.TimeoutSeconds = 30; });
            if (n8n is not null) services.AddHttpClient<N8nIngestionClient>().ConfigurePrimaryHttpMessageHandler(() => n8n);
        })); factories.Add(factory);
        return factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }
    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }
    private async Task Login(HttpClient client, FakeN8n? n8n, string role, bool configured = true)
    {
        var email = fixture.AdminEmail;
        if (role != "Admin")
        {
            using var admin = Client(n8n, configured); await Login(admin, n8n, "Admin", configured);
            email = $"upload-{Guid.NewGuid():N}@example.test";
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/users", new { email, password = fixture.Password, role })).StatusCode);
        }
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = fixture.Password })).StatusCode);
        await Csrf(client);
    }
    private static MultipartFormDataContent Files(params (string Name, string Body)[] files)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, body) in files) content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(body)), "files", name);
        return content;
    }
    private static FakeN8n Ok() => new(_ => Task.FromResult(Json(Report)));

    [PostgresFact]
    public async Task Anonymous_and_operator_are_rejected_and_nothing_is_forwarded()
    {
        var n8n = Ok();
        using var anonymous = Client(n8n); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Status)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(Submit, Files(("a.txt", "x")))).StatusCode);
        using var operatorClient = Client(n8n); await Login(operatorClient, n8n, "Operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(Status)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync(Submit, Files(("a.txt", "x")))).StatusCode);
        Assert.Empty(n8n.Calls);
    }

    [PostgresTheory]
    [InlineData("Admin")] [InlineData("Manager")]
    public async Task Management_reads_the_status_without_credentials(string role)
    {
        using var client = Client(Ok()); await Login(client, null, role);
        var response = await client.GetAsync(Status); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("configured").GetBoolean()); Assert.False(body.GetProperty("inProgress").GetBoolean());
        Assert.Equal(7, body.GetProperty("availableProxies").GetInt32()); Assert.Equal("Perfil 1255", body.GetProperty("nextProfileName").GetString());
        Assert.Equal("Test", body.GetProperty("group").GetProperty("mode").GetString()); Assert.Equal(3, body.GetProperty("maxFiles").GetInt32());
    }

    [PostgresFact]
    public async Task Files_reach_n8n_with_the_key_and_only_known_report_fields_come_back()
    {
        var n8n = Ok(); using var client = Client(n8n); await Login(client, n8n, "Manager");
        var response = await client.PostAsync(Submit, Files(("a.txt", "Login: " + Secret), ("b.TXT", "outro")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("intruso", text); Assert.DoesNotContain("vazou", text); Assert.DoesNotContain(Secret, text);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("Concluído", body.GetProperty("title").GetString()); Assert.Equal(2, body.GetProperty("summary").GetProperty("created").GetInt32());
        Assert.Equal("Perfil 1256", body.GetProperty("created")[1].GetProperty("profile").GetString()); Assert.False(body.GetProperty("failed").GetBoolean());
        var call = Assert.Single(n8n.Calls);
        Assert.Equal("chave-sintetica", call.Key);
        Assert.Equal([("file0", "a.txt"), ("file1", "b.TXT")], call.Parts.Select(p => (p.Field, p.File)));
        Assert.Equal("Login: " + Secret, call.Parts[0].Body);
    }

    [PostgresFact]
    public async Task Failure_report_from_the_flow_is_marked_as_failed()
    {
        var n8n = new FakeN8n(_ => Task.FromResult(Json("""{"titulo":"Falha — nenhum perfil criado","mensagem":"Nenhum perfil foi criado: x Reservas desfeitas: 2","liberacaoDoLote":"OK","reservasDesfeitas":2}""")));
        using var client = Client(n8n); await Login(client, n8n, "Admin");
        var body = await (await client.PostAsync(Submit, Files(("a.txt", "x")))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("failed").GetBoolean()); Assert.Equal(2, body.GetProperty("reservationsUndone").GetInt32()); Assert.Empty(body.GetProperty("created").EnumerateArray());
    }

    [PostgresTheory]
    [InlineData("a.csv", "x", 400)]
    [InlineData("../a.exe", "x", 400)]
    [InlineData("a.txt", "", 400)]
    [InlineData("a.txt", "TOOBIG", 413)]
    public async Task Invalid_files_are_rejected_before_reaching_n8n(string name, string body, int status)
    {
        var n8n = Ok(); using var client = Client(n8n); await Login(client, n8n, "Admin");
        var content = body == "TOOBIG" ? new string('x', 4096) : body;
        Assert.Equal(status, (int)(await client.PostAsync(Submit, Files((name, content)))).StatusCode);
        Assert.Empty(n8n.Calls);
    }

    [PostgresFact]
    public async Task Duplicate_names_too_many_files_and_missing_csrf_are_rejected()
    {
        var n8n = Ok(); using var client = Client(n8n); await Login(client, n8n, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Submit, Files(("a.txt", "x"), ("A.txt", "y")))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync(Submit, Files(("a.txt", "x"), ("b.txt", "x"), ("c.txt", "x"), ("d.txt", "x")))).StatusCode);
        var wrongField = new MultipartFormDataContent(); wrongField.Add(new ByteArrayContent([1]), "outro", "a.txt");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Submit, wrongField)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Submit, Files(("a.txt", "x")))).StatusCode);
        Assert.Empty(n8n.Calls);
    }

    [PostgresFact]
    public async Task Missing_configuration_is_a_sanitized_503()
    {
        using var client = Client(null, configured: false); await Login(client, null, "Admin", configured: false);
        var response = await client.PostAsync(Submit, Files(("a.txt", "x"))); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>(Status)).GetProperty("configured").GetBoolean());
    }

    [PostgresTheory]
    [InlineData("down", 503)] [InlineData("500", 502)] [InlineData("garbage", 502)] [InlineData("timeout", 504)]
    public async Task N8n_failures_become_sanitized_problems_without_the_file_content(string mode, int status)
    {
        var n8n = new FakeN8n(_ => mode switch
        {
            "down" => throw new HttpRequestException("connection refused " + Secret),
            "500" => Task.FromResult(Json("{}", HttpStatusCode.InternalServerError)),
            "timeout" => throw new TaskCanceledException("timeout " + Secret),
            _ => Task.FromResult(Json("não é json " + Secret)),
        });
        using var client = Client(n8n); await Login(client, n8n, "Admin");
        var response = await client.PostAsync(Submit, Files(("a.txt", Secret))); Assert.Equal(status, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(Secret, text);
    }

    [PostgresFact]
    public async Task Only_one_submission_runs_at_a_time()
    {
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var n8n = new FakeN8n(async _ => { entered.TrySetResult(); await release.Task; return Json(Report); });
        using var first = Client(n8n); await Login(first, n8n, "Admin");
        var running = first.PostAsync(Submit, Files(("a.txt", "x")));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True((await first.GetFromJsonAsync<JsonElement>(Status)).GetProperty("inProgress").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsync(Submit, Files(("b.txt", "y")))).StatusCode);
        release.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await running).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsync(Submit, Files(("c.txt", "z")))).StatusCode); // liberou o lock
        Assert.Equal(2, n8n.Calls.Count);
    }
}
