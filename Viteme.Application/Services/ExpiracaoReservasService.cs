using Microsoft.Extensions.Logging;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;

namespace Viteme.Application.Services;

/// <summary>
/// RN11: reserva vencida torna a inscrição EXPIRADA, expira a cobrança pendente e libera a vaga.
/// Idempotente e seguro com concorrência: cada inscrição é tratada isoladamente e um conflito é reavaliado no ciclo seguinte.
/// </summary>
public class ExpiracaoReservasService(
    IInscricaoRepository inscricoes,
    IPagamentoRepository pagamentos,
    IUnitOfWork unitOfWork,
    IRelogio relogio,
    ILogger<ExpiracaoReservasService> logger)
{
    private const int LoteMaximo = 200;

    public async Task<int> ExpirarVencidasAsync(CancellationToken cancellationToken = default)
    {
        var expiradas = 0;

        foreach (var id in await inscricoes.ListarIdsComReservaVencidaAsync(relogio.UtcAgora, LoteMaximo, cancellationToken))
        {
            try
            {
                var inscricao = await inscricoes.ObterPorIdAsync(id, cancellationToken);
                if (inscricao is null || !inscricao.ReservaVencida(relogio.UtcAgora))
                {
                    continue;
                }

                PagamentoService.ExpirarReserva(inscricao, await pagamentos.ListarDaInscricaoAsync(id, cancellationToken));
                await unitOfWork.SalvarAsync(cancellationToken);
                expiradas++;
            }
            catch (ConflitoDeConcorrenciaException)
            {
                logger.LogInformation("Inscrição {InscricaoId} mudou durante a expiração da reserva; será reavaliada.", id);
                unitOfWork.LimparRastreamento();
            }
        }

        return expiradas;
    }
}
