using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class InscricaoRepository(VitemeDbContext contexto) : IInscricaoRepository
{
    public Task<Inscricao?> ObterAtivaAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Inscricoes.FirstOrDefaultAsync(
            i => i.EventoId == eventoId && i.UsuarioId == usuarioId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento),
            cancellationToken);

    public async Task<IReadOnlyList<Inscricao>> ListarAtivasAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        await contexto.Inscricoes
            .Where(i => i.EventoId == eventoId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento))
            .ToListAsync(cancellationToken);

    public Task<bool> ExisteAtivaAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        contexto.Inscricoes.AnyAsync(
            i => i.EventoId == eventoId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento),
            cancellationToken);
}
