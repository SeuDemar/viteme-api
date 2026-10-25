using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autorizacao;
using Viteme.Application.DTOs.Eventos;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Colaboradores do evento: consultam os inscritos e fazem o check-in (RN07).</summary>
[ApiController]
[Route("api/v1/eventos/{id:guid}/colaboradores")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public class ColaboradoresController(ColaboradorService colaboradores) : ControllerBase
{
    /// <summary>Lista os colaboradores do evento (E21). Organizador ou colaborador.</summary>
    [HttpGet]
    [Authorize(Policy = Politicas.OrganizadorOuColaborador)]
    [ProducesResponseType<IReadOnlyList<ColaboradorResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(Guid id, CancellationToken cancellationToken) =>
        Ok(await colaboradores.ListarAsync(id, cancellationToken));

    /// <summary>Adiciona um colaborador pelo e-mail (E22). O usuário é notificado.</summary>
    /// <response code="404"><c>USUARIO_NAO_ENCONTRADO</c>.</response>
    /// <response code="409"><c>JA_COLABORADOR</c>.</response>
    /// <response code="422"><c>ORGANIZADOR_NAO_PODE_SER_COLABORADOR</c> ou <c>USUARIO_JA_INSCRITO</c>.</response>
    [HttpPost]
    [Authorize(Policy = Politicas.OrganizadorDoEvento)]
    [ProducesResponseType<ColaboradorResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Adicionar(Guid id, [FromBody] ColaboradorRequest requisicao, CancellationToken cancellationToken)
    {
        var colaborador = await colaboradores.AdicionarAsync(id, requisicao, cancellationToken);
        return Created($"/api/v1/eventos/{id}/colaboradores", colaborador);
    }

    /// <summary>Remove um colaborador (E23).</summary>
    [HttpDelete("{usuarioId:guid}")]
    [Authorize(Policy = Politicas.OrganizadorDoEvento)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remover(Guid id, Guid usuarioId, CancellationToken cancellationToken)
    {
        await colaboradores.RemoverAsync(id, usuarioId, cancellationToken);
        return NoContent();
    }
}
