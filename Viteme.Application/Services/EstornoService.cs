using Microsoft.Extensions.Logging;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Services;

/// <summary>
/// Devolve pagamentos aprovados quando a inscrição não vai se confirmar: cancelamento do evento (RN14),
/// cancelamento pelo participante (RN15) e aprovação tardia (RN23). Se o gateway falhar, o pagamento continua
/// APROVADO e a tarefa periódica tenta de novo.
/// </summary>
public class EstornoService(
    IPagamentoRepository pagamentos,
    IGatewayPagamento gateway,
    NotificacaoService notificacoes,
    IUnitOfWork unitOfWork,
    ILogger<EstornoService> logger)
{
    private const int LoteMaximo = 50;

    /// <summary>
    /// Pede o estorno ao gateway e marca o pagamento como estornado (quem chama confirma a transação).
    /// Retorna falso se o gateway falhou: o pagamento segue APROVADO para nova tentativa.
    /// </summary>
    public async Task<bool> TentarEstornarAsync(
        Pagamento pagamento, Guid usuarioId, Guid eventoId, CancellationToken cancellationToken = default)
    {
        if (pagamento.Status == StatusPagamento.Estornado)
        {
            return true;
        }

        if (pagamento.Status != StatusPagamento.Aprovado)
        {
            return false;
        }

        if (string.IsNullOrEmpty(pagamento.IdTransacaoGateway))
        {
            logger.LogWarning("Pagamento {PagamentoId} aprovado sem id de transação; estorno impossível.", pagamento.Id);
            return false;
        }

        try
        {
            await gateway.SolicitarEstornoAsync(pagamento.IdTransacaoGateway, pagamento.Valor, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Gateway recusou o estorno do pagamento {PagamentoId}; nova tentativa no próximo ciclo.", pagamento.Id);
            return false;
        }

        pagamento.Estornar();
        await notificacoes.NotificarAsync(
            [usuarioId], eventoId, TipoNotificacao.Pagamento,
            "Estorno solicitado",
            $"O estorno de {Dinheiro.Formatar(pagamento.Valor)} foi solicitado e o valor será devolvido pelo meio de pagamento utilizado.",
            cancellationToken);
        return true;
    }

    /// <summary>Tenta estornar todos os pagamentos aprovados cuja inscrição não está confirmada. Retorna quantos foram estornados.</summary>
    public async Task<int> ProcessarPendentesAsync(Guid? eventoId = null, CancellationToken cancellationToken = default)
    {
        var estornados = 0;

        foreach (var alvo in await pagamentos.ListarParaEstornoAsync(eventoId, LoteMaximo, cancellationToken))
        {
            try
            {
                var pagamento = await pagamentos.ObterPorIdAsync(alvo.PagamentoId, cancellationToken);
                if (pagamento is not null && await TentarEstornarAsync(pagamento, alvo.UsuarioId, alvo.EventoId, cancellationToken))
                {
                    await unitOfWork.SalvarAsync(cancellationToken);
                    estornados++;
                }
            }
            catch (ConflitoDeConcorrenciaException)
            {
                // Outra operação alterou o pagamento; ele é reavaliado no próximo ciclo.
                unitOfWork.LimparRastreamento();
            }
        }

        return estornados;
    }
}
