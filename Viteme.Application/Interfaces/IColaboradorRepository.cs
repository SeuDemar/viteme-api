using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public sealed record ColaboradorListado(Guid UsuarioId, string Nome, string Email, DateTime AdicionadoEm);

public interface IColaboradorRepository
{
    Task<IReadOnlyList<ColaboradorListado>> ListarAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<ColaboradorEvento?> ObterAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default);

    void Adicionar(ColaboradorEvento colaborador);

    void Remover(ColaboradorEvento colaborador);
}
