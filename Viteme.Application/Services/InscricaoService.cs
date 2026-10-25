using Viteme.Application.DTOs.Comuns;
using Viteme.Application.DTOs.Inscricoes;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Inscrição, cancelamento, listagens e check-in (E24–E28). Regras RN08–RN12, RN15, RN20.</summary>
public class InscricaoService(
    IEventoRepository eventos,
    IInscricaoRepository inscricoes,
    IPagamentoRepository pagamentos,
    IUsuarioRepository usuarios,
    PagamentoService pagamentoService,
    EstornoService estornos,
    NotificacaoService notificacoes,
    IGeradorAleatorio aleatorio,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
{
    private const int TamanhoCodigoCheckin = 12;
    private const int TentativasCodigo = 3;
    private const string RestricaoCodigo = "ix_inscricao_codigo_checkin";
    private const string RestricaoInscricaoAtiva = "ux_inscricao_ativa_por_usuario_evento";

    private sealed record InscricaoCriada(Inscricao Inscricao, Pagamento? Pagamento, Evento Evento);

    /// <summary>
    /// E24. A contagem de vagas e a gravação da inscrição e do pagamento ocorrem em uma transação curta, com bloqueio
    /// da linha do evento (RI10). A cobrança no gateway é feita depois do commit, com o id do pagamento como
    /// referência externa; se o gateway falhar, a inscrição expira e a vaga é liberada (RN11).
    /// </summary>
    public async Task<InscricaoCriadaResponse> InscreverAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            InscricaoCriada criada;
            try
            {
                criada = await unitOfWork.ExecutarEmTransacaoAsync(c => CriarAsync(eventoId, usuarioId, c), cancellationToken);
            }
            catch (ViolacaoDeUnicidadeException ex) when (ex.Restricao == RestricaoCodigo && tentativa < TentativasCodigo)
            {
                // Colisão (improvável) do código de check-in: gera outro.
                unitOfWork.LimparRastreamento();
                continue;
            }
            catch (ViolacaoDeUnicidadeException ex) when (ex.Restricao == RestricaoInscricaoAtiva)
            {
                // Duas requisições do mesmo usuário ao mesmo tempo.
                throw InscricaoExistente();
            }

            if (criada.Pagamento is { } pagamento)
            {
                await pagamentoService.CriarCobrancaAsync(pagamento, criada.Inscricao, criada.Evento, expirarInscricaoEmFalha: true, cancellationToken);
            }

            return Mapear(criada);
        }
    }

    private async Task<InscricaoCriada> CriarAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var evento = await eventos.ObterBloqueadoAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();
        var agora = relogio.UtcAgora;

        var permissao = await eventos.ObterPermissaoAsync(eventoId, usuarioId, cancellationToken);
        if (!permissao.PodeVer)
        {
            throw EventoService.EventoNaoEncontrado();
        }

        // RN04/RN08: organizador e colaboradores não se inscrevem no próprio evento.
        if (permissao.EhOrganizadorOuColaborador)
        {
            throw new RegraDeNegocioException("PAPEL_NAO_PERMITE_INSCRICAO", "Papel não permite inscrição",
                "Organizador e colaboradores não podem se inscrever no próprio evento.");
        }

        if (evento.Status != StatusEvento.Publicado)
        {
            throw new RegraDeNegocioException("EVENTO_INDISPONIVEL", "Evento indisponível",
                "O evento não está aberto para inscrições.");
        }

        if (await inscricoes.ObterAtivaAsync(eventoId, usuarioId, cancellationToken) is not null)
        {
            throw InscricaoExistente();
        }

        if (!evento.AceitaInscricoes(agora))
        {
            throw new RegraDeNegocioException("INSCRICOES_ENCERRADAS", "Inscrições encerradas",
                "O período de inscrições deste evento não está aberto.");
        }

        // RN09: vagas ocupadas = confirmadas + pendentes com reserva válida. Capacidade nula = ilimitado.
        if (evento.Capacidade is { } capacidade
            && await eventos.ContarVagasOcupadasAsync(eventoId, agora, cancellationToken) >= capacidade)
        {
            throw new RegraDeNegocioException("SEM_VAGAS", "Evento sem vagas", "Todas as vagas deste evento já foram preenchidas.");
        }

        var codigo = aleatorio.GerarCodigoAlfanumerico(TamanhoCodigoCheckin);
        Pagamento? pagamento = null;
        Inscricao inscricao;

        if (evento.Gratuito)
        {
            // RN10: evento gratuito confirma na hora.
            inscricao = Inscricao.CriarGratuita(eventoId, usuarioId, codigo);
            await notificacoes.NotificarAsync(
                [usuarioId], eventoId, TipoNotificacao.Inscricao,
                "Inscrição confirmada",
                $"Sua inscrição em \"{evento.Titulo}\" está confirmada. O código de check-in está em Minhas inscrições.",
                cancellationToken);
        }
        else
        {
            // RN11/RN12: reserva de 30 minutos e cobrança do valor exato do evento.
            inscricao = Inscricao.CriarPendentePagamento(eventoId, usuarioId, codigo, agora);
            pagamento = new Pagamento(inscricao.Id, evento.Preco);
            pagamentos.Adicionar(pagamento);
        }

        inscricoes.Adicionar(inscricao);
        await unitOfWork.SalvarAsync(cancellationToken);
        return new InscricaoCriada(inscricao, pagamento, evento);
    }

    /// <summary>E25.</summary>
    public async Task<IReadOnlyList<MinhaInscricaoResponse>> ListarMinhasAsync(
        Guid usuarioId, SituacaoInscricoes situacao, CancellationToken cancellationToken = default)
    {
        var itens = await inscricoes.ListarDoUsuarioAsync(usuarioId, situacao, relogio.UtcAgora, cancellationToken);

        return itens
            .Select(i => new MinhaInscricaoResponse(
                i.Inscricao.Id,
                new EventoDaInscricao(i.Evento.Id, i.Evento.Titulo, i.Evento.DataInicio, i.Evento.Local),
                i.Inscricao.Status, i.Inscricao.CodigoCheckin, i.Inscricao.ReservadaAte, i.Inscricao.PresencaConfirmada,
                CertificadoDisponivel(i.Inscricao, i.Evento)))
            .ToList();
    }

    /// <summary>
    /// E26 (RN15): o participante cancela até 24 h antes do início. Pagamento aprovado é estornado por inteiro; se o
    /// gateway falhar, o estorno fica pendente e a tarefa periódica repete.
    /// </summary>
    public async Task<CancelarInscricaoResponse> CancelarAsync(Guid inscricaoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var inscricao = await inscricoes.ObterPorIdAsync(inscricaoId, cancellationToken);
        if (inscricao is null || inscricao.UsuarioId != usuarioId)
        {
            throw PagamentoService.InscricaoNaoEncontrada();
        }

        var evento = await eventos.ObterPorIdAsync(inscricao.EventoId, cancellationToken) ?? throw PagamentoService.InscricaoNaoEncontrada();

        if (!inscricao.Ativa)
        {
            inscricao.Cancelar(); // lança 409 (já cancelada ou não ativa)
        }

        if (!inscricao.DentroDoPrazoDeCancelamento(evento.DataInicio, relogio.UtcAgora))
        {
            throw new RegraDeNegocioException("PRAZO_CANCELAMENTO_ENCERRADO", "Prazo de cancelamento encerrado",
                "O cancelamento só é permitido até 24 horas antes do início do evento.");
        }

        var doPagamento = await pagamentos.ListarDaInscricaoAsync(inscricao.Id, cancellationToken);
        inscricao.Cancelar();
        foreach (var pendente in doPagamento.Where(p => p.Status == StatusPagamento.Pendente))
        {
            pendente.Expirar();
        }

        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ConflitoDeConcorrenciaException)
        {
            throw new RegraDeNegocioException("CONFLITO_CONCORRENCIA", "Conflito de concorrência",
                "A inscrição foi alterada por outra operação (ex.: pagamento aprovado). Consulte-a e tente novamente.", 409);
        }

        var estornoSolicitado = false;
        foreach (var aprovado in doPagamento.Where(p => p.Status == StatusPagamento.Aprovado))
        {
            estornoSolicitado = true;
            if (await estornos.TentarEstornarAsync(aprovado, usuarioId, evento.Id, cancellationToken))
            {
                await unitOfWork.SalvarAsync(cancellationToken);
            }
        }

        return new CancelarInscricaoResponse(inscricao.Status, estornoSolicitado);
    }

    /// <summary>E27: inscritos do evento, sem o telefone (minimização de dados).</summary>
    public async Task<PaginaResponse<InscritoResponse>> ListarInscritosAsync(
        Guid eventoId, InscritosFiltro filtro, CancellationToken cancellationToken = default)
    {
        _ = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();

        var (itens, total) = await inscricoes.ListarInscritosAsync(eventoId, filtro, cancellationToken);
        return new PaginaResponse<InscritoResponse>(
            itens.Select(i => new InscritoResponse(i.InscricaoId, i.Nome, i.Email, i.Status, i.PresencaConfirmada, i.CheckinEm)).ToList(),
            filtro.Pagina, filtro.TamanhoPagina, total);
    }

    /// <summary>E28 (RN20): check-in pelo código da inscrição, uma única vez, de 2 h antes do início até o fim do evento.</summary>
    public async Task<CheckinResponse> RegistrarCheckinAsync(Guid eventoId, CheckinRequest requisicao, CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();
        var inscricao = await inscricoes.ObterPorCodigoAsync(eventoId, requisicao.CodigoCheckin.ToUpperInvariant(), cancellationToken)
            ?? throw new RegraDeNegocioException("INSCRICAO_NAO_ENCONTRADA", "Inscrição não encontrada",
                "Nenhuma inscrição deste evento tem este código.", 404);

        inscricao.RegistrarCheckin(evento.DataInicio, evento.DataFim, relogio.UtcAgora);

        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ConflitoDeConcorrenciaException)
        {
            // Dois check-ins simultâneos com o mesmo código: só o primeiro vale.
            throw new RegraDeNegocioException("CHECKIN_JA_REALIZADO", "Check-in já realizado",
                "O check-in desta inscrição já foi registrado.", 409);
        }

        var usuario = await usuarios.ObterPorIdAsync(inscricao.UsuarioId, cancellationToken);
        return new CheckinResponse(usuario?.Nome ?? string.Empty, inscricao.CheckinEm!.Value);
    }

    /// <summary>RN21: certificado para inscrição confirmada com presença, em evento encerrado e com carga horária.</summary>
    private static bool CertificadoDisponivel(Inscricao inscricao, Evento evento) =>
        inscricao.Status == StatusInscricao.Confirmada && inscricao.PresencaConfirmada
        && evento.Status == StatusEvento.Encerrado && evento.CargaHoraria is not null;

    private static InscricaoCriadaResponse Mapear(InscricaoCriada criada)
    {
        var inscricao = criada.Inscricao;
        var pagamento = criada.Pagamento is { } p ? new PagamentoDaInscricao(p.Id, p.Valor, p.UrlPagamento) : null;

        return new InscricaoCriadaResponse(
            inscricao.Id, inscricao.Status,
            inscricao.Status == StatusInscricao.Confirmada ? inscricao.CodigoCheckin : null,
            inscricao.ReservadaAte, pagamento);
    }

    private static RegraDeNegocioException InscricaoExistente() =>
        new("INSCRICAO_EXISTENTE", "Inscrição já existente", "Você já tem uma inscrição ativa neste evento.", 409);
}
