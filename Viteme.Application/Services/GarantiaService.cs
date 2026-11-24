using Microsoft.Extensions.Logging;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Application.Services;

/// <summary>
/// Garantia (Conta Escrow, Fase 3 do Asaas): o valor pago fica retido na subconta do organizador e só é liberado depois do
/// evento (RN26). Desligada pela configuração, nada aqui chama o gateway.
/// </summary>
public class GarantiaService(
    IPagamentoRepository pagamentos,
    IEventoRepository eventos,
    IContaRecebimentoRepository contas,
    IContasRecebimento gateway,
    ICifradorSegredos cifrador,
    IUnitOfWork unitOfWork,
    IRelogio relogio,
    ILogger<GarantiaService> logger)
{
    /// <summary>RN26: a garantia é liberada 2 dias depois do término do evento (prazo para reclamações e estornos).</summary>
    public static readonly TimeSpan PrazoLiberacao = TimeSpan.FromDays(2);

    /// <summary>
    /// Na aprovação do pagamento: guarda o id da garantia. Melhor esforço: uma falha só é registrada no log, porque o
    /// pagamento já está confirmado e a tarefa de liberação busca o id depois.
    /// </summary>
    public async Task TentarRegistrarAsync(Pagamento pagamento, Guid eventoId, CancellationToken cancellationToken = default)
    {
        if (!gateway.GarantiaHabilitada || pagamento.GarantiaId is not null || pagamento.IdTransacaoGateway is null)
        {
            return;
        }

        try
        {
            var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken);
            var conta = evento is null ? null : await contas.ObterPorUsuarioAsync(evento.OrganizadorId, cancellationToken);
            if (conta?.GarantiaHabilitadaEm is null)
            {
                return;
            }

            var garantiaId = await gateway.ObterGarantiaAsync(pagamento.IdTransacaoGateway, cifrador.Decifrar(conta.ChaveApiCifrada), cancellationToken);
            if (garantiaId is not null)
            {
                pagamento.RegistrarGarantia(garantiaId);
                await unitOfWork.SalvarAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha ao registrar a garantia do pagamento {PagamentoId}; a tarefa de liberação tenta depois.", pagamento.Id);
            unitOfWork.LimparRastreamento();
        }
    }

    /// <summary>
    /// Tarefa periódica (RN26): libera as garantias dos pagamentos de eventos encerrados há mais de
    /// <see cref="PrazoLiberacao"/>. Pagamento sem garantia (anterior à Conta Escrow) sai da fila sem chamar o gateway.
    /// Cada pagamento é gravado à parte: uma falha não impede os outros e é repetida no próximo ciclo.
    /// </summary>
    public async Task<int> LiberarVencidasAsync(int limite, CancellationToken cancellationToken = default)
    {
        if (!gateway.GarantiaHabilitada)
        {
            return 0;
        }

        var agora = relogio.UtcAgora;
        var liberadas = 0;
        foreach (var item in await pagamentos.ListarGarantiasParaLiberarAsync(agora - PrazoLiberacao, limite, cancellationToken))
        {
            try
            {
                var pagamento = await pagamentos.ObterPorIdAsync(item.PagamentoId, cancellationToken);
                if (pagamento is null)
                {
                    continue;
                }

                var conta = await contas.ObterPorUsuarioAsync(item.OrganizadorId, cancellationToken);
                if (conta?.GarantiaHabilitadaEm is not { } habilitadaEm || (item.GarantiaId is null && item.CriadoEm < habilitadaEm))
                {
                    pagamento.DispensarGarantia(agora);
                    await unitOfWork.SalvarAsync(cancellationToken);
                    continue;
                }

                var chave = cifrador.Decifrar(conta.ChaveApiCifrada);
                var garantiaId = item.GarantiaId ?? await gateway.ObterGarantiaAsync(item.IdTransacao, chave, cancellationToken);
                if (garantiaId is null)
                {
                    // O gateway não reteve esta cobrança: o valor já está com o organizador.
                    pagamento.DispensarGarantia(agora);
                    await unitOfWork.SalvarAsync(cancellationToken);
                    continue;
                }

                pagamento.RegistrarGarantia(garantiaId);
                await gateway.EncerrarGarantiaAsync(item.IdTransacao, garantiaId, chave, cancellationToken);
                pagamento.LiberarGarantia(agora);
                await unitOfWork.SalvarAsync(cancellationToken);
                liberadas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha ao liberar a garantia do pagamento {PagamentoId}; tenta de novo no próximo ciclo.", item.PagamentoId);
                unitOfWork.LimparRastreamento();
            }
        }

        return liberadas;
    }
}
