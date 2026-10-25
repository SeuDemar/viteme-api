using Microsoft.EntityFrameworkCore;
using Viteme.Application.DTOs.Notificacoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class NotificacaoRepository(VitemeDbContext contexto) : INotificacaoRepository
{
    public void Adicionar(Notificacao notificacao) => contexto.Notificacoes.Add(notificacao);

    public async Task<(IReadOnlyList<Notificacao> Itens, int Total)> ListarAsync(
        Guid usuarioId, NotificacoesFiltro filtro, CancellationToken cancellationToken = default)
    {
        var consulta = contexto.Notificacoes.AsNoTracking().Where(n => n.UsuarioId == usuarioId);
        if (filtro.Lidas is { } lidas)
        {
            consulta = consulta.Where(n => n.Lida == lidas);
        }

        var total = await consulta.CountAsync(cancellationToken);
        var itens = await consulta
            .OrderByDescending(n => n.CriadoEm).ThenBy(n => n.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .ToListAsync(cancellationToken);

        return (itens, total);
    }

    public Task<int> ContarNaoLidasAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Notificacoes.CountAsync(n => n.UsuarioId == usuarioId && !n.Lida, cancellationToken);

    public Task<Notificacao?> ObterDoUsuarioAsync(Guid notificacaoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Notificacoes.FirstOrDefaultAsync(n => n.Id == notificacaoId && n.UsuarioId == usuarioId, cancellationToken);

    public Task<int> MarcarTodasComoLidasAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Notificacoes
            .Where(n => n.UsuarioId == usuarioId && !n.Lida)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Lida, true), cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListarUsuariosComLembreteAsync(
        Guid eventoId, string titulo, CancellationToken cancellationToken = default) =>
        await contexto.Notificacoes.AsNoTracking()
            .Where(n => n.EventoId == eventoId && n.Tipo == TipoNotificacao.Lembrete && n.Titulo == titulo)
            .Select(n => n.UsuarioId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
