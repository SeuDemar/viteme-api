using Viteme.Application.DTOs.Inscricoes;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Interfaces;

public sealed record InscricaoComEvento(Inscricao Inscricao, Evento Evento);

public sealed record InscritoListado(
    Guid InscricaoId, string Nome, string Email, StatusInscricao Status, bool PresencaConfirmada, DateTime? CheckinEm);

public interface IInscricaoRepository
{
    Task<Inscricao?> ObterPorIdAsync(Guid inscricaoId, CancellationToken cancellationToken = default);

    /// <summary>Inscrição ativa (confirmada ou pendente de pagamento) do usuário no evento.</summary>
    Task<Inscricao?> ObterAtivaAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Busca pelo código de check-in dentro do evento (com rastreamento).</summary>
    Task<Inscricao?> ObterPorCodigoAsync(Guid eventoId, string codigoCheckin, CancellationToken cancellationToken = default);

    /// <summary>Todas as inscrições ativas do evento, com rastreamento (cancelamento do evento, RN14).</summary>
    Task<IReadOnlyList<Inscricao>> ListarAtivasAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<bool> ExisteAtivaAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InscricaoComEvento>> ListarDoUsuarioAsync(
        Guid usuarioId, SituacaoInscricoes situacao, DateTime agora, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<InscritoListado> Itens, int Total)> ListarInscritosAsync(
        Guid eventoId, InscritosFiltro filtro, CancellationToken cancellationToken = default);

    /// <summary>Ids das inscrições pendentes cuja reserva venceu (RN11).</summary>
    Task<IReadOnlyList<Guid>> ListarIdsComReservaVencidaAsync(DateTime agora, int limite, CancellationToken cancellationToken = default);

    void Adicionar(Inscricao inscricao);
}
