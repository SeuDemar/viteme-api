using Microsoft.EntityFrameworkCore;
using Viteme.Application.DTOs.Eventos;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class EventoRepository(VitemeDbContext contexto) : IEventoRepository
{
    private static readonly TimeSpan JanelaAlteracaoRecente = TimeSpan.FromHours(72);

    public Task<Evento?> ObterPorIdAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        contexto.Eventos.FirstOrDefaultAsync(e => e.Id == eventoId, cancellationToken);

    // SELECT ... FOR UPDATE: a linha fica bloqueada até o fim da transação. "e.xmin" é a coluna de sistema usada como versão.
    public async Task<Evento?> ObterBloqueadoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        (await contexto.Eventos
            .FromSqlInterpolated($"SELECT e.*, e.xmin FROM evento e WHERE e.id = {eventoId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken))
        .FirstOrDefault();

    public Task<Evento?> ObterComAtividadesAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        contexto.Eventos.Include(e => e.Atividades).FirstOrDefaultAsync(e => e.Id == eventoId, cancellationToken);

    public async Task<PermissaoEvento> ObterPermissaoAsync(Guid eventoId, Guid? usuarioId, CancellationToken cancellationToken = default)
    {
        var evento = await contexto.Eventos.AsNoTracking()
            .Where(e => e.Id == eventoId)
            .Select(e => new { e.Status, e.OrganizadorId })
            .FirstOrDefaultAsync(cancellationToken);

        if (evento is null)
        {
            return PermissaoEvento.Inexistente;
        }

        if (usuarioId is not { } uid)
        {
            return new PermissaoEvento(true, evento.Status, PapelEvento.Participante, false);
        }

        if (evento.OrganizadorId == uid)
        {
            return new PermissaoEvento(true, evento.Status, PapelEvento.Organizador, false);
        }

        var colaborador = await contexto.Colaboradores.AsNoTracking()
            .AnyAsync(c => c.EventoId == eventoId && c.UsuarioId == uid, cancellationToken);
        if (colaborador)
        {
            return new PermissaoEvento(true, evento.Status, PapelEvento.Colaborador, false);
        }

        var inscrito = await contexto.Inscricoes.AsNoTracking().AnyAsync(
            i => i.EventoId == eventoId && i.UsuarioId == uid
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento),
            cancellationToken);

        return new PermissaoEvento(true, evento.Status, PapelEvento.Participante, inscrito);
    }

    public async Task<(IReadOnlyList<EventoListado> Itens, int Total)> ListarMuralAsync(
        MuralFiltro filtro, DateTime agora, CancellationToken cancellationToken = default)
    {
        var consulta = contexto.Eventos.AsNoTracking()
            .Where(e => e.Status == StatusEvento.Publicado && e.DataFim > agora);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var padrao = $"%{EscaparLike(filtro.Busca.Trim())}%";
            consulta = consulta.Where(e =>
                EF.Functions.ILike(e.Titulo, padrao) || EF.Functions.ILike(e.Descricao, padrao) || EF.Functions.ILike(e.Local, padrao));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Categoria))
        {
            var categoria = filtro.Categoria.Trim().ToLower();
            consulta = consulta.Where(e => e.Categoria.ToLower() == categoria);
        }

        if (filtro.DataDe is { } de)
        {
            var inicio = DateTime.SpecifyKind(de.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            consulta = consulta.Where(e => e.DataInicio >= inicio);
        }

        if (filtro.DataAte is { } ate)
        {
            var fimExclusivo = DateTime.SpecifyKind(ate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            consulta = consulta.Where(e => e.DataInicio < fimExclusivo);
        }

        if (filtro.Gratuito is { } gratuito)
        {
            consulta = gratuito ? consulta.Where(e => e.Preco == 0) : consulta.Where(e => e.Preco > 0);
        }

        var total = await consulta.CountAsync(cancellationToken);
        var limiteRecente = agora - JanelaAlteracaoRecente;

        var itens = await consulta
            .OrderBy(e => e.DataInicio).ThenBy(e => e.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(e => new
            {
                Evento = e,
                Ocupadas = contexto.Inscricoes.Count(i => i.EventoId == e.Id
                    && (i.Status == StatusInscricao.Confirmada
                        || (i.Status == StatusInscricao.PendentePagamento && i.ReservadaAte > agora))),
                Alterado = contexto.Alteracoes.Any(a => a.EventoId == e.Id && a.CriadoEm >= limiteRecente),
            })
            .ToListAsync(cancellationToken);

        return (itens.Select(i => new EventoListado(i.Evento, i.Ocupadas, i.Alterado)).ToList(), total);
    }

    public async Task<IReadOnlyList<EventoDoUsuario>> ListarDoUsuarioAsync(
        Guid usuarioId, PapelEvento? papel, StatusEvento? status, CancellationToken cancellationToken = default)
    {
        var resultado = new List<EventoDoUsuario>();

        if (papel is null or PapelEvento.Organizador)
        {
            var consulta = contexto.Eventos.AsNoTracking().Where(e => e.OrganizadorId == usuarioId);
            if (status is { } s)
            {
                consulta = consulta.Where(e => e.Status == s);
            }

            resultado.AddRange((await consulta.ToListAsync(cancellationToken)).Select(e => new EventoDoUsuario(e, PapelEvento.Organizador)));
        }

        if (papel is null or PapelEvento.Colaborador)
        {
            var consulta = contexto.Colaboradores.AsNoTracking()
                .Where(c => c.UsuarioId == usuarioId)
                .Join(contexto.Eventos.AsNoTracking(), c => c.EventoId, e => e.Id, (_, e) => e);
            if (status is { } s)
            {
                consulta = consulta.Where(e => e.Status == s);
            }

            resultado.AddRange((await consulta.ToListAsync(cancellationToken)).Select(e => new EventoDoUsuario(e, PapelEvento.Colaborador)));
        }

        return resultado.OrderByDescending(r => r.Evento.DataInicio).ToList();
    }

    public Task<int> ContarVagasOcupadasAsync(Guid eventoId, DateTime agora, CancellationToken cancellationToken = default) =>
        contexto.Inscricoes.CountAsync(
            i => i.EventoId == eventoId
                && (i.Status == StatusInscricao.Confirmada
                    || (i.Status == StatusInscricao.PendentePagamento && i.ReservadaAte > agora)),
            cancellationToken);

    public async Task<IReadOnlyList<AlteracaoEvento>> ListarAlteracoesAsync(
        Guid eventoId, int? limite, CancellationToken cancellationToken = default)
    {
        var consulta = contexto.Alteracoes.AsNoTracking()
            .Where(a => a.EventoId == eventoId)
            .OrderByDescending(a => a.CriadoEm);

        return limite is { } n
            ? await consulta.Take(n).ToListAsync(cancellationToken)
            : await consulta.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListarDestinatariosAsync(
        Guid eventoId, bool incluirColaboradores, CancellationToken cancellationToken = default)
    {
        var destinatarios = contexto.Inscricoes
            .Where(i => i.EventoId == eventoId
                && (i.Status == StatusInscricao.Confirmada || i.Status == StatusInscricao.PendentePagamento))
            .Select(i => i.UsuarioId)
            .Union(contexto.Interesses.Where(i => i.EventoId == eventoId).Select(i => i.UsuarioId));

        if (incluirColaboradores)
        {
            destinatarios = destinatarios.Union(
                contexto.Colaboradores.Where(c => c.EventoId == eventoId).Select(c => c.UsuarioId));
        }

        return await destinatarios.ToListAsync(cancellationToken);
    }

    public Task<AtividadeProgramacao?> ObterAtividadeAsync(Guid eventoId, Guid atividadeId, CancellationToken cancellationToken = default) =>
        contexto.Atividades.FirstOrDefaultAsync(a => a.EventoId == eventoId && a.Id == atividadeId, cancellationToken);

    public async Task<IReadOnlyList<Evento>> ListarVencidosParaEncerrarAsync(DateTime agora, CancellationToken cancellationToken = default) =>
        await contexto.Eventos
            .Where(e => e.Status == StatusEvento.Publicado && e.DataFim <= agora)
            .ToListAsync(cancellationToken);

    public void DefinirVersaoEsperada(Evento evento, uint versao) =>
        contexto.Entry(evento).Property(e => e.Versao).OriginalValue = versao;

    public void Adicionar(Evento evento) => contexto.Eventos.Add(evento);

    public void Adicionar(AlteracaoEvento alteracao) => contexto.Alteracoes.Add(alteracao);

    public void Adicionar(AtividadeProgramacao atividade) => contexto.Atividades.Add(atividade);

    public void Remover(AtividadeProgramacao atividade) => contexto.Atividades.Remove(atividade);

    private static string EscaparLike(string texto) =>
        texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<IReadOnlyList<Evento>> ListarComInicioAteAsync(DateTime agora, DateTime limite, CancellationToken cancellationToken = default) =>
        await contexto.Eventos.AsNoTracking()
            .Where(e => e.Status == StatusEvento.Publicado && e.DataInicio > agora && e.DataInicio <= limite)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Evento>> ListarComFimDeInscricaoAteAsync(DateTime agora, DateTime limite, CancellationToken cancellationToken = default) =>
        await contexto.Eventos.AsNoTracking()
            .Where(e => e.Status == StatusEvento.Publicado && e.InscricaoFim > agora && e.InscricaoFim <= limite)
            .ToListAsync(cancellationToken);
}
