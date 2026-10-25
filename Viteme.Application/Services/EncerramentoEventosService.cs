using Viteme.Application.Interfaces;

namespace Viteme.Application.Services;

/// <summary>RN22: um evento publicado passa automaticamente a encerrado após a data de término. Idempotente.</summary>
public class EncerramentoEventosService(IEventoRepository eventos, IUnitOfWork unitOfWork, IRelogio relogio)
{
    public async Task<int> EncerrarVencidosAsync(CancellationToken cancellationToken = default)
    {
        var agora = relogio.UtcAgora;
        var encerrados = 0;

        foreach (var evento in await eventos.ListarVencidosParaEncerrarAsync(agora, cancellationToken))
        {
            if (evento.Encerrar(agora))
            {
                encerrados++;
            }
        }

        if (encerrados > 0)
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }

        return encerrados;
    }
}
