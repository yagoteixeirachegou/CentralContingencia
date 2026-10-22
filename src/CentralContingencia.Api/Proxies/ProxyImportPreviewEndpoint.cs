using System.Security.Claims;
using System.Security.Cryptography;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Application.Profiles.Recovery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace CentralContingencia.Api.Proxies;

public static class ProxyImportPreviewEndpoint
{
    public const string Path = "/api/v1/proxies/import/preview";
    public const string CommitPath = "/api/v1/proxies/import/commit";
    public const string RecoveryPreviewPath = "/api/v1/profiles/recovery/preview";
    public const string RecoveryCommitPath = "/api/v1/profiles/recovery/commit";
    public static void ConfigureBodyLimit(HttpContext context)
    {
        var limits = context.RequestServices.GetRequiredService<CsvPreviewLimits>();
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limits.MaxFileBytes + 16 * 1024;
    }
    public static void MapProxyImportPreview(this WebApplication app)
    {
        foreach (var path in new[] { RecoveryPreviewPath, RecoveryCommitPath })
        {
            var endpoint = app.MapPost(path, Handle).WithTags("Profiles recovery")
                .WithMetadata(new CsvPreviewEndpointMetadata(), new ProfileRecoveryEndpointMetadata())
                .RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive")
                .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409)
                .ProducesProblem(413).ProducesProblem(415).ProducesProblem(422).ProducesProblem(429).ProducesProblem(500)
                .WithDescription("Admin e CSRF. Multipart file UTF-8 limitado; skipRowsWithoutConnection opcional. Sem alteração de proxies ou credenciais. Reenvie arquivo e opções com X-Import-Confirmation e Idempotency-Key UUID para confirmar atomicamente; mudanças no plano exigem nova prévia.");
            if (path == RecoveryPreviewPath) endpoint.Produces<RecoveryPreview>().WithSummary("Prévia paginada da recuperação de perfis/atribuições, sem gravação.");
            else endpoint.WithMetadata(new CsvCommitEndpointMetadata()).Produces<RecoveryReport>().WithSummary("Confirma recuperação previamente aprovada, revalidada, idempotente e auditada.");
        }
        app.MapPost(Path, Handle).WithTags("Proxies")
            .WithMetadata(new CsvPreviewEndpointMetadata())
            .RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive")
            .Produces<CsvPreviewResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(413).ProducesProblem(415).ProducesProblem(429)
            .WithSummary("Pré-visualiza CSV UTF-8, somente Admin; exige CSRF. Não persiste arquivo, proxies ou credenciais.")
            .WithDescription("Envie exatamente um arquivo no campo file. Query page=1..10000, pageSize=1..100 (padrão 20). Reenvie o arquivo para cada página; resumo considera o arquivo inteiro. Datas sem hora usam meia-noite UTC. Limites em ProxyImportPreview. Credenciais e metadados textuais sempre mascarados.");
        app.MapPost(CommitPath, Handle).WithTags("Proxies")
            .WithMetadata(new CsvPreviewEndpointMetadata(), new CsvCommitEndpointMetadata())
            .RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive")
            .Produces<CsvImportReport>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(422).ProducesProblem(429).ProducesProblem(503)
            .WithSummary("Confirma CSV reenviado com revalidação, idempotência e transação; somente Admin com CSRF.")
            .WithDescription("Exige X-Import-Confirmation da prévia e Idempotency-Key UUID não vazio. Arquivo, defaultRegion, skipRowsWithoutConnection e referenceYear devem coincidir com a prévia. dd/MM exige referenceYear explícito; datas completas preservadas. Não sobrescreve proxies; inválidos impedem gravação, conflitos/vazios/duplicatas são ignorados.");
    }
    private static async Task<IResult> Handle(HttpContext context, IProxyCsvPreviewService service, IProxyCsvImportService import,
        CsvPreviewLimits limits, CancellationToken token)
    {
        using var memory = new MemoryStream();
        var chunk = new byte[8192];
        try
        {
            var recovery = context.GetEndpoint()?.Metadata.GetMetadata<ProfileRecoveryEndpointMetadata>() is not null;
            if (recovery && context.Request.Query.Keys.Any(k => k is not ("page" or "pageSize" or "skipRowsWithoutConnection")))
                return Results.Problem(statusCode: 400, title: "Opção de recuperação desconhecida. Região/validade existentes não são alteradas.");
            if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media) || media.MediaType != "multipart/form-data")
                return Results.Problem(statusCode: 415, title: "Envie multipart/form-data com um arquivo no campo file.");
            var boundary = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
            if (string.IsNullOrEmpty(boundary) || boundary.Length > 128) return InvalidMultipart();
            if (context.Request.ContentLength > limits.MaxFileBytes + 16 * 1024) return TooLarge();
            if (!TryPage(context, "page", 1, out var page) || !TryPage(context, "pageSize", 20, out var pageSize))
                return Results.Problem(statusCode: 400, title: "Parâmetros de paginação inválidos.");
            var regionValues = context.Request.Query["defaultRegion"];
            if (regionValues.Count > 1) return Results.Problem(statusCode: 400, title: "Opção de região inválida.");
            var defaultRegion = regionValues.Count == 0 ? null : regionValues[0];
            var skipValues = context.Request.Query["skipRowsWithoutConnection"];
            var skipRowsWithoutConnection = false;
            if (skipValues.Count > 1 || (skipValues.Count == 1 && !bool.TryParse(skipValues[0], out skipRowsWithoutConnection)))
                return Results.Problem(statusCode: 400, title: "Opção para ignorar linhas sem conexão inválida.");
            var yearValues = context.Request.Query["referenceYear"];
            int? referenceYear = null;
            if (yearValues.Count > 1) return InvalidReferenceYear();
            if (yearValues.Count == 1)
            {
                if (!int.TryParse(yearValues[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year) || year is < 1 or > 9999)
                    return InvalidReferenceYear();
                referenceYear = year;
            }
            var commit = context.GetEndpoint()?.Metadata.GetMetadata<CsvCommitEndpointMetadata>() is not null;
            var proof = context.Request.Headers["X-Import-Confirmation"];
            var key = context.Request.Headers["Idempotency-Key"];
            Guid idempotencyKey = default;
            if (commit && (proof.Count != 1 || proof[0]!.Length > 4096 || key.Count != 1 || !Guid.TryParse(key[0],out idempotencyKey) || idempotencyKey == Guid.Empty))
                return Results.Problem(statusCode: 400, title: "Informe comprovante de prévia e Idempotency-Key UUID válido.");
            using var body = new LimitedRequestStream(context.Request.Body, limits.MaxFileBytes + 16 * 1024);
            var reader = new MultipartReader(boundary, body) { HeadersCountLimit = 16, HeadersLengthLimit = 4096, BodyLengthLimit = limits.MaxFileBytes + 16 * 1024 };
            var section = await reader.ReadNextSectionAsync(token);
            if (section is null || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                disposition.DispositionType != "form-data" || HeaderUtilities.RemoveQuotes(disposition.Name).Value != "file" ||
                (!disposition.FileName.HasValue && !disposition.FileNameStar.HasValue)) return InvalidMultipart();
            int read;
            while ((read = await section.Body.ReadAsync(chunk, token)) > 0)
            {
                if (memory.Length + read > limits.MaxFileBytes) return TooLarge();
                await memory.WriteAsync(chunk.AsMemory(0, read), token);
                CryptographicOperations.ZeroMemory(chunk);
            }
            if (await reader.ReadNextSectionAsync(token) is not null) return InvalidMultipart();
            var actor = new ProxyActor(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray());
            var csv = memory.GetBuffer().AsMemory(0, (int)memory.Length);
            if (recovery)
            {
                var recoveryService = context.RequestServices.GetRequiredService<IProfileRecoveryService>();
                return commit ? Results.Ok(await recoveryService.CommitAsync(actor, csv, skipRowsWithoutConnection, proof[0]!, idempotencyKey, context.TraceIdentifier, token))
                    : Results.Ok(await recoveryService.PreviewAsync(actor, csv, skipRowsWithoutConnection, page, pageSize, token));
            }
            return commit ? Results.Ok(await import.CommitAsync(actor, csv, proof[0]!, idempotencyKey, defaultRegion, context.TraceIdentifier, token, skipRowsWithoutConnection, referenceYear))
                : Results.Ok(await service.PreviewAsync(actor, csv, page, pageSize, token, defaultRegion, skipRowsWithoutConnection, referenceYear));
        }
        catch (RecoveryException error)
        {
            var status = error.Failure switch { RecoveryFailure.Forbidden => 403,
                RecoveryFailure.Confirmation or RecoveryFailure.IdempotencyConflict or RecoveryFailure.Conflict => 409,
                RecoveryFailure.Validation => 422, _ => 500 };
            var title = error.Failure switch {
                RecoveryFailure.Forbidden => "Acesso negado.",
                RecoveryFailure.Confirmation => "Prévia expirada, arquivo/opções alterados ou estado concorrente mudou. Gere nova prévia.",
                RecoveryFailure.IdempotencyConflict => "Idempotency-Key já utilizada para outra recuperação.",
                RecoveryFailure.Conflict => "Conflito concorrente; consulte o estado e gere nova prévia.",
                RecoveryFailure.Validation => "Recuperação não gravada. Corrija todos os impedimentos da prévia.",
                _ => "Falha interna na recuperação; nenhuma gravação parcial confirmada. Consulte antes de repetir com a mesma chave." };
            return Results.Problem(statusCode: status, title: title);
        }
        catch (CsvImportException error)
        {
            var status = error.Failure switch
            {
                CsvImportFailure.Confirmation or CsvImportFailure.IdempotencyConflict => 409,
                CsvImportFailure.Validation => 422, _ => 503
            };
            var title = error.Failure switch
            {
                CsvImportFailure.Confirmation => "Prévia inválida, expirada, de outro administrador ou arquivo/opções alterados. Gere nova prévia.",
                CsvImportFailure.IdempotencyConflict => "Idempotency-Key já utilizada com outro arquivo ou opções.",
                CsvImportFailure.Validation => "Importação não gravada: corrija os registros inválidos e gere nova prévia.",
                CsvImportFailure.EncryptionUnavailable => "Criptografia de proxies indisponível; importação não gravada.",
                _ => "Persistência da importação indisponível; nenhuma gravação parcial confirmada. Repita com a mesma Idempotency-Key."
            };
            return Results.Problem(statusCode: status, title: title, extensions: error.Report is null ? null : new Dictionary<string,object?> { ["report"] = error.Report });
        }
        catch (CsvPreviewException error)
        {
            return error.Failure switch
            {
                CsvPreviewFailure.LimitExceeded => TooLarge(error.Line),
                CsvPreviewFailure.Forbidden => Results.Problem(statusCode: 403, title: "Acesso negado."),
                CsvPreviewFailure.InvalidPage => Results.Problem(statusCode: 400, title: "Parâmetros de paginação inválidos."),
                CsvPreviewFailure.InvalidRegion => Results.Problem(statusCode: 400, title: "Região padrão deve ser não vazia, sem controles e até 100 caracteres."),
                CsvPreviewFailure.InvalidReferenceYear => InvalidReferenceYear(),
                _ => Results.Problem(statusCode: 400, title: "CSV vazio, UTF-8 inválido ou estrutura malformada.", extensions: new Dictionary<string, object?> { ["line"] = error.Line })
            };
        }
        catch (BadHttpRequestException error) when (error.StatusCode == 413) { return TooLarge(); }
        catch (InvalidDataException) { return Results.Problem(statusCode: 400, title: "Multipart inválido ou seção além do limite."); }
        catch (IOException) { return Results.Problem(statusCode: 400, title: "Upload incompleto ou multipart inválido."); }
        finally { CryptographicOperations.ZeroMemory(chunk); CryptographicOperations.ZeroMemory(memory.GetBuffer()); }
    }
    private static bool TryPage(HttpContext context, string name, int fallback, out int value)
    {
        value = fallback;
        return !context.Request.Query.TryGetValue(name, out var values) || (values.Count == 1 && int.TryParse(values[0], out value));
    }
    private static IResult InvalidMultipart() => Results.Problem(statusCode: 400, title: "Envie exatamente um arquivo no campo file, sem outros campos multipart.");
    private static IResult InvalidReferenceYear() => Results.Problem(statusCode: 400, title: "Ano de referência deve ser um único inteiro entre 1 e 9999, ou omitido.");
    private static IResult TooLarge(int line = 1) => Results.Problem(statusCode: 413, title: "Arquivo excede os limites de pré-visualização.", extensions: new Dictionary<string, object?> { ["line"] = line });
}
public sealed class CsvPreviewEndpointMetadata;
public sealed class CsvCommitEndpointMetadata;
public sealed class ProfileRecoveryEndpointMetadata;
// Nunca usa ReadForm/EnableBuffering: não cria arquivos temporários de upload.
internal sealed class LimitedRequestStream(Stream inner, long limit) : Stream
{
    private long consumed;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => consumed; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count); Count(read); return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        var read = await inner.ReadAsync(buffer, token); Count(read); return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    private void Count(int read) { consumed += read; if (consumed > limit) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
