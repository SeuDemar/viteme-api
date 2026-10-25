using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Viteme.Api.Middlewares;

namespace Viteme.Api.Autenticacao;

/// <summary>
/// Proteção contra CSRF para endpoints que dependem só do cookie de renovação (E03 e E04). Como o cookie é
/// <c>SameSite=None</c>, o navegador o envia também em requisições de outros sites; por isso a requisição precisa
/// trazer o cabeçalho <see cref="Cabecalho"/>, que força o preflight de CORS (só a origem do frontend o passa), e,
/// quando houver <c>Origin</c> e uma origem configurada, ela deve ser a do frontend.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ExigirRequisicaoDoFrontendAttribute : Attribute, IAsyncActionFilter
{
    public const string Cabecalho = "X-Requested-With";
    public const string ValorCabecalho = "viteme-web";
    public const string Codigo = "REQUISICAO_NAO_PERMITIDA";

    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate proximo)
    {
        var http = contexto.HttpContext;

        if (!string.Equals(http.Request.Headers[Cabecalho], ValorCabecalho, StringComparison.Ordinal) || !OrigemPermitida(http))
        {
            var problema = RespostaProblema.Criar(http, StatusCodes.Status403Forbidden, "Requisição não permitida",
                "A requisição não veio do frontend do Viteme.", Codigo);
            contexto.Result = new ObjectResult(problema)
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await proximo();
    }

    private static bool OrigemPermitida(HttpContext http)
    {
        var origem = http.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origem))
        {
            return true; // clientes que não são navegadores não enviam Origin
        }

        var configurada = http.RequestServices.GetRequiredService<IConfiguration>()["Cors:OrigemFrontend"];
        return !string.IsNullOrWhiteSpace(configurada)
            && string.Equals(origem.TrimEnd('/'), configurada.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
}
