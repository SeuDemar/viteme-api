using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Externos;

/// <summary>
/// Canal SMS (RN16): só registra o envio, na mesma transação da notificação, e apenas para quem verificou o telefone
/// e aceitou receber SMS. A entrega acontece depois, por tarefa, para uma falha do provedor não afetar a operação.
/// </summary>
public class NotificacaoSmsSender : INotificationSender
{
    public Task EnviarAsync(Notificacao notificacao, Usuario destinatario, CancellationToken cancellationToken = default)
    {
        if (destinatario.PodeReceberSms && destinatario.Telefone is { } telefone)
        {
            notificacao.AgendarSms(telefone);
        }

        return Task.CompletedTask;
    }
}
