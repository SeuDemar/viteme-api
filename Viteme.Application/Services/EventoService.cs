using Viteme.Application.DTOs.Comuns;
using Viteme.Application.DTOs.Eventos;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Mural, detalhes, criação, edição, publicação e cancelamento de eventos (E10–E17). Regras RN05–RN07, RN13, RN14.</summary>
public class EventoService(
    IEventoRepository eventos,
    IUsuarioRepository usuarios,
    IInscricaoRepository inscricoes,
    IInteresseRepository interesses,
    IPagamentoRepository pagamentos,
    EstornoService estornos,
    AlteracaoEventoRegistrador registrador,
    NotificacaoService notificacoes,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
{
    private static readonly TimeSpan JanelaAlteracaoRecente = TimeSpan.FromHours(72);

    public async Task<PaginaResponse<EventoResumoResponse>> ListarMuralAsync(MuralFiltro filtro, CancellationToken cancellationToken = default)
    {
        var (itens, total) = await eventos.ListarMuralAsync(filtro, relogio.UtcAgora, cancellationToken);

        var resumos = itens
            .Select(i => new EventoResumoResponse(
                i.Evento.Id, i.Evento.Titulo, i.Evento.Categoria, i.Evento.Local, i.Evento.DataInicio, i.Evento.Preco,
                VagasRestantes(i.Evento, i.VagasOcupadas), i.Evento.ImagemUrl, i.AlteradoRecentemente))
            .ToList();

        return new PaginaResponse<EventoResumoResponse>(resumos, filtro.Pagina, filtro.TamanhoPagina, total);
    }

    /// <summary>E11. Rascunho só aparece para organizador e colaboradores; oculto também para inscritos. Quem não pode ver recebe 404.</summary>
    public async Task<EventoDetalheResultado> ObterDetalheAsync(Guid eventoId, Guid? usuarioId, CancellationToken cancellationToken = default)
    {
        var permissao = await eventos.ObterPermissaoAsync(eventoId, usuarioId, cancellationToken);
        if (!permissao.PodeVer)
        {
            throw EventoNaoEncontrado();
        }

        return await MontarDetalheAsync(eventoId, usuarioId, permissao, cancellationToken);
    }

    public async Task<EventoDetalheResultado> CriarAsync(Guid usuarioId, EventoRequest requisicao, CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(requisicao);
        var evento = new Evento(
            usuarioId, dados.Titulo, dados.Descricao, dados.Categoria, dados.Local, dados.ImagemUrl,
            dados.DataInicio, dados.DataFim, dados.InscricaoInicio, dados.InscricaoFim,
            dados.Capacidade, dados.Preco, dados.CargaHoraria);

        eventos.Adicionar(evento);
        await unitOfWork.SalvarAsync(cancellationToken);

        return await ObterDetalheAsync(evento.Id, usuarioId, cancellationToken);
    }

    /// <summary>
    /// E13. Exige a versão (ETag) lida pelo cliente. Em evento publicado, mudar título, data, horário ou local
    /// exige motivo, gera histórico e avisa inscritos ativos e interessados (RN13).
    /// </summary>
    public async Task<EventoDetalheResultado> AtualizarAsync(
        Guid eventoId, Guid usuarioId, uint? versaoInformada, EventoAtualizarRequest requisicao, CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterComAtividadesAsync(eventoId, cancellationToken) ?? throw EventoNaoEncontrado();
        evento.GarantirNaoFinalizado();

        if (versaoInformada is null)
        {
            throw new RegraDeNegocioException("VERSAO_OBRIGATORIA", "Versão obrigatória",
                "Informe a versão do evento no cabeçalho If-Match (use o ETag recebido ao consultar o evento).", 428);
        }

        if (evento.Versao != versaoInformada)
        {
            throw VersaoDesatualizada();
        }

        eventos.DefinirVersaoEsperada(evento, versaoInformada.Value);

        var agora = relogio.UtcAgora;
        var dados = Normalizar(requisicao);

        if (dados.Preco != evento.Preco && await inscricoes.ExisteAtivaAsync(evento.Id, cancellationToken))
        {
            throw new RegraDeNegocioException("PRECO_BLOQUEADO", "Preço bloqueado",
                "O preço não pode ser alterado enquanto houver inscrições ativas no evento.");
        }

        if (dados.Capacidade is { } capacidade)
        {
            var ocupadas = await eventos.ContarVagasOcupadasAsync(evento.Id, agora, cancellationToken);
            if (capacidade < ocupadas)
            {
                throw new RegraDeNegocioException("CAPACIDADE_ABAIXO_DAS_VAGAS_OCUPADAS", "Capacidade abaixo das vagas ocupadas",
                    $"A capacidade não pode ser menor que as {ocupadas} vagas já ocupadas.");
            }
        }

        // RN05: as atividades da programação devem continuar dentro do período do evento.
        if (evento.Atividades.Any(a => a.Inicio < dados.DataInicio || a.Fim > dados.DataFim))
        {
            throw new RegraDeNegocioException("ATIVIDADE_FORA_DO_PERIODO", "Atividade fora do período",
                "Há atividades da programação fora do novo período do evento. Ajuste a programação antes.");
        }

        var mudancas = evento.Status is StatusEvento.Publicado or StatusEvento.Oculto
            ? DetectarMudancas(evento, dados)
            : [];

        if (mudancas.Count > 0 && string.IsNullOrWhiteSpace(requisicao.MotivoAlteracao))
        {
            throw MotivoObrigatorio();
        }

        evento.AtualizarDados(
            dados.Titulo, dados.Descricao, dados.Categoria, dados.Local, dados.ImagemUrl,
            dados.DataInicio, dados.DataFim, dados.InscricaoInicio, dados.InscricaoFim,
            dados.Capacidade, dados.Preco, dados.CargaHoraria);

        await registrador.RegistrarAsync(evento, usuarioId, mudancas, requisicao.MotivoAlteracao?.Trim() ?? string.Empty, cancellationToken);
        await SalvarComControleDeVersaoAsync(cancellationToken);

        return await ObterDetalheAsync(evento.Id, usuarioId, cancellationToken);
    }

    public async Task<StatusEventoResponse> PublicarAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoNaoEncontrado();

        evento.Publicar(relogio.UtcAgora);
        await unitOfWork.SalvarAsync(cancellationToken);

        return new StatusEventoResponse(evento.Status);
    }

    /// <summary>
    /// E15 (RN14). Cancela o evento e as inscrições ativas, expira as cobranças pendentes, solicita o estorno dos
    /// pagamentos aprovados e avisa os envolvidos. Se o gateway falhar, o estorno fica pendente e a tarefa periódica repete.
    /// </summary>
    public async Task<StatusEventoResponse> CancelarAsync(
        Guid eventoId, Guid usuarioId, CancelarEventoRequest requisicao, CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoNaoEncontrado();

        evento.Cancelar();

        var destinatarios = (await eventos.ListarDestinatariosAsync(evento.Id, incluirColaboradores: true, cancellationToken))
            .Where(id => id != usuarioId)
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
            destinatarios, evento.Id, TipoNotificacao.Cancelamento,
            "Evento cancelado",
            $"O evento \"{evento.Titulo}\" foi cancelado. Motivo: {requisicao.Motivo.Trim()}",
            cancellationToken);

        await unitOfWork.SalvarAsync(cancellationToken);

        // Depois do commit: chamadas ao gateway nunca ficam dentro da transação do banco.
        await estornos.ProcessarPendentesAsync(evento.Id, cancellationToken);
        return new StatusEventoResponse(evento.Status);
    }

    public async Task<IReadOnlyList<EventoMeuResponse>> ListarMeusAsync(
        Guid usuarioId, PapelEvento? papel, StatusEvento? status, CancellationToken cancellationToken = default)
    {
        var itens = await eventos.ListarDoUsuarioAsync(usuarioId, papel, status, cancellationToken);

        return itens
            .Select(i => new EventoMeuResponse(
                i.Evento.Id, i.Evento.Titulo, i.Evento.Categoria, i.Evento.Local, i.Evento.DataInicio, i.Evento.DataFim,
                i.Evento.Preco, i.Evento.Status, i.Papel, i.Evento.ImagemUrl))
            .ToList();
    }

    /// <summary>E17: da alteração mais recente para a mais antiga, com a mesma visibilidade de E11.</summary>
    public async Task<IReadOnlyList<AlteracaoResponse>> ListarAlteracoesAsync(
        Guid eventoId, Guid? usuarioId, CancellationToken cancellationToken = default)
    {
        var permissao = await eventos.ObterPermissaoAsync(eventoId, usuarioId, cancellationToken);
        if (!permissao.PodeVer)
        {
            throw EventoNaoEncontrado();
        }

        return (await eventos.ListarAlteracoesAsync(eventoId, null, cancellationToken)).Select(Mapear).ToList();
    }

    internal async Task<EventoDetalheResultado> MontarDetalheAsync(
        Guid eventoId, Guid? usuarioId, PermissaoEvento permissao, CancellationToken cancellationToken)
    {
        var evento = await eventos.ObterComAtividadesAsync(eventoId, cancellationToken) ?? throw EventoNaoEncontrado();
        var organizador = await usuarios.ObterPorIdAsync(evento.OrganizadorId, cancellationToken);
        var ocupadas = await eventos.ContarVagasOcupadasAsync(evento.Id, relogio.UtcAgora, cancellationToken);
        var alteracoes = await eventos.ListarAlteracoesAsync(evento.Id, 5, cancellationToken);

        MinhaInscricaoResumo? minhaInscricao = null;
        var interessado = false;
        if (usuarioId is { } uid)
        {
            var inscricao = await inscricoes.ObterAtivaAsync(evento.Id, uid, cancellationToken);
            minhaInscricao = inscricao is null
                ? null
                : new MinhaInscricaoResumo(inscricao.Id, inscricao.Status, inscricao.CodigoCheckin, inscricao.ReservadaAte);
            interessado = await interesses.ExisteAsync(evento.Id, uid, cancellationToken);
        }

        var dados = new EventoDetalheResponse(
            evento.Id, evento.Titulo, evento.Descricao, evento.Categoria, evento.Local, evento.ImagemUrl,
            evento.DataInicio, evento.DataFim, evento.InscricaoInicio, evento.InscricaoFim,
            evento.Capacidade, evento.Preco, evento.CargaHoraria, evento.Status, evento.ReabertoEm,
            VagasRestantes(evento, ocupadas),
            new OrganizadorResumo(evento.OrganizadorId, organizador?.Nome ?? string.Empty),
            evento.Atividades.OrderBy(a => a.Inicio).Select(Mapear).ToList(),
            alteracoes.Select(Mapear).ToList(),
            minhaInscricao,
            permissao.EhOrganizador,
            permissao.Papel == PapelEvento.Colaborador,
            interessado);

        return new EventoDetalheResultado(dados, evento.Versao);
    }

    /// <summary>Confirma as alterações; se a linha mudou desde a leitura, responde 412 (RI11).</summary>
    private async Task SalvarComControleDeVersaoAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ConflitoDeConcorrenciaException)
        {
            throw VersaoDesatualizada();
        }
    }

    private static List<MudancaEvento> DetectarMudancas(Evento atual, DadosEvento novos)
    {
        var mudancas = new List<MudancaEvento>();

        if (atual.Titulo != novos.Titulo)
        {
            mudancas.Add(new MudancaEvento("titulo", atual.Titulo, novos.Titulo));
        }

        if (atual.DataInicio != novos.DataInicio)
        {
            mudancas.Add(new MudancaEvento("dataInicio",
                AlteracaoEventoRegistrador.Formatar(atual.DataInicio), AlteracaoEventoRegistrador.Formatar(novos.DataInicio)));
        }

        if (atual.DataFim != novos.DataFim)
        {
            mudancas.Add(new MudancaEvento("dataFim",
                AlteracaoEventoRegistrador.Formatar(atual.DataFim), AlteracaoEventoRegistrador.Formatar(novos.DataFim)));
        }

        if (atual.Local != novos.Local)
        {
            mudancas.Add(new MudancaEvento("local", atual.Local, novos.Local));
        }

        return mudancas;
    }

    private static DadosEvento Normalizar(EventoRequest r) => new(
        r.Titulo.Trim(), r.Descricao.Trim(), r.Categoria.Trim(), r.Local.Trim(),
        string.IsNullOrWhiteSpace(r.ImagemUrl) ? null : r.ImagemUrl.Trim(),
        r.DataInicio!.Value.UtcDateTime, r.DataFim!.Value.UtcDateTime,
        r.InscricaoInicio!.Value.UtcDateTime, r.InscricaoFim!.Value.UtcDateTime,
        r.Capacidade, r.Preco!.Value, r.CargaHoraria);

    internal static int? VagasRestantes(Evento evento, int ocupadas) =>
        evento.Capacidade is { } capacidade ? Math.Max(0, capacidade - ocupadas) : null;

    internal static AtividadeResponse Mapear(AtividadeProgramacao a) => new(a.Id, a.Titulo, a.Palestrante, a.Local, a.Inicio, a.Fim);

    private static AlteracaoResponse Mapear(AlteracaoEvento a) => new(a.Campo, a.ValorAnterior, a.ValorNovo, a.Motivo, a.CriadoEm);

    internal static RegraDeNegocioException EventoNaoEncontrado() =>
        new("EVENTO_NAO_ENCONTRADO", "Evento não encontrado", "O evento não existe ou você não tem acesso a ele.", 404);

    internal static RegraDeNegocioException MotivoObrigatorio() =>
        new("MOTIVO_OBRIGATORIO", "Motivo obrigatório",
            "Informe o motivo da alteração (de 10 a 300 caracteres): o evento já está publicado e os inscritos serão avisados.");

    private static RegraDeNegocioException VersaoDesatualizada() =>
        new("VERSAO_DESATUALIZADA", "Versão desatualizada",
            "O evento foi alterado por outra pessoa. Recarregue os dados e tente novamente.", 412);

    private sealed record DadosEvento(
        string Titulo, string Descricao, string Categoria, string Local, string? ImagemUrl,
        DateTime DataInicio, DateTime DataFim, DateTime InscricaoInicio, DateTime InscricaoFim,
        int? Capacidade, decimal Preco, short? CargaHoraria);
}
