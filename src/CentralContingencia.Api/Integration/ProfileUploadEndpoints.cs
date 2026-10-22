using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Ingestion;
using CentralContingencia.Api.Proxies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using CentralContingencia.Application.Proxies.Import;

namespace CentralContingencia.Api.Integration;

/// <summary>Configuração do envio de TXT ao fluxo do n8n. A chave é segredo: vem de variável de ambiente, nunca de arquivo versionado.</summary>
public sealed class N8nIngestionOptions
{
    public string? WebhookUrl { get; set; }
    public string? WebhookKey { get; set; }
    public int MaxFiles { get; set; } = 20;
    public int MaxFileBytes { get; set; } = 512 * 1024;
    public int TimeoutSeconds { get; set; } = 300;
    public bool Configured => !string.IsNullOrWhiteSpace(WebhookUrl) && !string.IsNullOrWhiteSpace(WebhookKey);
    public long MaxBodyBytes => (long)MaxFiles * MaxFileBytes + 64 * 1024;

    public void Validate()
    {
        if (MaxFiles is < 1 or > 50 || MaxFileBytes is < 1024 or > 4 * 1024 * 1024 || TimeoutSeconds is < 10 or > 900)
            throw new InvalidOperationException("N8nIngestion: limites fora do intervalo permitido.");
        if (string.IsNullOrWhiteSpace(WebhookUrl)) return;
        // O n8n roda neste computador (ou no Docker dele). Um endereço externo enviaria credenciais de Facebook para fora.
        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !(uri.IsLoopback || string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("N8nIngestion:WebhookUrl deve apontar para este computador (localhost, 127.0.0.1 ou host.docker.internal), sem usuário na URL.");
    }
}

/// <summary>Apenas um envio por vez: o fluxo reserva proxies e numera perfis, e dois lotes simultâneos disputariam o mesmo grupo.</summary>
public sealed class UploadGate { public SemaphoreSlim Lock { get; } = new(1, 1); }

public enum N8nFailure { NotConfigured, Unreachable, Timeout, Rejected, BadResponse }
public sealed class N8nIngestionException(N8nFailure failure, int? httpStatus = null) : Exception("Falha ao falar com o n8n.") { public N8nFailure Failure { get; } = failure; public int? HttpStatus { get; } = httpStatus; }

public sealed record UploadSummary(int Requested, int Reserved, int Created, int AdsPowerFailures, int UnfilledForLackOfProxy, int FilesForReview);
public sealed record UploadCreated(string Profile, string? AdsPowerId, string? Serial, bool ConfirmedInCentral, string? Warning);
public sealed record UploadFailed(string Profile, string? Reason, string? AdsPowerMessage, bool ProxyReleased);
public sealed record UploadUnfilled(string? File, string? Reason, string? Message);
public sealed record UploadReview(string File, IReadOnlyList<string> Reasons);
/// <summary>Resposta ao navegador: só os campos conhecidos do relatório do n8n, com tamanho limitado. Nada além disso é repassado.</summary>
public sealed record UploadReport(string Title, string Message, string? GroupName, string? Environment, bool Failed, UploadSummary? Summary,
    IReadOnlyList<UploadCreated> Created, IReadOnlyList<UploadFailed> Failures, IReadOnlyList<UploadUnfilled> Unfilled, IReadOnlyList<UploadReview> Review, int? ReservationsUndone);

public static class UploadReportParser
{
    private const int MaxText = 400, MaxItems = 100;
    private static string? Text(JsonElement parent, string name, int max = MaxText) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Truncate(value.GetString(), max) : null;
    private static string? Truncate(string? value, int max)
    {
        if (value is null) return null;
        var clean = new string(value.Where(c => !char.IsControl(c) || c == '\n').ToArray());
        return clean.Length > max ? clean[..max] : clean;
    }
    private static int Number(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static bool Flag(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Take(MaxItems) : [];

    public static UploadReport Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new N8nIngestionException(N8nFailure.BadResponse);
        var title = Text(root, "titulo", 120); var message = Text(root, "mensagem", 1500);
        if (title is null || message is null) throw new N8nIngestionException(N8nFailure.BadResponse);
        var group = root.TryGetProperty("grupo", out var g) ? g : default;
        UploadSummary? summary = root.TryGetProperty("resumo", out var s) && s.ValueKind == JsonValueKind.Object
            ? new(Number(s, "solicitados"), Number(s, "reservados"), Number(s, "criados"), Number(s, "falhasNoAdsPower"), Number(s, "naoPreenchidosPorFaltaDeProxy"), Number(s, "arquivosParaRevisao")) : null;
        int? undone = root.TryGetProperty("reservasDesfeitas", out var u) && u.TryGetInt32(out var count) ? count : null;
        return new(title, message, Text(group, "nome", 200), Text(group, "ambiente", 20), title.StartsWith("Falha", StringComparison.Ordinal) || root.TryGetProperty("liberacaoDoLote", out _), summary,
            Items(root, "criados").Select(i => new UploadCreated(Text(i, "perfil", 100) ?? "?", Text(i, "adsPowerId", 64), Text(i, "serie", 20), Flag(i, "confirmadoNaCentral"), Text(i, "aviso"))).ToList(),
            Items(root, "falhas").Select(i => new UploadFailed(Text(i, "perfil", 100) ?? "?", Text(i, "motivo", 60), Text(i, "mensagemAdsPower", 300), Flag(i, "proxyLiberada"))).ToList(),
            Items(root, "naoPreenchidos").Select(i => new UploadUnfilled(Text(i, "arquivo", 150), Text(i, "motivo", 60), Text(i, "mensagem"))).ToList(),
            Items(root, "revisaoManual").Select(i => new UploadReview(Text(i, "sourceFile", 150) ?? "?",
                i.TryGetProperty("reasons", out var r) && r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Take(10).Select(x => Truncate(x.GetString(), 200)!).ToList() : [])).ToList(),
            undone);
    }
}

public sealed record UploadFile(string Name, byte[] Content);

/// <summary>Entrega os TXT ao webhook do n8n. Os bytes existem só em memória e são zerados ao terminar.</summary>
public sealed class N8nIngestionClient(HttpClient http, IOptions<N8nIngestionOptions> options)
{
    public async Task<UploadReport> SubmitAsync(IReadOnlyList<UploadFile> files, CancellationToken token)
    {
        var settings = options.Value;
        if (!settings.Configured) throw new N8nIngestionException(N8nFailure.NotConfigured);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var form = new MultipartFormDataContent();
        for (var i = 0; i < files.Count; i++)
        {
            var part = new ByteArrayContent(files[i].Content);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            form.Add(part, "file" + i, files[i].Name);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl) { Content = form };
        request.Headers.Add("X-Central-Key", settings.WebhookKey);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new N8nIngestionException(N8nFailure.Rejected, (int)response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (body.Length > 512 * 1024) throw new N8nIngestionException(N8nFailure.BadResponse);
            try { return UploadReportParser.Parse(body); }
            catch (JsonException) { throw new N8nIngestionException(N8nFailure.BadResponse); }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new N8nIngestionException(N8nFailure.Timeout); }
        catch (HttpRequestException) { throw new N8nIngestionException(N8nFailure.Unreachable); }
    }
}

public sealed class UploadEndpointMetadata;

public static class ProfileUploadEndpoints
{
    public const string StatusPath = "/api/v1/profile-ingestions/status";
    public const string SubmitPath = "/api/v1/profile-ingestions/submit";

