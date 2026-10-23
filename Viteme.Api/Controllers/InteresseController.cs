using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autenticacao;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Acompanhar eventos sem estar inscrito (E32, E33).</summary>
[ApiController]
[Authorize]
[Route("api/v1/eventos/{id:guid}/interesse")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public class InteresseController(InteresseService interesses) : ControllerBase
{
    /// <summary>Passa a acompanhar o evento (E32). Idempotente.</summary>
    /// <remarks>Quem acompanha recebe avisos de alteração e lembretes (RN13, RN17).</remarks>
    /// <response code="404">Evento inexistente ou que o usuário não pode ver.</response>
    /// <response code="422"><c>EVENTO_INDISPONIVEL</c>: só eventos publicados.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Seguir(Guid id, CancellationToken cancellationToken)
    {
        await interesses.SeguirAsync(id, User.ObterUsuarioId(), cancellationToken);
        return NoContent();
    }

    /// <summary>Deixa de acompanhar o evento (E33). Idempotente.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeixarDeSeguir(Guid id, CancellationToken cancellationToken)
    {
        await interesses.DeixarDeSeguirAsync(id, User.ObterUsuarioId(), cancellationToken);
        return NoContent();
    }
}
