using Viteme.Application.DTOs.Notificacoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Sino de notificações do usuário (E34–E36). Sempre filtrado pelo usuário do token.</summary>
public class CaixaNotificacoesService(INotificacaoRepository notificacoes, IUnitOfWork unitOfWork)
{
    public async Task<NotificacoesPaginaResponse> ListarAsync(
        Guid usuarioId, NotificacoesFiltro filtro, CancellationToken cancellationToken = default)
    {
        var (itens, total) = await notificacoes.ListarAsync(usuarioId, filtro, cancellationToken);
        var naoLidas = await notificacoes.ContarNaoLidasAsync(usuarioId, cancellationToken);

        return new NotificacoesPaginaResponse(
            itens.Select(n => new NotificacaoResponse(n.Id, n.Tipo, n.Titulo, n.Mensagem, n.EventoId, n.Lida, n.CriadoEm)).ToList(),
            naoLidas, filtro.Pagina, total);
    }

    /// <summary>Notificação inexistente ou de outro usuário responde 404, sem revelar a diferença.</summary>
    public async Task MarcarComoLidaAsync(Guid notificacaoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var notificacao = await notificacoes.ObterDoUsuarioAsync(notificacaoId, usuarioId, cancellationToken)
            ?? throw new RegraDeNegocioException("NOTIFICACAO_NAO_ENCONTRADA", "Notificação não encontrada",
                "A notificação não existe.", 404);

        notificacao.MarcarComoLida();
        await unitOfWork.SalvarAsync(cancellationToken);
    }

    public Task<int> MarcarTodasComoLidasAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        notificacoes.MarcarTodasComoLidasAsync(usuarioId, cancellationToken);
}
