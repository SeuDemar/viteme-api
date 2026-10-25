using System.Security.Claims;
using Viteme.Domain.Excecoes;

namespace Viteme.Api.Autenticacao;

public static class UsuarioAutenticadoExtensions
{
    /// <summary>Id do usuário autenticado (claim <c>sub</c>). Recursos pessoais são sempre filtrados por ele.</summary>
    public static Guid ObterUsuarioId(this ClaimsPrincipal usuario)
    {
        var sub = usuario.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new RegraDeNegocioException("NAO_AUTENTICADO", "Não autenticado", "Token inválido.", 401);
    }

    /// <summary>Id do usuário se houver token válido; nulo para visitantes (endpoints públicos com dados extras para logados).</summary>
    public static Guid? ObterUsuarioIdOuNull(this ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true ? usuario.ObterUsuarioId() : null;
}
