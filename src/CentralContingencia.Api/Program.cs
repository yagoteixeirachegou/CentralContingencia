using System.Threading.RateLimiting;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Infrastructure;
using CentralContingencia.Infrastructure.Hosting;
using CentralContingencia.Api.Identity;
using CentralContingencia.Api.Proxies;
using CentralContingencia.Api.AdsPower;
using CentralContingencia.Api.Warmup;
using CentralContingencia.Api.WorkQueue;
using CentralContingencia.Api.Integration;
using Microsoft.AspNetCore.Authentication;
using CentralContingencia.Api.Operations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.OpenApi;
using CentralContingencia.Api.Hosting;
using CentralContingencia.Application.Health;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 16 * 1024;
});
ProductionConfiguration.EnsureValid(builder.Configuration, builder.Environment);
if (PlatformHosting.UrlsFromPort(builder.Configuration) is { } platformUrls) builder.WebHost.UseUrls(platformUrls);
ReverseProxySettings.Configure(builder.Services, builder.Configuration);
builder.Services.AddCentralDataProtection(builder.Configuration);
builder.Services.AddOpenApi(options =>
{
options.AddSchemaTransformer((schema, context, cancellationToken) =>
{
    if (context.JsonTypeInfo.Type == typeof(CentralContingencia.Application.Allocations.CreateProfileRequest) ||
        context.JsonTypeInfo.Type == typeof(CentralContingencia.Application.Allocations.UpdateProfileRequest))
        foreach (var name in new[] { "platform1", "platform2" })
            if (schema.Properties?.TryGetValue(name, out var assignment) == true && assignment is OpenApiSchema field)
            {
                field.MaxLength = 100;
                field.Description = "Opcional; null representa ausência. Catálogo: " + string.Join(", ", CentralContingencia.Domain.Profiles.ProfileAssignments.Catalog);
            }
    if (context.JsonTypeInfo.Type == typeof(CreateProxyRequest) || context.JsonTypeInfo.Type == typeof(UpdateProxyRequest))
        if (schema.Properties?.TryGetValue("password", out var password) == true && password is OpenApiSchema secret)
            secret.WriteOnly = true;
    return Task.CompletedTask;
});
options.AddDocumentTransformer((document, context, cancellationToken) =>
{
    document.Components ??= new();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["SessionCookie"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie,
        Name = "__Host-Central.Session", Description = "Sessão Identity HttpOnly/Secure/SameSite=Strict."
    };
    document.Components.SecuritySchemes["ExecutorKey"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "Credencial do executor (ce1.<ExecutorId>.<segredo>), provisionada por um Admin em POST /api/v1/executors."
    };
    foreach (var (path, item) in document.Paths)
        foreach (var (method, operation) in item.Operations ?? [])
        {
            if ((method == HttpMethod.Post || method == HttpMethod.Patch) && path.StartsWith("/api/v1") && !path.StartsWith("/api/v1/agent"))
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-CSRF-TOKEN", In = ParameterLocation.Header, Required = true,
                    Description = "Obtenha em /api/v1/auth/csrf após cada mudança de identidade.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });
            }
            if (path.StartsWith("/api/v1/proxies") || path.StartsWith("/api/v1/tasks") || path.StartsWith("/api/v1/executors") || path.StartsWith("/api/v1/profiles") || path.StartsWith("/api/v1/allocations") || path is "/api/v1/auth/me" or "/api/v1/auth/logout" or "/api/v1/auth/change-password" or "/api/v1/users")
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("SessionCookie", document)] = [] }];
            if (path.StartsWith("/api/v1/agent"))
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ExecutorKey", document)] = [] }];
            if (path is "/api/v1/allocations/reserve" or "/api/v1/tasks")
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                    Description = path == "/api/v1/tasks" ? "UUID por operação; repetição com o mesmo conteúdo devolve a mesma tarefa (200), com conteúdo diferente 409." : "UUID por operação; repetição retorna 409, sem replay.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } });
            }
            if (path.StartsWith("/api/v1/profiles") || path.StartsWith("/api/v1/allocations"))
            {
                operation.Description = (operation.Description ?? "") + " Leitura: Admin/Manager/Operator. Escrita: Admin/Manager, CSRF e limite sensitive. Paginação 1..10000; pageSize 1..100, padrão 20. Filtros exatos; ordenação por whitelist, desempate UUID.";
                foreach (var parameter in operation.Parameters ?? [])
                    if (parameter.Schema is OpenApiSchema schema)
                    {
                        var name = parameter.Name?.ToLowerInvariant();
                        if (name is "page" or "pagesize") { schema.Minimum = "1"; schema.Maximum = name == "page" ? "10000" : "100"; }
                        if (name == "name") schema.MaxLength = 200;
                        if (name == "platform") schema.MaxLength = 100;
                    }
                if (method == HttpMethod.Post && (path.EndsWith("/close") || path.EndsWith("/confirm") || path.EndsWith("/release")))
                    operation.RequestBody = new OpenApiRequestBody { Required = true, Content = new Dictionary<string, OpenApiMediaType>
                    { ["application/json"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.Object, MaxProperties = 0 } } } };
            }
            if (path.StartsWith("/api/v1/profiles"))
                operation.Description = (operation.Description ?? "") + " platform é legado textual preservado; platform1/platform2 são atribuições opcionais do catálogo. PATCH: omitir preserva; null limpa somente os novos slots. Os slots são independentes e podem repetir um valor.";
            if (path == OperationalGridEndpoint.Path)
            {
                operation.Description = "Admin/Manager/Operator. Uma linha por proxy; profile/region são contains case-insensitive literais e combinados por AND. platform corresponde a platform1 OU platform2 do catálogo. availability: Available, Reserved, InUse, Unavailable. sort=expiresAt; direction=asc/desc; desempate proxyId. Snapshot READ ONLY, evaluatedAt do servidor; não recupera reservas nem grava. Sem credenciais.";
                foreach (var parameter in operation.Parameters ?? [])
                    if (parameter.Schema is OpenApiSchema schema)
                    {
                        var name = parameter.Name?.ToLowerInvariant();
                        if (parameter is OpenApiParameter writableParameter && parameter.Name is { Length: > 0 } parameterName)
                            writableParameter.Name = char.ToLowerInvariant(parameterName[0]) + parameterName[1..];
                        if (name is "page" or "pagesize") { schema.Type = JsonSchemaType.Integer; schema.Minimum = "1"; schema.Maximum = name == "page" ? "10000" : "100"; }
                        if (name == "profile") schema.MaxLength = 200;
                        if (name is "region" or "platform") schema.MaxLength = 100;
                    }
            }
            if (path is ProxyImportPreviewEndpoint.Path or ProxyImportPreviewEndpoint.CommitPath or ProxyImportPreviewEndpoint.RecoveryPreviewPath or ProxyImportPreviewEndpoint.RecoveryCommitPath)
            {
                // O handler lê o stream manualmente; metadata Accepts causaria fallback 415
                // do roteador antes da política administrativa para media types incorretos.
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["multipart/form-data"] = new()
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                Required = new HashSet<string> { "file" },
                                Properties = new Dictionary<string, IOpenApiSchema>
                                { ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } }
                            }
                        }
                    }
                };
                operation.Parameters ??= [];
                foreach (var (name, maximum) in path is ProxyImportPreviewEndpoint.Path or ProxyImportPreviewEndpoint.RecoveryPreviewPath ? new[] { ("page", 10000), ("pageSize", 100) } : Array.Empty<(string,int)>())
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = name, In = ParameterLocation.Query,
                        Description = name == "page" ? "Página da prévia, padrão 1; reenvie o CSV." : "Itens por página, padrão 20.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                    });
                if (path is ProxyImportPreviewEndpoint.Path or ProxyImportPreviewEndpoint.CommitPath) operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "defaultRegion", In = ParameterLocation.Query,
                    Description = "Região explicitamente informada pelo Admin para células ausentes; deve coincidir na prévia e confirmação. Não substitui células preenchidas.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 100 }
                });
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "skipRowsWithoutConnection", In = ParameterLocation.Query,
                    Description = "Padrão false. Opção explícita para ignorar somente linhas sem host, porta, usuário e senha; deve coincidir com a prévia.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean }
                });
                if (path is ProxyImportPreviewEndpoint.Path or ProxyImportPreviewEndpoint.CommitPath) operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "referenceYear", In = ParameterLocation.Query,
                    Description = "Opcional, sem padrão automático. Inteiro entre 1 e 9999 aplicado exclusivamente a dd/MM sem ano; datas completas preservadas. Deve coincidir com a prévia e participa da prova protegida.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "9999" }
                });
                if (path is ProxyImportPreviewEndpoint.CommitPath or ProxyImportPreviewEndpoint.RecoveryCommitPath)
                    foreach (var name in new[] { "X-Import-Confirmation", "Idempotency-Key" })
                        operation.Parameters.Add(new OpenApiParameter { Name = name, In = ParameterLocation.Header, Required = true,
                            Description = name == "Idempotency-Key" ? "UUID por operação; reutilize em tentativas da mesma importação." : "Comprovante protegido emitido na prévia; validade de 15 minutos.",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
            }
        }
    return Task.CompletedTask;
});
});
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
// Chave de serviço das integrações (n8n): somente hash na configuração; escopo mínimo por chave.
builder.Services.Configure<IntegrationKeyOptions>(builder.Configuration.GetSection("Integration"));
builder.Services.Configure<N8nIngestionOptions>(builder.Configuration.GetSection("N8nIngestion"));
builder.Configuration.GetSection("N8nIngestion").Get<N8nIngestionOptions>()?.Validate();
builder.Services.AddSingleton<UploadGate>();
// Freio de tentativas de autenticação inválidas (executores e chave do n8n): 10 falhas por minuto e por cliente, por padrão.
builder.Services.AddSingleton(provider => new AuthFailureThrottle(provider.GetRequiredService<TimeProvider>(),
    builder.Configuration.GetValue<int?>("Security:AuthFailureLimit") ?? 10, TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("Security:AuthFailureWindowSeconds") ?? 60)));
