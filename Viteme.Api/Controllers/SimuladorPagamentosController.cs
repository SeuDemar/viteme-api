using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autenticacao;
using Viteme.Application.DTOs.Pagamentos;
using Viteme.Application.Interfaces;
using Viteme.Application.Services;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Api.Controllers;

public enum ResultadoSimulado
{
    Aprovar,
    Recusar,
    Expirar,
}

/// <summary>
/// SOMENTE DEMONSTRAÇÃO. Simula o pagamento de uma cobrança do próprio usuário. Desligado por padrão
/// (<c>Gateway:Stub:HabilitarSimulador</c>); com ele desligado responde 404.
/// <list type="bullet">
/// <item>Gateway stub: monta o webhook, assina com o segredo do stub e o processa pelo mesmo caminho do E31.</item>
/// <item>Asaas no sandbox: confirma a cobrança no próprio sandbox (só "aprovar"; o sandbox não recusa Pix).</item>
/// </list>
/// A cobrança é indicada pelo id da transação no gateway ou pelo id do pagamento.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/simulador/pagamentos")]
[Produces("application/json")]
public class SimuladorPagamentosController(
    ISimuladorGateway simulador, IEnumerable<IConfirmacaoSandbox> sandbox, IPagamentoRepository pagamentos, IInscricaoRepository inscricoes,
    PagamentoService pagamentoService)
    : ControllerBase
{
    /// <summary>Simula o resultado do pagamento de uma cobrança do próprio usuário.</summary>
    /// <param name="idTransacao">Id da transação no gateway (<c>stub_...</c>, <c>pay_...</c>) ou id do pagamento (E29).</param>
    /// <param name="resultado"><c>aprovar</c>, <c>recusar</c> ou <c>expirar</c>.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    [HttpPost("{idTransacao}/{resultado}")]
    [ProducesResponseType<WebhookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Simular(string idTransacao, ResultadoSimulado resultado, CancellationToken cancellationToken)
    {
        if (!simulador.Habilitado)
        {
            return NotFound();
        }

        var pagamento = await pagamentos.ObterPorIdTransacaoAsync(idTransacao, cancellationToken)
            ?? (Guid.TryParse(idTransacao, out var pagamentoId) ? await pagamentos.ObterPorIdAsync(pagamentoId, cancellationToken) : null);
        var inscricao = pagamento is null ? null : await inscricoes.ObterPorIdAsync(pagamento.InscricaoId, cancellationToken);
        if (pagamento is null || inscricao is null || inscricao.UsuarioId != User.ObterUsuarioId())
        {
            throw new RegraDeNegocioException("TRANSACAO_DESCONHECIDA", "Transação desconhecida",
                "Nenhuma cobrança sua tem este id de transação.", 404);
        }

        if (sandbox.FirstOrDefault(s => s.Disponivel) is { } confirmacao)
        {
            if (resultado != ResultadoSimulado.Aprovar)
            {
                throw new RegraDeNegocioException("SIMULACAO_INDISPONIVEL", "Simulação indisponível",
                    "No sandbox do Asaas só é possível simular o pagamento aprovado.", 422);
            }

            return Ok(await pagamentoService.ConfirmarNoSandboxAsync(pagamento.Id, confirmacao, cancellationToken));
        }

        var transacao = pagamento.IdTransacaoGateway ?? idTransacao;
        var status = resultado switch
        {
            ResultadoSimulado.Aprovar => StatusPagamento.Aprovado,
            ResultadoSimulado.Recusar => StatusPagamento.Recusado,
            _ => StatusPagamento.Expirado,
        };

        var corpo = simulador.MontarWebhook(transacao, pagamento.Id, status, pagamento.Valor, "SIMULADO");
        var cabecalhos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [simulador.NomeCabecalhoAssinatura] = simulador.Assinar(corpo),
        };

        return Ok(await pagamentoService.ProcessarWebhookAsync(corpo, cabecalhos, cancellationToken));
    }
}
