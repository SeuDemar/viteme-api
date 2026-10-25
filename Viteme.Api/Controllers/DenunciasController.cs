using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viteme.Api.Autenticacao;
using Viteme.Api.Configuracao;
using Viteme.Application.DTOs.Denuncias;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Denúncia de eventos (E37).</summary>
[ApiController]
[Authorize]
[Route("api/v1/eventos/{id:guid}/denuncias")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public class DenunciasController(DenunciaService denuncias) : ControllerBase
{
    /// <summary>Denuncia um evento (E37). Uma denúncia por usuário e evento.</summary>
    /// <remarks>
    /// RN19: com 5 denúncias o evento publicado sai do mural (<c>OCULTO</c>). Se o organizador o republicar e ele receber
    /// outras 5, é cancelado automaticamente. A descrição é obrigatória quando o motivo é <c>OUTRO</c>.
    /// </remarks>
    /// <response code="404">Evento inexistente ou que o usuário não pode ver.</response>
    /// <response code="409"><c>DENUNCIA_EXISTENTE</c>.</response>
    /// <response code="422"><c>NAO_PODE_DENUNCIAR_PROPRIO_EVENTO</c> ou <c>EVENTO_INDISPONIVEL</c>.</response>
    /// <response code="429">Limite de denúncias por minuto excedido.</response>
    [HttpPost]
    [EnableRateLimiting(LimitacaoExtensions.Denuncias)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Denunciar(Guid id, [FromBody] DenunciarEventoRequest requisicao, CancellationToken cancellationToken)
    {
        await denuncias.DenunciarAsync(id, User.ObterUsuarioId(), requisicao, cancellationToken);
        return StatusCode(StatusCodes.Status201Created);
    }
}