    public static void ConfigureBodyLimit(HttpContext context)
    {
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = context.RequestServices.GetRequiredService<IOptions<N8nIngestionOptions>>().Value.MaxBodyBytes;
    }

    public static void MapProfileUploadEndpoints(this WebApplication app)
    {
        app.MapGet(StatusPath, async (IProfileIngestionService service, IOptions<N8nIngestionOptions> options, UploadGate gate, CancellationToken token) =>
            await IngestionEndpoints.Execute(async () =>
            {
                var availability = await service.GetAvailabilityAsync(token);
                return Results.Ok(new { configured = options.Value.Configured, inProgress = gate.Lock.CurrentCount == 0, availability.AvailableProxies, availability.NextProfileNumber,
                    availability.NextProfileName, availability.Group, maxFiles = options.Value.MaxFiles, maxFileBytes = options.Value.MaxFileBytes });
            })).WithTags("Ingestão de perfis (tela)").RequireAuthorization(AccessPolicies.Management)
            .WithSummary("Proxies livres, próximo nome de perfil e o grupo (teste ou produção) para a tela de envio de TXT. Sem credenciais.")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(503);

        app.MapPost(SubmitPath, Submit).WithTags("Ingestão de perfis (tela)").WithMetadata(new UploadEndpointMetadata())
            .RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(502).ProducesProblem(503).ProducesProblem(504)
            .WithSummary("Envia TXT (multipart, campo files) ao fluxo do n8n e devolve o relatório. Admin/Manager com CSRF; um envio por vez.")
            .WithDescription("Os arquivos contêm credenciais: passam somente pela memória da Central (nada é gravado em disco, banco ou log) e são zerados ao final. Somente .txt, até MaxFiles arquivos de MaxFileBytes cada.");
    }

