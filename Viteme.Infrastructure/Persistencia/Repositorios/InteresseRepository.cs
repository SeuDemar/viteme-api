using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class InteresseRepository(VitemeDbContext contexto) : IInteresseRepository
{
    public Task<bool> ExisteAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Interesses.AnyAsync(i => i.EventoId == eventoId && i.UsuarioId == usuarioId, cancellationToken);

    public Task<InteresseEvento?> ObterAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Interesses.FirstOrDefaultAsync(i => i.EventoId == eventoId && i.UsuarioId == usuarioId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListarSemInscricaoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        await contexto.Interesses.AsNoTracking()
            .Where(i => i.EventoId == eventoId
                && !contexto.Inscricoes.Any(x => x.EventoId == eventoId && x.UsuarioId == i.UsuarioId
                    && (x.Status == StatusInscricao.Confirmada || x.Status == StatusInscricao.PendentePagamento)))
            .Select(i => i.UsuarioId)
            .ToListAsync(cancellationToken);

    public void Adicionar(InteresseEvento interesse) => contexto.Interesses.Add(interesse);

    public void Remover(InteresseEvento interesse) => contexto.Interesses.Remove(interesse);
}
