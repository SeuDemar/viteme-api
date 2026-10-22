using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface ITokenRenovacaoRepository
{
    Task<TokenRenovacao?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Revoga todos os tokens ativos do usuário (reuso de token revogado indica roubo).</summary>
    Task RevogarTodosDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken cancellationToken = default);

    void Adicionar(TokenRenovacao token);
}
