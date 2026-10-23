using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class ColaboradorRepository(VitemeDbContext contexto) : IColaboradorRepository
{
    // A ordenação vem antes da projeção: o EF não traduz OrderBy sobre um record criado por construtor.
    public async Task<IReadOnlyList<ColaboradorListado>> ListarAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        await (from c in contexto.Colaboradores.AsNoTracking()
               where c.EventoId == eventoId
               join u in contexto.Usuarios.AsNoTracking() on c.UsuarioId equals u.Id
               orderby c.AdicionadoEm
               select new ColaboradorListado(u.Id, u.Nome, u.Email, c.AdicionadoEm))
            .ToListAsync(cancellationToken);

    public Task<ColaboradorEvento?> ObterAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Colaboradores.FirstOrDefaultAsync(c => c.EventoId == eventoId && c.UsuarioId == usuarioId, cancellationToken);

    public void Adicionar(ColaboradorEvento colaborador) => contexto.Colaboradores.Add(colaborador);

    public void Remover(ColaboradorEvento colaborador) => contexto.Colaboradores.Remove(colaborador);
}
