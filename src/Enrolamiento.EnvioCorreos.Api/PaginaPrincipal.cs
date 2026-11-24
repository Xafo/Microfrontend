using System.Text.Encodings.Web;

namespace Enrolamiento.EnvioCorreos.Api;

/// <summary>
/// Entrega wwwroot/index.html agregando &lt;base href="{PathBase}/"&gt;. Las rutas de la pantalla son relativas
/// (./assets, api/...), y sin la base un acceso a "/MiApp" (sin "/" final) las resolvería contra la raíz del servidor.
/// </summary>
public static class PaginaPrincipal
{
    public static async Task Servir(HttpContext ctx, IWebHostEnvironment env)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound; // una ruta de API inexistente no devuelve la pantalla
            return;
        }

        var archivo = env.WebRootFileProvider.GetFileInfo("index.html");
        if (!archivo.Exists)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsync("No se encontró la pantalla (wwwroot/index.html). Ejecute 'npm run build' en la carpeta web antes de publicar.");
            return;
        }

        string html;
        using (var lector = new StreamReader(archivo.CreateReadStream()))
            html = await lector.ReadToEndAsync();

        var baseHref = HtmlEncoder.Default.Encode(ctx.Request.PathBase.ToUriComponent().TrimEnd('/') + "/");
        var i = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        html = i < 0 ? html : html.Insert(i + "<head>".Length, $"\n    <base href=\"{baseHref}\" />");

        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        await ctx.Response.WriteAsync(html);
    }
}
