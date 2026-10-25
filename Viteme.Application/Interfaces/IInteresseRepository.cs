using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface IInteresseRepository
{
    Task<bool> ExisteAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default);

    Task<InteresseEvento?> ObterAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Interessados que ainda não têm inscrição ativa no evento (lembrete do fim das inscrições, RN17).</summary>
    Task<IReadOnlyList<Guid>> ListarSemInscricaoAsync(Guid eventoId, CancellationToken cancellationToken = default);

    void Adicionar(InteresseEvento interesse);

    void Remover(InteresseEvento interesse);
}
