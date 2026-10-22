using System.Net.Http.Headers;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CentralContingencia.Worker;

/// <summary>
/// Monta o worker conforme o modo. Remoto (padrão): somente HttpClientFactory → API; nenhuma referência a DbContext/PostgreSQL é registrada e
/// a presença de ConnectionStrings:DefaultConnection é recusada. LocalDatabase: acesso direto ao banco, aceito apenas em Development.
/// </summary>
public static class WorkerComposition
{
    public static WorkerOptions Add(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, Action<IHttpClientBuilder>? configureHttp = null)
    {
        var options = configuration.GetSection(WorkerOptions.Section).Get<WorkerOptions>() ?? new();
        services.AddSingleton<ITaskExecutor, SimulatedProcessingExecutor>();

        if (options.Mode == WorkerMode.LocalDatabase)
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Worker:Mode=LocalDatabase (acesso direto ao banco) só é permitido em Development. Use o modo Remote com a credencial do executor.");
            if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")) && !environment.IsDevelopment())
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection é obrigatório.");
            options.Validate(configuration.GetSection(WorkQueueOptions.Section).Get<WorkQueueOptions>() ?? new());
            services.AddWorkerInfrastructure(configuration);
            services.AddScoped<IExecutorSession, NullExecutorSession>();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
                throw new InvalidOperationException("O worker remoto não deve ter acesso direto ao PostgreSQL: remova ConnectionStrings:DefaultConnection deste processo.");
            options.Validate(null);
            var credential = ResolveCredential(options.Api);
            var baseUri = ResolveBaseUri(options.Api, environment);
            var client = services.AddHttpClient<HttpWorkQueueClient>(http =>
            {
                http.BaseAddress = baseUri;
                http.Timeout = TimeSpan.FromSeconds(options.Api.RequestTimeoutSeconds);
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("CentralContingencia-Worker");
            });
            if (!string.IsNullOrWhiteSpace(options.Api.TrustedCaFile))
            {
                var authorities = TlsTrust.LoadAuthorities(options.Api.TrustedCaFile);
                client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    SslOptions = { RemoteCertificateValidationCallback = (_, certificate, chain, errors) => TlsTrust.Validate(authorities, certificate, chain, errors) },
                });
            }
            configureHttp?.Invoke(client);
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<IWorkQueueService>(provider => provider.GetRequiredService<HttpWorkQueueClient>());
            services.AddScoped<IExecutorSession>(provider => provider.GetRequiredService<HttpWorkQueueClient>());
        }
        services.AddSingleton(options);
        services.AddSingleton<WorkerService>();
        services.AddHostedService(provider => provider.GetRequiredService<WorkerService>());
        return options;
    }

    internal static string ResolveCredential(WorkerApiOptions api)
    {
        var value = api.Credential;
        if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(api.CredentialFile))
        {
            try { value = File.ReadAllText(api.CredentialFile).Trim(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new InvalidOperationException("Worker:Api:CredentialFile não pôde ser lido."); }
        }
        // A mensagem nunca ecoa o valor informado.
        if (string.IsNullOrWhiteSpace(value) || !ExecutorCredentials.TryParse(value.Trim(), out _, out _))
            throw new InvalidOperationException("Credencial do executor ausente ou em formato inválido (Worker:Api:Credential ou Worker:Api:CredentialFile).");
        return value.Trim();
    }

    internal static Uri ResolveBaseUri(WorkerApiOptions api, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(api.BaseUrl, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query))
            throw new InvalidOperationException("Worker:Api:BaseUrl deve ser uma URL absoluta, sem usuário nem query.");
        var secure = uri.Scheme == Uri.UriSchemeHttps;
        // A credencial viaja em todo pedido: só HTTPS. HTTP puro apenas para loopback em Development.
        if (!secure && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new InvalidOperationException("Worker:Api:BaseUrl deve usar HTTPS (HTTP somente para localhost em Development).");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
