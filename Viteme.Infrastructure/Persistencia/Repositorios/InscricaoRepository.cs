using Microsoft.EntityFrameworkCore;
using Viteme.Application.DTOs.Inscricoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class InscricaoRepository(VitemeDbContext contexto) : IInscricaoRepository
{
    public Task<Inscricao?> ObterPorIdAsync(Guid inscricaoId, CancellationToken cancellationToken = default) =>
        contexto.Inscricoes.FirstOrDefaultAsync(i => i.Id == inscricaoId, cancellationToken);

    public Task<Inscricao?> ObterAtivaAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.Inscricoes.FirstOrDefaultAsync(
            i => i.EventoId == eventoId && i.UsuarioId == usuarioId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento),
            cancellationToken);

    public Task<Inscricao?> ObterPorCodigoAsync(Guid eventoId, string codigoCheckin, CancellationToken cancellationToken = default) =>
        contexto.Inscricoes.FirstOrDefaultAsync(i => i.EventoId == eventoId && i.CodigoCheckin == codigoCheckin, cancellationToken);

    public async Task<IReadOnlyList<Inscricao>> ListarAtivasAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        await contexto.Inscricoes
            .Where(i => i.EventoId == eventoId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListarUsuariosAtivosAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        await contexto.Inscricoes.AsNoTracking()
            .Where(i => i.EventoId == eventoId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento))
            .Select(i => i.UsuarioId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListarUsuariosConfirmadosAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        await contexto.Inscricoes.AsNoTracking()
            .Where(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada)
            .Select(i => i.UsuarioId)
            .ToListAsync(cancellationToken);

    public Task<bool> ExisteAtivaAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        contexto.Inscricoes.AnyAsync(
            i => i.EventoId == eventoId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento),
            cancellationToken);

    public async Task<IReadOnlyList<InscricaoComEvento>> ListarDoUsuarioAsync(
        Guid usuarioId, SituacaoInscricoes situacao, DateTime agora, CancellationToken cancellationToken = default)
    {
        var consulta = from i in contexto.Inscricoes.AsNoTracking()
                       where i.UsuarioId == usuarioId
                       join e in contexto.Eventos.AsNoTracking() on i.EventoId equals e.Id
                       select new { Inscricao = i, Evento = e };

        consulta = situacao switch
        {
            SituacaoInscricoes.Proximas => consulta.Where(x => x.Evento.DataFim >= agora).OrderBy(x => x.Evento.DataInicio),
            SituacaoInscricoes.Passadas => consulta.Where(x => x.Evento.DataFim < agora).OrderByDescending(x => x.Evento.DataInicio),
            _ => consulta.OrderByDescending(x => x.Evento.DataInicio),
        };

        var itens = await consulta.ToListAsync(cancellationToken);
        return itens.Select(x => new InscricaoComEvento(x.Inscricao, x.Evento)).ToList();
    }

    public async Task<(IReadOnlyList<InscritoListado> Itens, int Total)> ListarInscritosAsync(
        Guid eventoId, InscritosFiltro filtro, CancellationToken cancellationToken = default)
    {
        var consulta = from i in contexto.Inscricoes.AsNoTracking()
                       where i.EventoId == eventoId
                       join u in contexto.Usuarios.AsNoTracking() on i.UsuarioId equals u.Id
                       select new { Inscricao = i, Usuario = u };

        if (filtro.StatusFiltro is { } status)
        {
            consulta = consulta.Where(x => x.Inscricao.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var padrao = $"%{filtro.Busca.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            consulta = consulta.Where(x => EF.Functions.ILike(x.Usuario.Nome, padrao) || EF.Functions.ILike(x.Usuario.Email, padrao));
        }

        var total = await consulta.CountAsync(cancellationToken);

        // A ordenação vem antes da projeção para o record (o EF não traduz o contrário).
        var itens = await consulta
            .OrderBy(x => x.Usuario.Nome).ThenBy(x => x.Inscricao.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(x => new
            {
                x.Inscricao.Id, x.Usuario.Nome, x.Usuario.Email, x.Inscricao.Status, x.Inscricao.PresencaConfirmada, x.Inscricao.CheckinEm,
            })
            .ToListAsync(cancellationToken);

        return (itens.Select(x => new InscritoListado(x.Id, x.Nome, x.Email, x.Status, x.PresencaConfirmada, x.CheckinEm)).ToList(), total);
    }

    public async Task<IReadOnlyList<Guid>> ListarIdsComReservaVencidaAsync(DateTime agora, int limite, CancellationToken cancellationToken = default) =>
        await contexto.Inscricoes.AsNoTracking()
            .Where(i => i.Status == StatusInscricao.PendentePagamento && i.ReservadaAte <= agora)
            .OrderBy(i => i.ReservadaAte)
            .Select(i => i.Id)
            .Take(limite)
            .ToListAsync(cancellationToken);

    public void Adicionar(Inscricao inscricao) => contexto.Inscricoes.Add(inscricao);
}