    private static async Task<IResult> Submit(HttpContext context, N8nIngestionClient client, IOptions<N8nIngestionOptions> options, UploadGate gate, ILoggerFactory loggers, CancellationToken token)
    {
        var settings = options.Value;
        var logger = loggers.CreateLogger("ProfileUpload");
        if (!settings.Configured) return Results.Problem(statusCode: 503, title: "O envio ao n8n não está configurado neste computador.", extensions: new Dictionary<string, object?> { ["code"] = "NotConfigured" });
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media) || media.MediaType != "multipart/form-data")
            return Results.Problem(statusCode: 415, title: "Envie multipart/form-data com os arquivos no campo files.");
        var boundary = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 128) return Invalid();
        if (context.Request.ContentLength > settings.MaxBodyBytes) return TooLarge();
        if (!await gate.Lock.WaitAsync(0, token)) return Results.Problem(statusCode: 409, title: "Já existe um envio em andamento. Aguarde terminar.");
        var files = new List<UploadFile>();
        var buffer = new byte[8192];
        try
        {
            using var body = new LimitedRequestStream(context.Request.Body, settings.MaxBodyBytes);
            var reader = new MultipartReader(boundary, body) { HeadersCountLimit = 16, HeadersLengthLimit = 4096 };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadNextSectionAsync(token) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) || disposition.DispositionType != "form-data" ||
                    HeaderUtilities.RemoveQuotes(disposition.Name).Value != "files" || !disposition.FileName.HasValue) return Invalid();
                var name = Path.GetFileName(HeaderUtilities.RemoveQuotes(disposition.FileName).Value ?? "");
                if (name.Length is 0 or > 150 || name.Any(char.IsControl) || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    return Results.Problem(statusCode: 400, title: "Somente arquivos .txt com nome de até 150 caracteres.");
                if (!names.Add(name)) return Results.Problem(statusCode: 400, title: "Há arquivos com o mesmo nome. Remova a duplicata.");
                if (files.Count >= settings.MaxFiles) return Results.Problem(statusCode: 413, title: $"No máximo {settings.MaxFiles} arquivos por envio.");
                using var memory = new MemoryStream();
                int read;
                while ((read = await section.Body.ReadAsync(buffer, token)) > 0)
                {
                    if (memory.Length + read > settings.MaxFileBytes) return Results.Problem(statusCode: 413, title: $"Cada arquivo pode ter no máximo {settings.MaxFileBytes / 1024} KB.");
                    await memory.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (memory.Length == 0) return Results.Problem(statusCode: 400, title: $"O arquivo {name} está vazio.");
                var content = memory.ToArray();
                CryptographicOperations.ZeroMemory(memory.GetBuffer());
                files.Add(new(name, content));
            }
            if (files.Count == 0) return Results.Problem(statusCode: 400, title: "Envie ao menos um arquivo .txt no campo files.");
            var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var total = files.Sum(f => f.Content.Length);
            try
            {
                var report = await client.SubmitAsync(files, token);
                // Auditoria sem conteúdo: quem, quando, quantos e o resultado. Nomes de arquivo e credenciais nunca vão para o log.
                logger.LogInformation("Envio de TXT ao n8n: ator {Actor}, arquivos {Files}, bytes {Bytes}, criados {Created}, falhas {Failures}, sem proxy {Unfilled}, revisão {Review}, falhou {Failed}, correlação {Correlation}",
                    actor, files.Count, total, report.Summary?.Created ?? 0, report.Summary?.AdsPowerFailures ?? 0, report.Summary?.UnfilledForLackOfProxy ?? 0, report.Summary?.FilesForReview ?? 0, report.Failed, context.TraceIdentifier);
                return Results.Ok(report);
            }
            catch (N8nIngestionException error)
            {
                logger.LogWarning("Envio de TXT ao n8n falhou: ator {Actor}, arquivos {Files}, motivo {Failure}, status do n8n {HttpStatus}, correlação {Correlation}", actor, files.Count, error.Failure, error.HttpStatus, context.TraceIdentifier);
                var (status, title) = error.Failure switch
                {
                    N8nFailure.NotConfigured => (503, "O envio ao n8n não está configurado neste computador."),
                    N8nFailure.Unreachable => (503, "Não foi possível falar com o n8n. Confira se ele está aberto e se o fluxo está ativo."),
                    N8nFailure.Timeout => (504, "O n8n demorou demais para responder. Confira o resultado no AdsPower e em Proxies antes de reenviar; reservas não usadas voltam sozinhas em até 15 minutos."),
                    N8nFailure.Rejected => (502, error.HttpStatus switch { 401 or 403 => "O n8n recusou a chave do webhook (HTTP " + error.HttpStatus + "). A chave da credencial do n8n não confere com a da Central.", 404 => "O n8n não encontrou o webhook (HTTP 404). Publique o fluxo do site e confira o endereço.", int code => "O fluxo do n8n falhou ao processar o envio (HTTP " + code + "). Veja a execução no n8n.", _ => "O n8n recusou o envio." }),
                    _ => (502, "O n8n devolveu uma resposta inesperada. Confira o fluxo antes de reenviar."),
                };
                return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = error.Failure.ToString() });
            }
        }
        catch (CsvPreviewException) { return TooLarge(); }
        catch (BadHttpRequestException error) when (error.StatusCode == 413) { return TooLarge(); }
        catch (InvalidDataException) { return Invalid(); }
        catch (IOException) { return Results.Problem(statusCode: 400, title: "Upload incompleto ou multipart inválido."); }
        finally
        {
            foreach (var file in files) CryptographicOperations.ZeroMemory(file.Content);
            CryptographicOperations.ZeroMemory(buffer);
            gate.Lock.Release();
        }
    }

    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "Envie apenas arquivos .txt no campo files.");
    private static IResult TooLarge() => Results.Problem(statusCode: 413, title: "O envio excede o tamanho máximo permitido.");
}
