using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface IDenunciaRepository
{
    Task<bool> ExisteAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Denúncias do evento; com <paramref name="aPartirDe"/>, só as feitas depois da republicação (RN19).</summary>
    Task<int> ContarAsync(Guid eventoId, DateTime? aPartirDe, CancellationToken cancellationToken = default);

    void Adicionar(Denuncia denuncia);
}
