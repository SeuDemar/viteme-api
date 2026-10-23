using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

/// <summary>
/// Canal de notificação (RN16). Um novo canal (e-mail, WhatsApp) é só mais uma implementação.
/// Os canais apenas registram o envio; quem chama confirma tudo em uma transação, então uma falha na entrega
/// nunca interrompe a operação que originou a notificação.
/// </summary>
public interface INotificationSender
{
    Task EnviarAsync(Notificacao notificacao, Usuario destinatario, CancellationToken cancellationToken = default);
}
