using Microsoft.AspNetCore.Mvc;
using Viteme.Application.Excecoes;

namespace Viteme.Api.Middlewares;

/// <summary>
/// Converte exceções em ProblemDetails (RFC 9457). Erros inesperados viram 500 sem detalhes
/// internos e são registrados com o traceId.
/// </summary>
public class TratamentoExcecoesMiddleware(RequestDelegate proximo, ILogger<TratamentoExcecoesMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await proximo(contexto);
        }
        catch (RegraDeNegocioException ex)
        {
            await EscreverAsync(contexto, ex.StatusHttp, ex.Titulo, ex.Message, ex.Codigo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro inesperado. TraceId {TraceId}", contexto.TraceIdentifier);
            await EscreverAsync(contexto, StatusCodes.Status500InternalServerError,
                "Erro interno", "Ocorreu um erro inesperado.", "ERRO_INTERNO");
        }
    }

    private static async Task EscreverAsync(HttpContext contexto, int status, string titulo, string detalhe, string codigo)
    {
        if (contexto.Response.HasStarted)
        {
            return;
        }

        var problema = new ProblemDetails
        {
            Type = $"https://viteme.app/erros/{codigo.ToLowerInvariant().Replace('_', '-')}",
            Title = titulo,
            Status = status,
            Detail = detalhe,
        };
        problema.Extensions["codigo"] = codigo;
        problema.Extensions["traceId"] = contexto.TraceIdentifier;

        contexto.Response.Clear();
        contexto.Response.StatusCode = status;
        await contexto.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
    }
}
