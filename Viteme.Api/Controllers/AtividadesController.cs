using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autenticacao;
using Viteme.Api.Autorizacao;
using Viteme.Application.DTOs.Eventos;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>
/// Programação do evento. Somente o organizador altera. Em evento publicado, toda mudança exige
/// <c>motivoAlteracao</c> e avisa os inscritos (RN13).
/// </summary>
[ApiController]
[Authorize(Policy = Politicas.OrganizadorDoEvento)]
[Route("api/v1/eventos/{id:guid}/atividades")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public class AtividadesController(ProgramacaoService programacao) : ControllerBase
{
    /// <summary>Adiciona uma atividade à programação (E18).</summary>
    /// <response code="422"><c>ATIVIDADE_FORA_DO_PERIODO</c> ou <c>MOTIVO_OBRIGATORIO</c> (evento publicado).</response>
    [HttpPost]
    [ProducesResponseType<AtividadeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Adicionar(Guid id, [FromBody] AtividadeRequest requisicao, CancellationToken cancellationToken)
    {
        var atividade = await programacao.AdicionarAsync(id, User.ObterUsuarioId(), requisicao, cancellationToken);
        return Created($"/api/v1/eventos/{id}/atividades/{atividade.Id}", atividade);
    }

    /// <summary>Edita uma atividade da programação (E19).</summary>
    [HttpPut("{atividadeId:guid}")]
    [ProducesResponseType<AtividadeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Editar(Guid id, Guid atividadeId, [FromBody] AtividadeRequest requisicao, CancellationToken cancellationToken) =>
        Ok(await programacao.EditarAsync(id, atividadeId, User.ObterUsuarioId(), requisicao, cancellationToken));

    /// <summary>Remove uma atividade da programação (E20). O <c>motivo</c> é obrigatório em evento publicado.</summary>
    [HttpDelete("{atividadeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Remover(Guid id, Guid atividadeId, [FromQuery] MotivoQuery consulta, CancellationToken cancellationToken)
    {
        await programacao.RemoverAsync(id, atividadeId, User.ObterUsuarioId(), consulta.Motivo, cancellationToken);
        return NoContent();
    }
}
