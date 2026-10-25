namespace Viteme.Api.Middlewares;

/// <summary>
/// Cabeçalhos de segurança em toda resposta da API. Não define Content-Security-Policy: ela pertence ao frontend
/// (e a interface do Swagger, usada só em desenvolvimento/demonstração, carrega scripts próprios).
/// </summary>
public class CabecalhosDeSegurancaMiddleware(RequestDelegate proximo)
{
    public Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            var cabecalhos = contexto.Response.Headers;
            cabecalhos["X-Content-Type-Options"] = "nosniff";
            cabecalhos["X-Frame-Options"] = "DENY";
            cabecalhos["Referrer-Policy"] = "no-referrer";

            // Respostas da API têm dados pessoais: nada de cache compartilhado. Rotas de documentação ficam de fora.
            if (!contexto.Request.Path.StartsWithSegments("/swagger") && !contexto.Request.Path.StartsWithSegments("/openapi")
                && !cabecalhos.ContainsKey("Cache-Control"))
            {
                cabecalhos["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        });

        return proximo(contexto);
    }
}
