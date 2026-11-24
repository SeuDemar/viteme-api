using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Interfaces;

/// <summary>Pagamento aprovado cuja inscrição não está confirmada: o valor precisa ser devolvido (RN14, RN15, RN23).</summary>
public sealed record PagamentoParaEstorno(Guid PagamentoId, Guid UsuarioId, Guid EventoId);

/// <summary>Cobrança sem desfecho confirmado no Viteme (pendente, ou expirada há pouco) que vale conferir no gateway.</summary>
public sealed record PagamentoParaReconciliacao(Guid PagamentoId, string IdTransacao, decimal Valor, Guid? ContaRecebimentoId = null);

/// <summary>Pagamento aprovado de evento pago com garantia ainda não liberada (Fase 3, RN26).</summary>
/// <remarks><see cref="ContaRecebimentoId"/> nulo: cobrança da conta raiz, sem garantia.</remarks>
public sealed record PagamentoComGarantia(Guid PagamentoId, string IdTransacao, string? GarantiaId, Guid? ContaRecebimentoId);

/// <summary>Valor e desfecho de um pagamento do evento, para os totais de E47.</summary>
public sealed record PagamentoDoEvento(decimal Valor, StatusPagamento Status, Guid? ContaRecebimentoId, DateTime? GarantiaLiberadaEm);

public interface IPagamentoRepository
{
    Task<Pagamento?> ObterPorIdAsync(Guid pagamentoId, CancellationToken cancellationToken = default);

    Task<Pagamento?> ObterPorIdTransacaoAsync(string idTransacao, CancellationToken cancellationToken = default);

    /// <summary>Pagamentos da inscrição, do mais recente para o mais antigo (com rastreamento).</summary>
    Task<IReadOnlyList<Pagamento>> ListarDaInscricaoAsync(Guid inscricaoId, CancellationToken cancellationToken = default);

    /// <summary>Todos os pagamentos das inscrições do evento, só com valor e desfecho (sem rastreamento).</summary>
    Task<IReadOnlyList<PagamentoDoEvento>> ListarDoEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);

    /// <summary>Pagamentos ainda pendentes das inscrições do evento (com rastreamento).</summary>
    Task<IReadOnlyList<Pagamento>> ListarPendentesDoEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);

    /// <summary>Ids dos pagamentos aprovados com inscrição não confirmada, opcionalmente de um só evento.</summary>
    Task<IReadOnlyList<PagamentoParaEstorno>> ListarParaEstornoAsync(
        Guid? eventoId, int limite, CancellationToken cancellationToken = default);

    /// <summary>Pagamentos pendentes ou expirados, com cobrança no gateway, criados entre as duas datas (sem rastreamento).</summary>
    Task<IReadOnlyList<PagamentoParaReconciliacao>> ListarParaReconciliacaoAsync(
        DateTime criadosAntes, DateTime criadosDepois, int limite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pagamentos aprovados, com inscrição confirmada e garantia não liberada, de eventos encerrados cujo término é anterior a
    /// <paramref name="terminoAntes"/> (sem rastreamento). Inclui os que ainda não têm o id da garantia, para buscá-lo.
    /// </summary>
    Task<IReadOnlyList<PagamentoComGarantia>> ListarGarantiasParaLiberarAsync(
        DateTime terminoAntes, int limite, CancellationToken cancellationToken = default);

    void Adicionar(Pagamento pagamento);
}
