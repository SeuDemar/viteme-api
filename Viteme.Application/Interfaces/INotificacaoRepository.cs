using Viteme.Application.DTOs.Notificacoes;
using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface INotificacaoRepository
{
    void Adicionar(Notificacao notificacao);

    Task<(IReadOnlyList<Notificacao> Itens, int Total)> ListarAsync(
        Guid usuarioId, NotificacoesFiltro filtro, CancellationToken cancellationToken = default);

    Task<int> ContarNaoLidasAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    Task<Notificacao?> ObterDoUsuarioAsync(Guid notificacaoId, Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Marca todas como lidas em uma só instrução. Retorna quantas mudaram.</summary>
    Task<int> MarcarTodasComoLidasAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Usuários que já receberam o lembrete com este título para o evento (evita repetir, RN17).</summary>
    Task<IReadOnlyList<Guid>> ListarUsuariosComLembreteAsync(Guid eventoId, string titulo, CancellationToken cancellationToken = default);
}
