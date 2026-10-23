using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autenticacao;
using Viteme.Application.DTOs.Pagamentos;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Cobrança de inscrições em eventos pagos e retorno do gateway de pagamento.</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class PagamentosController(PagamentoService pagamentos) : ControllerBase
{
    private const int TamanhoMaximoWebhook = 64 * 1024;

    /// <summary>Gera ou retoma a cobrança de uma inscrição pendente (E29).</summary>
    /// <remarks>
    /// Devolve 200 com a cobrança pendente existente, ou 201 com uma nova (só se a anterior foi recusada ou expirou e a
    /// reserva ainda vale). O valor é exatamente o preço do evento (RN12).
    /// </remarks>
    /// <response code="404">Inscrição inexistente ou de outro usuário.</response>
    /// <response code="409"><c>PAGAMENTO_JA_APROVADO</c>.</response>
    /// <response code="422"><c>RESERVA_EXPIRADA</c> ou <c>EVENTO_GRATUITO</c>.</response>
    /// <response code="502"><c>GATEWAY_INDISPONIVEL</c>.</response>
    [HttpPost("inscricoes/{id:guid}/pagamento")]
    [Authorize]
    [ProducesResponseType<CobrancaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<CobrancaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<IActionResult> GerarCobranca(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await pagamentos.GerarCobrancaAsync(id, User.ObterUsuarioId(), cancellationToken);
        return resultado.Nova ? StatusCode(StatusCodes.Status201Created, resultado.Cobranca) : Ok(resultado.Cobranca);
    }

    /// <summary>Situação do pagamento da inscrição (E30), usada pelo frontend ao voltar do gateway.</summary>
    [HttpGet("inscricoes/{id:guid}/pagamento")]
    [Authorize]
    [ProducesResponseType<PagamentoStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> ObterStatus(Guid id, CancellationToken cancellationToken) =>
        Ok(await pagamentos.ObterStatusAsync(id, User.ObterUsuarioId(), cancellationToken));

    /// <summary>Recebe do gateway a mudança de status de um pagamento (E31).</summary>
    /// <remarks>
    /// Não usa JWT nem rate limiting: a autenticidade vem da assinatura do corpo (cabeçalho <c>X-Viteme-Assinatura</c>,
    /// HMAC-SHA256 no stub). Corpo: <c>{ idTransacao, referenciaExterna, status, valor, metodo? }</c>.
    /// O processamento é idempotente (RN23): repetir o mesmo status responde 200 com <c>processado: false</c>.
    /// </remarks>
    /// <response code="400"><c>WEBHOOK_INVALIDO</c>.</response>
    /// <response code="401"><c>ASSINATURA_INVALIDA</c>.</response>
    /// <response code="404"><c>TRANSACAO_DESCONHECIDA</c>.</response>
    /// <response code="422"><c>VALOR_DIVERGENTE</c>.</response>
    [HttpPost("pagamentos/webhook")]
    [AllowAnonymous]
    [RequestSizeLimit(TamanhoMaximoWebhook)]
    [Consumes("application/json", "text/plain")]
    [ProducesResponseType<WebhookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ReceberWebhook(CancellationToken cancellationToken)
    {
        // A assinatura é calculada sobre o corpo exato, então ele é lido como texto, sem model binding.
        using var leitor = new StreamReader(Request.Body, Encoding.UTF8);
        var corpo = await leitor.ReadToEndAsync(cancellationToken);

        var cabecalhos = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        return Ok(await pagamentos.ProcessarWebhookAsync(corpo, cabecalhos, cancellationToken));
    }
}
