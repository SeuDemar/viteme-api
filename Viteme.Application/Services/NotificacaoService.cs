using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Services;

/// <summary>
/// Decide os canais de cada notificação (RN16). Hoje só o site; o canal SMS entra como mais um
/// <see cref="INotificationSender"/>, sem alterar este serviço. Quem chama confirma tudo na mesma transação.
/// </summary>
public class NotificacaoService(IEnumerable<INotificationSender> canais)
{
    private const int MaxTitulo = 150;
    private const int MaxMensagem = 1000;

    public async Task NotificarAsync(
        IEnumerable<Guid> usuarioIds, Guid? eventoId, TipoNotificacao tipo, string titulo, string mensagem,
        CancellationToken cancellationToken = default)
    {
        foreach (var usuarioId in usuarioIds.Distinct())
        {
            var notificacao = new Notificacao(usuarioId, eventoId, tipo, Truncar(titulo, MaxTitulo), Truncar(mensagem, MaxMensagem));
            foreach (var canal in canais)
            {
                await canal.EnviarAsync(notificacao, cancellationToken);
            }
        }
    }

    private static string Truncar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";
}
