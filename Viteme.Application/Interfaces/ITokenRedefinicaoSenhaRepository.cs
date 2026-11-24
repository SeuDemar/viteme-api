using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface ITokenRedefinicaoSenhaRepository
{
    Task<TokenRedefinicaoSenha?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Há pedido do usuário criado depois de <paramref name="desde"/>?</summary>
    Task<bool> ExistePedidoDesdeAsync(Guid usuarioId, DateTime desde, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca o token como usado em uma única instrução condicional (<c>WHERE usado_em IS NULL AND expira_em &gt; agora</c>).
    /// Retorna <c>false</c> se outra requisição o usou antes ou se expirou: o link só vale uma vez.
    /// </summary>
    Task<bool> TentarUsarAsync(Guid tokenId, DateTime agora, CancellationToken cancellationToken = default);

    /// <summary>Invalida os links ainda pendentes do usuário (um novo pedido ou uma troca de senha anula os anteriores).</summary>
    Task InvalidarPendentesDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken cancellationToken = default);

    void Adicionar(TokenRedefinicaoSenha token);
}
