using Viteme.Domain.Excecoes;

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
            await RespostaProblema.EscreverAsync(contexto, ex.StatusHttp, ex.Titulo, ex.Message, ex.Codigo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro inesperado. TraceId {TraceId}", contexto.TraceIdentifier);
            await RespostaProblema.EscreverAsync(contexto, StatusCodes.Status500InternalServerError,
                "Erro interno", "Ocorreu um erro inesperado.", "ERRO_INTERNO");
        }
    }
}
