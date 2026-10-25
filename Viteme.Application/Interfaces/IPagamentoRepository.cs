using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

/// <summary>Pagamento aprovado cuja inscrição não está confirmada: o valor precisa ser devolvido (RN14, RN15, RN23).</summary>
public sealed record PagamentoParaEstorno(Guid PagamentoId, Guid UsuarioId, Guid EventoId);

public interface IPagamentoRepository
{
    Task<Pagamento?> ObterPorIdAsync(Guid pagamentoId, CancellationToken cancellationToken = default);

    Task<Pagamento?> ObterPorIdTransacaoAsync(string idTransacao, CancellationToken cancellationToken = default);

    /// <summary>Pagamentos da inscrição, do mais recente para o mais antigo (com rastreamento).</summary>
    Task<IReadOnlyList<Pagamento>> ListarDaInscricaoAsync(Guid inscricaoId, CancellationToken cancellationToken = default);

    /// <summary>Pagamentos ainda pendentes das inscrições do evento (com rastreamento).</summary>
    Task<IReadOnlyList<Pagamento>> ListarPendentesDoEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);

    /// <summary>Ids dos pagamentos aprovados com inscrição não confirmada, opcionalmente de um só evento.</summary>
    Task<IReadOnlyList<PagamentoParaEstorno>> ListarParaEstornoAsync(
        Guid? eventoId, int limite, CancellationToken cancellationToken = default);

    void Adicionar(Pagamento pagamento);
}