var recoverySeconds = builder.Configuration.GetValue<int?>(WorkQueueRecoveryService.Setting) ?? WorkQueueRecoveryService.DefaultSeconds;
if (recoverySeconds is < 0 or > 3600) throw new InvalidOperationException("WorkQueue:RecoveryIntervalSeconds deve estar entre 0 (desligado) e 3600.");
if (recoverySeconds > 0)
    builder.Services.AddHostedService(provider => new WorkQueueRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromSeconds(recoverySeconds), provider.GetRequiredService<ILogger<WorkQueueRecoveryService>>()));
builder.Services.AddHttpClient<N8nIngestionClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, IntegrationKeyHandler>(IntegrationAuth.Scheme, null);
builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ExecutorKeyHandler>(ExecutorAuth.Scheme, null);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(IntegrationAuth.ProfileIngestionPolicy, p => p.AddAuthenticationSchemes(IntegrationAuth.Scheme).RequireAuthenticatedUser().RequireClaim("scope", IntegrationAuth.ProfileIngestionScope));
    options.AddPolicy(ExecutorAuth.Policy, p => p.AddAuthenticationSchemes(ExecutorAuth.Scheme).RequireAuthenticatedUser().RequireClaim("scope", ExecutorAuth.Scope));
    options.AddPolicy(AccessPolicies.Administration, p => p.RequireAuthenticatedUser().RequireRole(AccessRoles.Admin));
    options.AddPolicy(AccessPolicies.Management, p => p.RequireAuthenticatedUser().RequireRole(AccessRoles.Admin, AccessRoles.Manager));
    options.AddPolicy(AccessPolicies.Operations, p => p.RequireAuthenticatedUser().RequireRole(AccessRoles.Admin, AccessRoles.Manager, AccessRoles.Operator));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Central.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, token) =>
    {
        // Espera real da janela quando o limitador a informa (o worker a respeita); 60 s é o teto de segurança.
        var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? Math.Clamp((int)Math.Ceiling(retryAfter.TotalSeconds), 1, 60) : 60;
        context.HttpContext.Response.Headers.RetryAfter = wait.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Results.Problem(statusCode: 429, title: "Muitas tentativas. Aguarde e tente novamente.").ExecuteAsync(context.HttpContext);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        // O canal dos executores NÃO divide a cota por IP com os usuários (um worker e pessoas atrás do mesmo NAT se derrubariam): ele tem cota por executor e,
        // para credenciais inválidas, o freio AuthFailureThrottle. Sem cabeçalho Bearer, a rota continua sob o limite global.
        context.Request.Path.StartsWithSegments("/api/v1/agent") && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? RateLimitPartition.GetNoLimiter("agent")
            : RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new() { PermitLimit = builder.Configuration.GetValue<int?>("Security:GlobalPermitLimit") ?? 120,
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Cota por executor autenticado (não por IP): um worker legítimo atrás de NAT não divide limite com outros.
    options.AddPolicy(ExecutorAuth.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = builder.Configuration.GetValue<int?>("Security:ExecutorPermitLimit") ?? 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(IntegrationAuth.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = builder.Configuration.GetValue<int?>("Security:IntegrationPermitLimit") ?? 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    foreach (var policy in new[] { "login", "sensitive" })
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new() { PermitLimit = builder.Configuration.GetValue<int?>("Security:EndpointPermitLimit") ?? 10,
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
// Valida configuração efetiva (inclusive overrides do host) antes de aceitar uploads.
_ = app.Services.GetRequiredService<CentralContingencia.Application.Proxies.Import.CsvPreviewLimits>();
_ = app.Services.GetRequiredService<CentralContingencia.Application.Allocations.ReservationOptions>();
if (args.Contains("--provision-admin"))
{
    using var scope = app.Services.CreateScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<IAdminProvisioner>().ProvisionAsync(
            Environment.GetEnvironmentVariable("CENTRAL_ADMIN_EMAIL") ?? "",
            Environment.GetEnvironmentVariable("CENTRAL_ADMIN_PASSWORD") ?? "");
        Console.WriteLine("Primeiro administrador provisionado.");
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Provisionamento recusado. Verifique migrations, requisitos de senha e se já existe Admin.");
        Environment.ExitCode = 1;
    }
    finally
    {
        Environment.SetEnvironmentVariable("CENTRAL_ADMIN_PASSWORD", null);
    }
    return;
}
if (args.Contains("--migrate"))
{
    // Aplicação controlada de migrations (ex.: comando de pré-deploy da plataforma). Ver MigrationRunner: --plan lista; aplicar exige CENTRAL_MIGRATE_CONFIRM.
    using var scope = app.Services.CreateScope();
    Environment.ExitCode = await CentralContingencia.Infrastructure.Hosting.MigrationRunner.RunAsync(
        scope.ServiceProvider.GetRequiredService<CentralContingencia.Infrastructure.Persistence.CentralContingenciaDbContext>(), args.Contains("--plan"),
        Environment.GetEnvironmentVariable(CentralContingencia.Infrastructure.Hosting.MigrationRunner.ConfirmVariable),
        Environment.GetEnvironmentVariable(CentralContingencia.Infrastructure.Hosting.MigrationRunner.DestructiveVariable), Console.Out, CancellationToken.None);
    return;
}
// Primeiro da cadeia: HTTPS e IP do cliente (rate limit) dependem de X-Forwarded-* aceito somente de proxies configurados.
if (ReverseProxySettings.From(app.Configuration).Enabled) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") && !context.Request.IsHttps)
    {
        await Results.Problem(statusCode: 400, title: "Esta API exige HTTPS.").ExecuteAsync(context);
        return;
    }
    await next(context);
});
// Health checks da plataforma chegam por HTTP interno (e, no Railway, com Host healthcheck.railway.app): nunca redirecionar, ou o deploy falha por 307 em vez de 2xx.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
app.UseSpaStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method))
        {
            var csvPreview = context.GetEndpoint()?.Metadata.GetMetadata<CsvPreviewEndpointMetadata>() is not null;
            var upload = context.GetEndpoint()?.Metadata.GetMetadata<UploadEndpointMetadata>() is not null;
            if (csvPreview || upload)
            {
                if (upload) ProfileUploadEndpoints.ConfigureBodyLimit(context); else ProxyImportPreviewEndpoint.ConfigureBodyLimit(context);
                // Impede que antiforgery tente ler/bufferizar multipart quando o header está ausente.
                if (string.IsNullOrEmpty(context.Request.Headers["X-CSRF-TOKEN"]))
                {
                    await Results.Problem(statusCode: 400, title: "Token CSRF inválido. Atualize a página.").ExecuteAsync(context);
                    return;
                }
            }
            // Endpoints de integração aceitam só chave de serviço (sem cookie), então não há CSRF a validar.
            var integration = context.GetEndpoint()?.Metadata.GetMetadata<IntegrationEndpointMetadata>() is not null;
            try { if (!integration) await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: 400, title: "Token CSRF inválido. Atualize a página.").ExecuteAsync(context);
                return;
            }
            // A revelação de credenciais não recebe corpo: só exige sessão, CSRF e papel Admin.
            var reveal = context.Request.Path.Value?.EndsWith("/credentials/reveal", StringComparison.Ordinal) == true;
            if (context.Request.Path.StartsWithSegments("/api/v1/proxies") && !csvPreview && !reveal)
            {
                if (!context.Request.HasJsonContentType())
                {
                    await Results.Problem(statusCode: 415, title: "Envie um objeto JSON.").ExecuteAsync(context);
                    return;
                }
                if (!await ProxyEndpoints.ValidateBodyAsync(context))
                {
                    await Results.Problem(statusCode: 400, title: "Objeto JSON de proxy inválido.").ExecuteAsync(context);
                    return;
                }
            }
            if (context.GetEndpoint()?.Metadata.GetMetadata<OperationalBodyMetadata>() is { } operationalBody)
            {
                if (!context.Request.HasJsonContentType()) { await Results.Problem(statusCode: 415, title: "Envie um objeto JSON.").ExecuteAsync(context); return; }
                if (!await OperationalEndpoints.ValidateBodyAsync(context, operationalBody)) { await Results.Problem(statusCode: 400, title: "Objeto JSON operacional inválido.").ExecuteAsync(context); return; }
            }
        }
    }
    await next(context);
});
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Text("Healthy")).WithName("Health")
    .WithSummary("Disponibilidade do processo da API; não verifica PostgreSQL.");
app.MapGet("/health/ready", async (IReadinessProbe probe, CancellationToken token) =>
        await probe.IsReadyAsync(token) ? Results.Text("Ready") : Results.Text("NotReady", statusCode: 503)).WithName("Ready")
    .WithSummary("Prontidão: PostgreSQL acessível e nenhuma migration pendente. Resposta sem detalhes.");
app.MapAuthEndpoints();
app.MapProxyEndpoints();
app.MapOperationalGrid();
app.MapProxyImportPreview();
app.MapOperationalEndpoints();
app.MapWarmupEndpoints();
app.MapWorkTaskEndpoints();
app.MapNetworkProbe();
app.MapExecutorAdminEndpoints();
app.MapAgentEndpoints();
if (LocalIntegrations.IsEnabled(app.Configuration, app.Environment))
{
    app.MapAdsPowerEndpoints();
    app.MapIngestionEndpoints();
    app.MapProfileUploadEndpoints();
}
else app.MapUnavailable();
app.MapSpaFallback();
app.Run();
public partial class Program;
