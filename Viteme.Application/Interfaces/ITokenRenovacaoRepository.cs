using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface ITokenRenovacaoRepository
{
    Task<TokenRenovacao?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Revoga todos os tokens ativos do usuário (reuso de token revogado indica roubo).</summary>
    Task RevogarTodosDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoga o token em uma única instrução condicional (<c>WHERE revogado_em IS NULL</c>). Retorna <c>false</c> se outra
    /// requisição o revogou antes: só uma de duas renovações simultâneas com o mesmo token consegue seguir.
    /// </summary>
    Task<bool> TentarRevogarAsync(Guid tokenId, DateTime agora, CancellationToken cancellationToken = default);

    void Adicionar(TokenRenovacao token);
}
