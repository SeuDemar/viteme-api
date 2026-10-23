using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class InteresseRepository(VitemeDbContext contexto) : IInteresseRepository
{
    public Task<bool> ExisteAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Interesses.AnyAsync(i => i.EventoId == eventoId && i.UsuarioId == usuarioId, cancellationToken);
}
