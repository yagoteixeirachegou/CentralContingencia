using CentralContingencia.Worker;
using Microsoft.Extensions.Hosting;

// Ponto de entrada nomeado (e não "Program"): evita conflito de tipo com o Program da API nos projetos de teste que referenciam os dois.
internal static class WorkerProgram
{
    private static async Task Main(string[] args)
    {
        // Content root = pasta do executável: appsettings.json é encontrado mesmo quando o serviço inicia com outro diretório de trabalho.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
        var options = WorkerComposition.Add(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = options.ShutdownGrace + TimeSpan.FromSeconds(10));
        await builder.Build().RunAsync();
    }
}
