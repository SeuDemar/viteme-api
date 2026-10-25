using Microsoft.Extensions.Logging;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Application.Services;

/// <summary>
/// RN16: entrega os SMS registrados pelo canal SMS. Uma falha do provedor gera até 3 novas tentativas (4 no total) nos
/// ciclos seguintes e nunca afeta a operação que originou a notificação. Antes de enviar, confere se o usuário ainda
/// tem o aceite ativo e o mesmo telefone verificado.
/// </summary>
public class EnvioSmsService(
    IEnvioSmsRepository envios,
    IUsuarioRepository usuarios,
    IProvedorSms provedor,
    IUnitOfWork unitOfWork,
    IRelogio relogio,
    ILogger<EnvioSmsService> logger)
{
    private const int LoteMaximo = 100;
    private const int MaxCaracteres = 320;

    /// <summary>Retorna quantos SMS foram entregues neste ciclo.</summary>
    public async Task<int> EnviarPendentesAsync(CancellationToken cancellationToken = default)
    {
        var enviados = 0;

        foreach (var id in await envios.ListarIdsPendentesAsync(LoteMaximo, cancellationToken))
        {
            var item = await envios.ObterComNotificacaoAsync(id, cancellationToken);
            if (item is not { } par || !par.Envio.PodeReenviar)
            {
                continue;
            }

            var (envio, notificacao) = par;
            var usuario = await usuarios.ObterPorIdAsync(notificacao.UsuarioId, cancellationToken);

            if (usuario is null || !usuario.PodeReceberSms || usuario.Telefone != envio.Telefone)
            {
                envio.Descartar("O usuário revogou o aceite de SMS ou trocou o telefone.");
            }
            else
            {
                try
                {
                    await provedor.EnviarAsync(envio.Telefone, Montar(notificacao), cancellationToken);
                    envio.RegistrarSucesso(relogio.UtcAgora);
                    enviados++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Falha ao enviar o SMS {EnvioId} (tentativa {Tentativa}).", envio.Id, envio.Tentativas + 1);
                    envio.RegistrarFalha(ex.Message);
                }
            }

            await unitOfWork.SalvarAsync(cancellationToken);
        }

        return enviados;
    }

    private static string Montar(Notificacao notificacao)
    {
        var texto = $"Viteme: {notificacao.Titulo}. {notificacao.Mensagem}";
        return texto.Length <= MaxCaracteres ? texto : texto[..(MaxCaracteres - 1)] + "…";
    }
}
