using Microsoft.AspNetCore.Mvc;

namespace Viteme.Api.Middlewares;

/// <summary>Escreve respostas de erro no padrão ProblemDetails (RFC 9457) com <c>codigo</c> e <c>traceId</c>.</summary>
public static class RespostaProblema
{
    public static ProblemDetails Criar(HttpContext contexto, int status, string titulo, string detalhe, string codigo)
    {
        var problema = new ProblemDetails
        {
            Type = $"https://viteme.app/erros/{codigo.ToLowerInvariant().Replace('_', '-')}",
            Title = titulo,
            Status = status,
            Detail = detalhe,
        };
        problema.Extensions["codigo"] = codigo;
        problema.Extensions["traceId"] = contexto.TraceIdentifier;
        return problema;
    }

    public static async Task EscreverAsync(
        HttpContext contexto, int status, string titulo, string detalhe, string codigo,
        IDictionary<string, string[]>? erros = null)
    {
        if (contexto.Response.HasStarted)
        {
            return;
        }

        var problema = Criar(contexto, status, titulo, detalhe, codigo);
        if (erros is not null)
        {
            problema.Extensions["errors"] = erros;
        }

        contexto.Response.Clear();
        contexto.Response.StatusCode = status;
        await contexto.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
    }
}
