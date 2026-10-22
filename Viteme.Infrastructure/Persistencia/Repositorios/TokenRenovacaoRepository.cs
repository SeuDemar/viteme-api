using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class TokenRenovacaoRepository(VitemeDbContext contexto) : ITokenRenovacaoRepository
{
    public Task<TokenRenovacao?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        contexto.TokensRenovacao.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task RevogarTodosDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken cancellationToken = default)
    {
        var ativos = await contexto.TokensRenovacao
            .Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null)
            .ToListAsync(cancellationToken);

        foreach (var token in ativos)
        {
            token.Revogar(agora);
        }
    }

    public void Adicionar(TokenRenovacao token) => contexto.TokensRenovacao.Add(token);
}
