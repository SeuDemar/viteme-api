using Microsoft.AspNetCore.Authorization;
using Viteme.Api.Autenticacao;
using Viteme.Application.Interfaces;

namespace Viteme.Api.Autorizacao;

public sealed class PapelNoEventoRequirement(bool exigeOrganizador) : IAuthorizationRequirement
{
    public bool ExigeOrganizador { get; } = exigeOrganizador;
}

/// <summary>
/// Verifica o papel do usuário no evento da rota. Evento inexistente, ou que o usuário não pode ver
/// (ex.: rascunho de outra pessoa), responde 404 e não revela que existe; os demais casos sem papel respondem 403.
/// </summary>
public sealed class PapelNoEventoHandler(IEventoRepository eventos) : AuthorizationHandler<PapelNoEventoRequirement>
{
    public const string MotivoNaoEncontrado = "EVENTO_NAO_ENCONTRADO";
    public const string MotivoAcessoNegado = "ACESSO_NEGADO";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PapelNoEventoRequirement requisito)
    {
        if (context.Resource is not HttpContext http
            || !Guid.TryParse(http.GetRouteValue("id")?.ToString(), out var eventoId))
        {
            context.Fail(new AuthorizationFailureReason(this, MotivoNaoEncontrado));
            return;
        }

        var permissao = await eventos.ObterPermissaoAsync(eventoId, context.User.ObterUsuarioIdOuNull(), http.RequestAborted);

        if (!permissao.Existe)
        {
            context.Fail(new AuthorizationFailureReason(this, MotivoNaoEncontrado));
            return;
        }

        var atende = requisito.ExigeOrganizador ? permissao.EhOrganizador : permissao.EhOrganizadorOuColaborador;
        if (atende)
        {
            context.Succeed(requisito);
            return;
        }

        context.Fail(new AuthorizationFailureReason(this, permissao.PodeVer ? MotivoAcessoNegado : MotivoNaoEncontrado));
    }
}
