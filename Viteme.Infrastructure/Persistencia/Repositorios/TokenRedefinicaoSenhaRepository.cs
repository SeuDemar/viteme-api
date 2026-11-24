using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class TokenRedefinicaoSenhaRepository(VitemeDbContext contexto) : ITokenRedefinicaoSenhaRepository
{
    public Task<TokenRedefinicaoSenha?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        contexto.TokensRedefinicaoSenha.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<bool> ExistePedidoDesdeAsync(Guid usuarioId, DateTime desde, CancellationToken cancellationToken = default) =>
        contexto.TokensRedefinicaoSenha.AnyAsync(t => t.UsuarioId == usuarioId && t.CriadoEm > desde, cancellationToken);

    public async Task<bool> TentarUsarAsync(Guid tokenId, DateTime agora, CancellationToken cancellationToken = default) =>
        await contexto.TokensRedefinicaoSenha
            .Where(t => t.Id == tokenId && t.UsadoEm == null && t.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora), cancellationToken) == 1;

    public Task InvalidarPendentesDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken cancellationToken = default) =>
        contexto.TokensRedefinicaoSenha
            .Where(t => t.UsuarioId == usuarioId && t.UsadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora), cancellationToken);

    public void Adicionar(TokenRedefinicaoSenha token) => contexto.TokensRedefinicaoSenha.Add(token);
}
