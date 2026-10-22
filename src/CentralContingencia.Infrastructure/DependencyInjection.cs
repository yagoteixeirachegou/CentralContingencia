using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Infrastructure.Proxies;
using CentralContingencia.Infrastructure.Identity;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddDatabase(services, configuration);
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        }).AddEntityFrameworkStores<CentralContingenciaDbContext>().AddDefaultTokenProviders();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-Central.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        // Valida revogação em cada requisição, incluindo cookies copiados antes do logout.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAdminProvisioner, AdminProvisioner>();
        // Resolvida sob demanda: ausência de chave recusa operações de proxies,
        // sem impedir o funcionamento da autenticação já homologada.
        services.AddSingleton<IProxyPasswordProtector>(_ => new AesGcmProxyPasswordProtector(configuration));
        services.AddSingleton(TimeProvider.System);
        AddWorkQueue(services, configuration);
        services.AddScoped<CentralContingencia.Application.Health.IReadinessProbe, CentralContingencia.Infrastructure.Hosting.EfReadinessProbe>();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IConfiguration>().GetSection("ProxyAllocations")
                .Get<CentralContingencia.Application.Allocations.ReservationOptions>() ?? new();
            options.Validate();
            return options;
        });
        services.Configure<CentralContingencia.Infrastructure.AdsPower.AdsPowerConnectionOptions>(configuration.GetSection("AdsPower"));
        services.AddHttpClient<CentralContingencia.Infrastructure.AdsPower.AdsPowerLocalApiClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton(_ =>
        {
            var options = configuration.GetSection("AdsPower").Get<CentralContingencia.Application.AdsPower.AdsPowerOptions>() ?? new();
            options.Validate();
            return options;
        });
        // Singleton: o último resultado fica em memória e é compartilhado entre requisições (nenhum perfil é persistido).
        services.AddSingleton<CentralContingencia.Application.AdsPower.IAdsPowerDashboardService>(provider =>
            new CentralContingencia.Application.AdsPower.AdsPowerDashboardService(new CentralContingencia.Infrastructure.AdsPower.ScopedAdsPowerSource(provider),
                provider.GetRequiredService<CentralContingencia.Application.AdsPower.AdsPowerOptions>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<CentralContingencia.Application.Warmup.IHealthCheckStore, CentralContingencia.Infrastructure.Profiles.EfHealthCheckStore>();
        services.AddScoped<CentralContingencia.Application.Warmup.IHealthCheckService, CentralContingencia.Application.Warmup.HealthCheckService>();
        services.AddScoped<IProxyStore, EfProxyStore>();
        services.AddScoped<IOperationalGridReader, EfOperationalGridReader>();
        services.AddScoped<IOperationalGridService, OperationalGridService>();
        services.AddScoped<CentralContingencia.Application.Allocations.IOperationalProfileStore, CentralContingencia.Infrastructure.Allocations.EfOperationalProfileStore>();
        services.AddScoped<CentralContingencia.Application.Allocations.IAllocationStore, CentralContingencia.Infrastructure.Allocations.EfAllocationStore>();
        services.AddScoped<CentralContingencia.Application.Ingestion.IIngestionStore, CentralContingencia.Infrastructure.Allocations.EfIngestionStore>();
        services.AddScoped<CentralContingencia.Application.Ingestion.IAdsPowerGroupReader>(provider => provider.GetRequiredService<CentralContingencia.Infrastructure.AdsPower.AdsPowerLocalApiClient>());
        services.AddSingleton(_ =>
        {
            var ingestion = configuration.GetSection("ProfileIngestion").Get<CentralContingencia.Application.Ingestion.ProfileIngestionOptions>() ?? new();
            ingestion.Validate();
            return ingestion;
        });
        services.AddScoped<CentralContingencia.Application.Ingestion.IProfileIngestionService>(provider => new CentralContingencia.Application.Ingestion.ProfileIngestionService(
            provider.GetRequiredService<CentralContingencia.Application.Ingestion.IIngestionStore>(), () => provider.GetRequiredService<IProxyPasswordProtector>(),
            provider.GetRequiredService<CentralContingencia.Application.Ingestion.IAdsPowerGroupReader>(), provider.GetRequiredService<CentralContingencia.Application.Ingestion.ProfileIngestionOptions>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<CentralContingencia.Application.Allocations.IOperationalManagementService, CentralContingencia.Application.Allocations.OperationalManagementService>();
        services.AddSingleton(provider =>
        {
            var limits = provider.GetRequiredService<IConfiguration>().GetSection("ProxyImportPreview").Get<CsvPreviewLimits>() ?? new();
            limits.Validate();
            return limits;
        });
        services.AddScoped<IProxyImportConflictReader, EfProxyImportConflictReader>();
        services.AddSingleton<IImportConfirmationProtector, DataProtectionImportConfirmation>();
        services.AddScoped<ProxyCsvPreviewService>();
        services.AddScoped<IProxyCsvPreviewService>(provider => provider.GetRequiredService<ProxyCsvPreviewService>());
        services.AddScoped<IProxyImportStore, EfProxyImportStore>();
        services.AddScoped<CentralContingencia.Application.Profiles.Recovery.IProfileRecoveryStore, CentralContingencia.Infrastructure.Profiles.EfProfileRecoveryStore>();
        services.AddScoped<CentralContingencia.Application.Profiles.Recovery.IProfileRecoveryService, CentralContingencia.Application.Profiles.Recovery.ProfileRecoveryService>();
        services.AddScoped<IProxyCsvImportService>(provider => new ProxyCsvImportService(
            provider.GetRequiredService<ProxyCsvPreviewService>(), provider.GetRequiredService<IProxyImportStore>(),
            provider.GetRequiredService<IImportConfirmationProtector>(), () => provider.GetRequiredService<IProxyPasswordProtector>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<CsvPreviewLimits>()));
        services.AddScoped<IProxyManagementService>(provider => new ProxyManagementService(
            provider.GetRequiredService<IProxyStore>(), () => provider.GetRequiredService<IProxyPasswordProtector>(),
            provider.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>Registro mínimo do processo Worker: banco e fila de tarefas, sem Identity, proxies nem integrações.</summary>
    public static IServiceCollection AddWorkerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddDatabase(services, configuration);
        services.AddSingleton(TimeProvider.System);
        AddWorkQueue(services, configuration);
        return services;
    }

    private static void AddDatabase(IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<CentralContingenciaDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection") ??
                "Host=127.0.0.1;Port=55432;Database=central_contingencia_dev;Username=central_dev"));

    private static void AddWorkQueue(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ =>
        {
            var queue = configuration.GetSection(CentralContingencia.Application.WorkQueue.WorkQueueOptions.Section).Get<CentralContingencia.Application.WorkQueue.WorkQueueOptions>() ?? new();
            queue.Validate();
            return queue;
        });
        services.AddScoped<CentralContingencia.Application.WorkQueue.IWorkTaskStore, CentralContingencia.Infrastructure.WorkQueue.EfWorkTaskStore>();
        services.AddScoped<CentralContingencia.Application.WorkQueue.IExecutorStore, CentralContingencia.Infrastructure.WorkQueue.EfExecutorStore>();
        services.AddScoped<CentralContingencia.Application.WorkQueue.IExecutorAdminService, CentralContingencia.Application.WorkQueue.ExecutorAdminService>();
        services.AddScoped<CentralContingencia.Application.WorkQueue.IExecutorGateway, CentralContingencia.Application.WorkQueue.ExecutorGateway>();
        services.AddScoped<CentralContingencia.Application.WorkQueue.IWorkTaskService, CentralContingencia.Application.WorkQueue.WorkTaskService>();
        services.AddScoped<CentralContingencia.Application.WorkQueue.IWorkQueueService, CentralContingencia.Application.WorkQueue.WorkQueueService>();
    }
}
