using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

/// <summary>
/// Canal de notificação. Um novo canal (e-mail, WhatsApp) é só mais uma implementação.
/// Os senders apenas preparam o envio; quem chama confirma tudo em uma transação.
/// </summary>
public interface INotificationSender
{
    Task EnviarAsync(Notificacao notificacao, CancellationToken cancellationToken = default);
}
