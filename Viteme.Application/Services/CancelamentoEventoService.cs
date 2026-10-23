using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Services;

/// <summary>
/// RN14: cancela o evento, cancela as inscrições ativas, expira as cobranças pendentes e avisa os envolvidos.
/// Usado pelo cancelamento do organizador (E15) e pelo cancelamento automático por denúncias (RN19).
/// Não confirma a transação nem fala com o gateway: quem chama salva e depois pede os estornos
/// (<see cref="EstornoService.ProcessarPendentesAsync"/>), fora da transação do banco.
/// </summary>
public class CancelamentoEventoService(
    IEventoRepository eventos,
    IInscricaoRepository inscricoes,
    IPagamentoRepository pagamentos,
    NotificacaoService notificacoes)
{
    /// <param name="evento">Evento carregado com rastreamento.</param>
    /// <param name="mensagem">Texto do aviso (já com o motivo).</param>
    /// <param name="autorId">Quem cancelou (não é avisado); nulo no cancelamento automático.</param>
    public async Task CancelarAsync(Evento evento, string mensagem, Guid? autorId, CancellationToken cancellationToken = default)
    {
        evento.Cancelar();

        var destinatarios = (await eventos.ListarDestinatariosAsync(evento.Id, incluirColaboradores: true, cancellationToken))
            .Append(evento.OrganizadorId)
            .Where(id => id != autorId)
            .ToList();

        foreach (var inscricao in await inscricoes.ListarAtivasAsync(evento.Id, cancellationToken))
        {
            inscricao.Cancelar();
        }

        foreach (var pendente in await pagamentos.ListarPendentesDoEventoAsync(evento.Id, cancellationToken))
        {
            pendente.Expirar();
        }

        await notificacoes.NotificarAsync(
            destinatarios, evento.Id, TipoNotificacao.Cancelamento, "Evento cancelado", mensagem, cancellationToken);
    }
}
