using System.Net;

namespace SIGO.Components.Shared;

/// <summary>
/// Página de error como HTML estático y autocontenido. A propósito NO es un componente
/// Blazor: si la app falló, la página que lo comunica no debería depender del circuito
/// (ni de SignalR, ni del render interactivo) para poder mostrarse.
/// </summary>
public static class PaginaError
{
    public static string Html(string? requestId)
    {
        var id = string.IsNullOrWhiteSpace(requestId)
            ? ""
            : $"<p class=\"id\">ID de solicitud: <code>{WebUtility.HtmlEncode(requestId)}</code></p>";

        return $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Error — SIGO</title>
            <style>
                :root { color-scheme: dark; }
                body {
                    margin: 0; min-height: 100vh;
                    display: grid; place-items: center;
                    background: #1a1a1a; color: #e6e6e6;
                    font-family: system-ui, "Segoe UI", Roboto, sans-serif;
                    text-align: center; padding: 1.5rem;
                }
                .icono { font-size: 3rem; line-height: 1; color: #ef4444; }
                h1 { margin: 1rem 0 .5rem; font-size: 1.6rem; font-weight: 600; }
                p { margin: .35rem 0; color: #a3a3a3; }
                .id { font-size: .8rem; margin-top: 1rem; }
                code { background: #262626; padding: .15rem .4rem; border-radius: 4px; color: #d4d4d4; }
                a {
                    display: inline-block; margin-top: 1.5rem;
                    background: #3b82f6; color: #fff; text-decoration: none;
                    padding: .55rem 1.1rem; border-radius: 6px; font-weight: 500;
                }
                a:hover { background: #2563eb; }
            </style>
        </head>
        <body>
            <main>
                <div class="icono">&#9888;</div>
                <h1>Ocurrió un error</h1>
                <p>Se produjo un error al procesar la solicitud.</p>
                {{id}}
                <a href="/">Volver al inicio</a>
            </main>
        </body>
        </html>
        """;
    }
}
