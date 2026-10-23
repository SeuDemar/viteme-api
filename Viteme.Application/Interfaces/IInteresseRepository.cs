namespace Viteme.Application.Interfaces;

public interface IInteresseRepository
{
    Task<bool> ExisteAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default);
}
