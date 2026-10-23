using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Viteme.Api.Middlewares;

namespace Viteme.Api.Autorizacao;

/// <summary>Converte as falhas de autorização em ProblemDetails: 404 para evento oculto do usuário e 403 para falta de papel.</summary>
public sealed class ResultadoAutorizacaoHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _padrao = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult resultado)
    {
        if (!resultado.Forbidden)
        {
            await _padrao.HandleAsync(next, context, policy, resultado);
            return;
        }

        var naoEncontrado = resultado.AuthorizationFailure?.FailureReasons
            .Any(r => r.Message == PapelNoEventoHandler.MotivoNaoEncontrado) == true;

        if (naoEncontrado)
        {
            await RespostaProblema.EscreverAsync(context, StatusCodes.Status404NotFound,
                "Evento não encontrado", "O evento não existe ou você não tem acesso a ele.", "EVENTO_NAO_ENCONTRADO");
            return;
        }

        await RespostaProblema.EscreverAsync(context, StatusCodes.Status403Forbidden,
            "Acesso negado", "Você não tem o papel necessário neste evento para realizar esta operação.", "ACESSO_NEGADO");
    }
}
