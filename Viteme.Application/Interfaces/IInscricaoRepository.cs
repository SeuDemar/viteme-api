using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface IInscricaoRepository
{
    /// <summary>Inscrição ativa (confirmada ou pendente de pagamento) do usuário no evento.</summary>
    Task<Inscricao?> ObterAtivaAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Todas as inscrições ativas do evento, com rastreamento (cancelamento do evento, RN14).</summary>
    Task<IReadOnlyList<Inscricao>> ListarAtivasAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<bool> ExisteAtivaAsync(Guid eventoId, CancellationToken cancellationToken = default);
}
