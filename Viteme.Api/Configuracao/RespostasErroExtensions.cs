using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Middlewares;

namespace Viteme.Api.Configuracao;

public static class RespostasErroExtensions
{
    /// <summary>Entrada inválida vira 400 ProblemDetails com <c>errors</c> por campo (nomes em camelCase).</summary>
    public static IServiceCollection AddRespostasDeErro(this IServiceCollection servicos)
    {
        servicos.Configure<ApiBehaviorOptions>(opcoes =>
        {
            opcoes.InvalidModelStateResponseFactory = contexto =>
            {
                var erros = contexto.ModelState
                    .Where(e => e.Value is { Errors.Count: > 0 })
                    .ToDictionary(
                        e => string.IsNullOrEmpty(e.Key) ? string.Empty : ParaCamelCase(e.Key),
                        e => e.Value!.Errors
                            .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) ? "Valor inválido." : x.ErrorMessage)
                            .ToArray());

                return new ProblemaResult(StatusCodes.Status400BadRequest, "Dados inválidos",
                    "Um ou mais campos estão inválidos.", "DADOS_INVALIDOS", erros);
            };
        });

        return servicos;
    }

    /// <summary>
    /// Escreve o ProblemDetails direto na resposta. Um <c>ObjectResult</c> seria reescrito para
    /// <c>application/json</c> pelo <c>[Produces]</c> dos controllers.
    /// </summary>
    private sealed class ProblemaResult(
        int status, string titulo, string detalhe, string codigo, IDictionary<string, string[]> erros) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext contexto) =>
            RespostaProblema.EscreverAsync(contexto.HttpContext, status, titulo, detalhe, codigo, erros);
    }

    private static string ParaCamelCase(string caminho) =>
        string.Join('.', caminho.Split('.').Select(p => JsonNamingPolicy.CamelCase.ConvertName(p)));
}
