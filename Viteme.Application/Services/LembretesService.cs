using Microsoft.Extensions.Logging;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Services;

/// <summary>
/// RN17: lembretes 24 horas antes do início do evento (aos inscritos confirmados) e 24 horas antes do fim das
/// inscrições (aos interessados que ainda não se inscreveram). Idempotente: cada usuário recebe cada lembrete uma vez.
/// </summary>
public class LembretesService(
    IEventoRepository eventos,
    IInscricaoRepository inscricoes,
    IInteresseRepository interesses,
    INotificacaoRepository notificacoesRepositorio,
    NotificacaoService notificacoes,
    IUnitOfWork unitOfWork,
    IRelogio relogio,
    ILogger<LembretesService> logger)
{
    public const string TituloInicio = "Lembrete: o evento começa em breve";
    public const string TituloFimInscricoes = "Lembrete: as inscrições terminam em breve";

    private static readonly TimeSpan Antecedencia = TimeSpan.FromHours(24);

    /// <summary>Retorna quantos lembretes foram gerados.</summary>
    public async Task<int> EnviarAsync(CancellationToken cancellationToken = default)
    {
        var agora = relogio.UtcAgora;
        var limite = agora + Antecedencia;
        var total = 0;

        foreach (var evento in await eventos.ListarComInicioAteAsync(agora, limite, cancellationToken))
        {
            total += await EnviarDoEventoAsync(
                evento, TituloInicio,
                $"O evento \"{evento.Titulo}\" começa em {Formatacao.HorarioBrasilia(evento.DataInicio)} (horário de Brasília). "
                + "Leve o código de check-in que está em Minhas inscrições.",
                () => inscricoes.ListarUsuariosConfirmadosAsync(evento.Id, cancellationToken), cancellationToken);
        }

        foreach (var evento in await eventos.ListarComFimDeInscricaoAteAsync(agora, limite, cancellationToken))
        {
            total += await EnviarDoEventoAsync(
                evento, TituloFimInscricoes,
                $"As inscrições do evento \"{evento.Titulo}\" terminam em {Formatacao.HorarioBrasilia(evento.InscricaoFim)} "
                + "(horário de Brasília). Garanta sua vaga.",
                () => interesses.ListarSemInscricaoAsync(evento.Id, cancellationToken), cancellationToken);
        }

        return total;
    }

    private async Task<int> EnviarDoEventoAsync(
        Evento evento, string titulo, string mensagem, Func<Task<IReadOnlyList<Guid>>> destinatarios, CancellationToken cancellationToken)
    {
        try
        {
            var jaLembrados = await notificacoesRepositorio.ListarUsuariosComLembreteAsync(evento.Id, titulo, cancellationToken);
            var novos = (await destinatarios()).Except(jaLembrados).ToList();
            if (novos.Count == 0)
            {
                return 0;
            }

            await notificacoes.NotificarAsync(novos, evento.Id, TipoNotificacao.Lembrete, titulo, mensagem, cancellationToken);
            await unitOfWork.SalvarAsync(cancellationToken);
            return novos.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Um evento com problema não pode impedir os lembretes dos demais; ele é tentado de novo no próximo ciclo.
            logger.LogError(ex, "Falha ao gerar o lembrete \"{Titulo}\" do evento {EventoId}.", titulo, evento.Id);
            unitOfWork.LimparRastreamento();
            return 0;
        }
    }
}
