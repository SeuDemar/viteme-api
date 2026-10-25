using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface IEnvioSmsRepository
{
    /// <summary>Ids dos envios pendentes com tentativas restantes, dos mais antigos aos mais novos.</summary>
    Task<IReadOnlyList<Guid>> ListarIdsPendentesAsync(int limite, CancellationToken cancellationToken = default);

    /// <summary>O envio (com rastreamento) e a notificação a que pertence.</summary>
    Task<(EnvioSms Envio, Notificacao Notificacao)?> ObterComNotificacaoAsync(Guid envioId, CancellationToken cancellationToken = default);
}
