using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class DenunciaRepository(VitemeDbContext contexto) : IDenunciaRepository
{
    public Task<bool> ExisteAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Denuncias.AnyAsync(d => d.EventoId == eventoId && d.UsuarioId == usuarioId, cancellationToken);

    public Task<int> ContarAsync(Guid eventoId, DateTime? aPartirDe, CancellationToken cancellationToken = default) =>
        contexto.Denuncias.CountAsync(
            d => d.EventoId == eventoId && (aPartirDe == null || d.CriadoEm >= aPartirDe), cancellationToken);

    public void Adicionar(Denuncia denuncia) => contexto.Denuncias.Add(denuncia);
}
