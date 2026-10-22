using Microsoft.AspNetCore.Builder;

namespace CentralContingencia.Api.Hosting;

/// <summary>Serve o build do React (wwwroot) na mesma origem da API. Sem wwwroot/index.html (desenvolvimento) nada é mapeado.</summary>
public static class SpaHosting
{
    private static bool HasBuild(WebApplication app) => app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;

    public static void UseSpaStaticFiles(this WebApplication app)
    {
        if (!HasBuild(app)) return;
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = CacheFor(context.File.Name) });
    }

    public static void MapSpaFallback(this WebApplication app)
    {
        if (!HasBuild(app)) return;
        // Rotas de API e health nunca caem no index.html: rota desconhecida continua sendo 404.
        foreach (var prefix in new[] { "/api", "/health" })
            app.MapFallback(prefix + "/{**rest}", () => Results.NotFound()).ExcludeFromDescription();
        // "nonfile": caminho sem extensão. Um /assets/inexistente.js continua 404.
        app.MapFallbackToFile("{*path:nonfile}", "index.html", new StaticFileOptions
        {
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = CacheFor("index.html")
        });
    }

    // index.html aponta para arquivos com hash; ele precisa ser sempre revalidado. Os arquivos com hash podem ficar em cache.
    private static string CacheFor(string fileName) => fileName == "index.html" ? "no-cache" : "public, max-age=31536000, immutable";
}
