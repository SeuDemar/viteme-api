using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class CodigoVerificacaoRepository(VitemeDbContext contexto) : ICodigoVerificacaoRepository
{
    public Task<CodigoVerificacao?> ObterUltimoAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.CodigosVerificacao
            .Where(c => c.UsuarioId == usuarioId)
            .OrderByDescending(c => c.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> ContarEnviosParaTelefoneAsync(string telefone, DateTime desde, CancellationToken cancellationToken = default) =>
        (from c in contexto.CodigosVerificacao
         join u in contexto.Usuarios on c.UsuarioId equals u.Id
         where u.Telefone == telefone && c.CriadoEm >= desde
         select c.Id).CountAsync(cancellationToken);

    public Task<CodigoVerificacao?> ObterUltimoPendenteAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.CodigosVerificacao
            .Where(c => c.UsuarioId == usuarioId && !c.Utilizado)
            .OrderByDescending(c => c.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task InvalidarPendentesAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var pendentes = await contexto.CodigosVerificacao
            .Where(c => c.UsuarioId == usuarioId && !c.Utilizado)
            .ToListAsync(cancellationToken);

        foreach (var codigo in pendentes)
        {
            codigo.Invalidar();
        }
    }

    public void Adicionar(CodigoVerificacao codigo) => contexto.CodigosVerificacao.Add(codigo);
}
