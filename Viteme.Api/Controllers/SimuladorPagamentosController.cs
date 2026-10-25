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
/// SOMENTE DEMONSTRAÇÃO. Enquanto o gateway real (Asaas) não está integrado, simula o pagamento de uma cobrança do
/// stub: monta o webhook, assina com o segredo do stub e o processa pelo mesmo caminho do E31. Desligado por padrão
/// (<c>Gateway:Stub:HabilitarSimulador</c>); com ele desligado responde 404.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/simulador/pagamentos")]
[Produces("application/json")]
public class SimuladorPagamentosController(
    ISimuladorGateway simulador, IPagamentoRepository pagamentos, IInscricaoRepository inscricoes, PagamentoService pagamentoService)
    : ControllerBase
{
    /// <summary>Simula o resultado do pagamento de uma cobrança do próprio usuário.</summary>
    /// <param name="idTransacao">Id da transação devolvido na cobrança (<c>stub_...</c>).</param>
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

        var pagamento = await pagamentos.ObterPorIdTransacaoAsync(idTransacao, cancellationToken);
        var inscricao = pagamento is null ? null : await inscricoes.ObterPorIdAsync(pagamento.InscricaoId, cancellationToken);
        if (pagamento is null || inscricao is null || inscricao.UsuarioId != User.ObterUsuarioId())
        {
            throw new RegraDeNegocioException("TRANSACAO_DESCONHECIDA", "Transação desconhecida",
                "Nenhuma cobrança sua tem este id de transação.", 404);
        }

        var status = resultado switch
        {
            ResultadoSimulado.Aprovar => StatusPagamento.Aprovado,
            ResultadoSimulado.Recusar => StatusPagamento.Recusado,
            _ => StatusPagamento.Expirado,
        };

        var corpo = simulador.MontarWebhook(idTransacao, pagamento.Id, status, pagamento.Valor, "SIMULADO");
        var cabecalhos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [simulador.NomeCabecalhoAssinatura] = simulador.Assinar(corpo),
        };

        return Ok(await pagamentoService.ProcessarWebhookAsync(corpo, cabecalhos, cancellationToken));
    }
}
