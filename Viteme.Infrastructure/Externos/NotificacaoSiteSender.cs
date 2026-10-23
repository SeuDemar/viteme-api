using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Externos;

/// <summary>Canal "site": toda notificação é registrada para o sino do usuário (RN16).</summary>
public class NotificacaoSiteSender(INotificacaoRepository notificacoes) : INotificationSender
{
    public Task EnviarAsync(Notificacao notificacao, Usuario destinatario, CancellationToken cancellationToken = default)
    {
        notificacoes.Adicionar(notificacao);
        return Task.CompletedTask;
    }
}
