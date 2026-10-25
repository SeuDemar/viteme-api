using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Services;

/// <summary>
/// Decide os canais de cada notificação (RN16): toda notificação vai ao sino do site e, se o usuário tiver telefone
/// verificado e aceite de SMS ativo, também por SMS. Um novo canal é só mais um <see cref="INotificationSender"/>.
/// Os canais apenas registram o envio na mesma transação de quem chama; a entrega do SMS acontece depois, por tarefa.
/// </summary>
public class NotificacaoService(IEnumerable<INotificationSender> canais, IUsuarioRepository usuarios)
{
    private const int MaxTitulo = 150;
    private const int MaxMensagem = 1000;

    public async Task NotificarAsync(
        IEnumerable<Guid> usuarioIds, Guid? eventoId, TipoNotificacao tipo, string titulo, string mensagem,
        CancellationToken cancellationToken = default)
    {
        var ids = usuarioIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        // Uma consulta para todos os destinatários (o evento pode ter milhares de inscritos).
        var destinatarios = (await usuarios.ListarPorIdsAsync(ids, cancellationToken)).ToDictionary(u => u.Id);

        foreach (var id in ids)
        {
            if (!destinatarios.TryGetValue(id, out var destinatario))
            {
                continue;
            }

            var notificacao = new Notificacao(id, eventoId, tipo, Truncar(titulo, MaxTitulo), Truncar(mensagem, MaxMensagem));
            foreach (var canal in canais)
            {
                await canal.EnviarAsync(notificacao, destinatario, cancellationToken);
            }
        }
    }

    private static string Truncar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";
}
