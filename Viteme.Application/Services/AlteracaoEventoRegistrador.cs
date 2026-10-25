using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Services;

public sealed record MudancaEvento(string Campo, string? ValorAnterior, string? ValorNovo);

/// <summary>RN13: registra o histórico de alterações de um evento publicado e avisa inscritos ativos e interessados.</summary>
public class AlteracaoEventoRegistrador(IEventoRepository eventos, NotificacaoService notificacoes)
{
    public async Task RegistrarAsync(
        Evento evento, Guid autorId, IReadOnlyList<MudancaEvento> mudancas, string motivo, CancellationToken cancellationToken = default)
    {
        if (mudancas.Count == 0)
        {
            return;
        }

        foreach (var mudanca in mudancas)
        {
            eventos.Adicionar(new AlteracaoEvento(evento.Id, autorId, mudanca.Campo, mudanca.ValorAnterior, mudanca.ValorNovo, motivo));
        }

        var destinatarios = (await eventos.ListarDestinatariosAsync(evento.Id, incluirColaboradores: false, cancellationToken))
            .Where(id => id != autorId);

        var campos = string.Join(", ", mudancas.Select(m => m.Campo).Distinct());
        await notificacoes.NotificarAsync(
            destinatarios, evento.Id, TipoNotificacao.Alteracao,
            "Evento alterado",
            $"O evento \"{evento.Titulo}\" foi alterado ({campos}). Motivo: {motivo}",
            cancellationToken);
    }

    public static string Formatar(DateTime data) => data.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
