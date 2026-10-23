using Microsoft.Extensions.Logging;
using Viteme.Application.DTOs.Pagamentos;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Cobrança, consulta de situação e webhook do gateway (E29–E31). Regras RN11, RN12 e RN23.</summary>
public class PagamentoService(
    IPagamentoRepository pagamentos,
    IInscricaoRepository inscricoes,
    IEventoRepository eventos,
    IGatewayPagamento gateway,
    EstornoService estornos,
    NotificacaoService notificacoes,
    IUnitOfWork unitOfWork,
    IRelogio relogio,
    ILogger<PagamentoService> logger)
{
    private const int TentativasWebhook = 3;

    /// <summary>
    /// Cria a cobrança de um pagamento já gravado. O id do pagamento é a referência externa (idempotência), então
    /// nunca existe cobrança sem o registro correspondente no banco. Em falha do gateway o pagamento expira e
    /// responde 502 (RN11).
    /// </summary>
    public async Task CriarCobrancaAsync(
        Pagamento pagamento, Inscricao inscricao, Evento evento, bool expirarInscricaoEmFalha, CancellationToken cancellationToken = default)
    {
        CobrancaCriada cobranca;
        try
        {
            cobranca = await gateway.CriarCobrancaAsync(
                new PedidoCobranca(pagamento.Id, pagamento.Valor, $"Inscrição em {evento.Titulo}", inscricao.ReservadaAte ?? relogio.UtcAgora),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Gateway indisponível ao criar a cobrança do pagamento {PagamentoId}.", pagamento.Id);

            pagamento.Expirar();
            if (expirarInscricaoEmFalha)
            {
                inscricao.Expirar();
            }

            await unitOfWork.SalvarAsync(cancellationToken);
            throw new RegraDeNegocioException("GATEWAY_INDISPONIVEL", "Gateway indisponível",
                "Não foi possível gerar a cobrança agora. Tente novamente em instantes.", 502);
        }

        pagamento.RegistrarCobranca(cobranca.IdTransacao, cobranca.UrlPagamento);
        await unitOfWork.SalvarAsync(cancellationToken);
    }

    /// <summary>E29: retoma a cobrança pendente (200) ou gera uma nova após recusa ou expiração (201), enquanto a reserva valer.</summary>
    public async Task<CobrancaResultado> GerarCobrancaAsync(Guid inscricaoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var inscricao = await ObterDoUsuarioAsync(inscricaoId, usuarioId, cancellationToken);
        var evento = await eventos.ObterPorIdAsync(inscricao.EventoId, cancellationToken) ?? throw InscricaoNaoEncontrada();

        if (evento.Gratuito)
        {
            throw new RegraDeNegocioException("EVENTO_GRATUITO", "Evento gratuito", "Este evento não tem cobrança.");
        }

        var existentes = await pagamentos.ListarDaInscricaoAsync(inscricao.Id, cancellationToken);
        if (existentes.Any(p => p.Status == StatusPagamento.Aprovado) || inscricao.Status == StatusInscricao.Confirmada)
        {
            throw new RegraDeNegocioException("PAGAMENTO_JA_APROVADO", "Pagamento já aprovado",
                "O pagamento desta inscrição já foi aprovado.", 409);
        }

        if (inscricao.Status == StatusInscricao.Cancelada)
        {
            throw new RegraDeNegocioException("INSCRICAO_JA_CANCELADA", "Inscrição cancelada",
                "A inscrição foi cancelada.", 409);
        }

        var agora = relogio.UtcAgora;
        if (inscricao.Status == StatusInscricao.Expirada || inscricao.ReservaVencida(agora))
        {
            if (inscricao.Status == StatusInscricao.PendentePagamento)
            {
                ExpirarReserva(inscricao, existentes);
                await unitOfWork.SalvarAsync(cancellationToken);
            }

            throw new RegraDeNegocioException("RESERVA_EXPIRADA", "Reserva expirada",
                "A reserva da vaga expirou. Faça uma nova inscrição.");
        }

        var pendente = existentes.FirstOrDefault(p => p.Status == StatusPagamento.Pendente);
        if (pendente is not null)
        {
            return new CobrancaResultado(Mapear(pendente), false);
        }

        // RN12: o valor é exatamente o preço do evento (que não muda enquanto há inscrições ativas).
        var novo = new Pagamento(inscricao.Id, evento.Preco);
        pagamentos.Adicionar(novo);
        await unitOfWork.SalvarAsync(cancellationToken);

        await CriarCobrancaAsync(novo, inscricao, evento, expirarInscricaoEmFalha: false, cancellationToken);
        return new CobrancaResultado(Mapear(novo), true);
    }

    /// <summary>E30: situação do pagamento mais recente da inscrição.</summary>
    public async Task<PagamentoStatusResponse> ObterStatusAsync(Guid inscricaoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var inscricao = await ObterDoUsuarioAsync(inscricaoId, usuarioId, cancellationToken);
        var ultimo = (await pagamentos.ListarDaInscricaoAsync(inscricao.Id, cancellationToken)).FirstOrDefault()
            ?? throw new RegraDeNegocioException("PAGAMENTO_NAO_ENCONTRADO", "Pagamento não encontrado",
                "Esta inscrição não tem pagamento.", 404);

        return new PagamentoStatusResponse(ultimo.Status, ultimo.Valor, ultimo.Metodo, ultimo.AtualizadoEm, inscricao.Status);
    }

    /// <summary>
    /// E31: processa a mudança de status informada pelo gateway. Exige assinatura válida e é idempotente:
    /// o mesmo status para a mesma transação nunca é processado duas vezes (RN23).
    /// </summary>
    public async Task<WebhookResponse> ProcessarWebhookAsync(
        string corpoBruto, IReadOnlyDictionary<string, string> cabecalhos, CancellationToken cancellationToken = default)
    {
        if (!gateway.ValidarAssinaturaWebhook(corpoBruto, cabecalhos))
        {
            throw new RegraDeNegocioException("ASSINATURA_INVALIDA", "Assinatura inválida",
                "A assinatura do webhook não confere.", 401);
        }

        NotificacaoPagamento notificacao;
        try
        {
            notificacao = gateway.InterpretarWebhook(corpoBruto);
        }
        catch (FormatException ex)
        {
            throw new RegraDeNegocioException("WEBHOOK_INVALIDO", "Webhook inválido", ex.Message, 400);
        }

        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                return await ProcessarAsync(notificacao, cancellationToken);
            }
            catch (ConflitoDeConcorrenciaException) when (tentativa < TentativasWebhook)
            {
                // Outra operação (webhook duplicado, expiração, cancelamento) alterou os registros; reavalia do zero.
                unitOfWork.LimparRastreamento();
            }
            catch (ConflitoDeConcorrenciaException)
            {
                throw new RegraDeNegocioException("CONFLITO_CONCORRENCIA", "Conflito de concorrência",
                    "O pagamento está sendo processado por outra operação. Tente novamente.", 409);
            }
        }
    }

    private async Task<WebhookResponse> ProcessarAsync(NotificacaoPagamento n, CancellationToken cancellationToken)
    {
        var pagamento = await pagamentos.ObterPorIdAsync(n.ReferenciaExterna, cancellationToken) ?? throw TransacaoDesconhecida();
        if (pagamento.IdTransacaoGateway is { } registrado && registrado != n.IdTransacao)
        {
            throw TransacaoDesconhecida();
        }

        if (n.Valor != pagamento.Valor)
        {
            throw new RegraDeNegocioException("VALOR_DIVERGENTE", "Valor divergente",
                "O valor informado pelo gateway é diferente do valor cobrado.");
        }

        var inscricao = await inscricoes.ObterPorIdAsync(pagamento.InscricaoId, cancellationToken) ?? throw TransacaoDesconhecida();
        var agora = relogio.UtcAgora;

        switch (n.Status)
        {
            case StatusPagamento.Aprovado:
                return await ProcessarAprovacaoAsync(pagamento, inscricao, n, agora, cancellationToken);

            case StatusPagamento.Recusado when pagamento.Status == StatusPagamento.Pendente:
                pagamento.Recusar();
                await notificacoes.NotificarAsync(
                    [inscricao.UsuarioId], inscricao.EventoId, TipoNotificacao.Pagamento,
                    "Pagamento recusado",
                    "O pagamento foi recusado. Se a reserva ainda estiver válida, você pode tentar novamente.",
                    cancellationToken);
                await unitOfWork.SalvarAsync(cancellationToken);
                return new WebhookResponse(true);

            case StatusPagamento.Expirado when pagamento.Status == StatusPagamento.Pendente:
                pagamento.Expirar();
                await unitOfWork.SalvarAsync(cancellationToken);
                return new WebhookResponse(true);

            case StatusPagamento.Estornado when pagamento.Status == StatusPagamento.Aprovado:
                pagamento.Estornar();
                await unitOfWork.SalvarAsync(cancellationToken);
                return new WebhookResponse(true);

            default:
                // Mesmo status já registrado (repetição) ou transição sem efeito: nada a fazer.
                return new WebhookResponse(false);
        }
    }

    private async Task<WebhookResponse> ProcessarAprovacaoAsync(
        Pagamento pagamento, Inscricao inscricao, NotificacaoPagamento n, DateTime agora, CancellationToken cancellationToken)
    {
        // Repetição do webhook. Se o estorno de uma aprovação tardia falhou antes, esta é a nova tentativa.
        if (pagamento.Status is StatusPagamento.Aprovado or StatusPagamento.Estornado)
        {
            if (pagamento.Status == StatusPagamento.Aprovado && inscricao.Status != StatusInscricao.Confirmada
                && await estornos.TentarEstornarAsync(pagamento, inscricao.UsuarioId, inscricao.EventoId, cancellationToken))
            {
                await unitOfWork.SalvarAsync(cancellationToken);
            }

            return new WebhookResponse(false);
        }

        pagamento.Aprovar(n.Metodo);

        if (inscricao.Status == StatusInscricao.PendentePagamento && !inscricao.ReservaVencida(agora))
        {
            inscricao.Confirmar();
            await notificacoes.NotificarAsync(
                [inscricao.UsuarioId], inscricao.EventoId, TipoNotificacao.Pagamento,
                "Pagamento aprovado",
                "Seu pagamento foi aprovado e a inscrição está confirmada. O código de check-in está em Minhas inscrições.",
                cancellationToken);
            await unitOfWork.SalvarAsync(cancellationToken);
            return new WebhookResponse(true);
        }

        // RN23: aprovação para inscrição expirada, cancelada ou já confirmada por outro pagamento gera estorno automático.
        if (inscricao.Status == StatusInscricao.PendentePagamento)
        {
            inscricao.Expirar();
        }

        await unitOfWork.SalvarAsync(cancellationToken);
        if (await estornos.TentarEstornarAsync(pagamento, inscricao.UsuarioId, inscricao.EventoId, cancellationToken))
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }

        return new WebhookResponse(true);
    }

    private async Task<Inscricao> ObterDoUsuarioAsync(Guid inscricaoId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var inscricao = await inscricoes.ObterPorIdAsync(inscricaoId, cancellationToken);
        return inscricao is not null && inscricao.UsuarioId == usuarioId ? inscricao : throw InscricaoNaoEncontrada();
    }

    internal static void ExpirarReserva(Inscricao inscricao, IEnumerable<Pagamento> pagamentosDaInscricao)
    {
        inscricao.Expirar();
        foreach (var pagamento in pagamentosDaInscricao.Where(p => p.Status == StatusPagamento.Pendente))
        {
            pagamento.Expirar();
        }
    }

    private static CobrancaResponse Mapear(Pagamento p) => new(p.Id, p.Status, p.Valor, p.UrlPagamento);

    internal static RegraDeNegocioException InscricaoNaoEncontrada() =>
        new("INSCRICAO_NAO_ENCONTRADA", "Inscrição não encontrada", "A inscrição não existe ou não pertence a você.", 404);

    private static RegraDeNegocioException TransacaoDesconhecida() =>
        new("TRANSACAO_DESCONHECIDA", "Transação desconhecida", "Nenhum pagamento corresponde a esta referência.", 404);
}
