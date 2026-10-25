using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autenticacao;
using Viteme.Application.DTOs.Notificacoes;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Sino de notificações do usuário autenticado (E34–E36).</summary>
[ApiController]
[Authorize]
[Route("api/v1/notificacoes")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public class NotificacoesController(CaixaNotificacoesService caixa) : ControllerBase
{
    /// <summary>Lista as notificações do usuário, das mais novas às mais antigas (E34).</summary>
    /// <remarks><c>naoLidas</c> conta todas as não lidas, independentemente do filtro <c>lidas</c>.</remarks>
    [HttpGet]
    [ProducesResponseType<NotificacoesPaginaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Listar([FromQuery] NotificacoesFiltro filtro, CancellationToken cancellationToken) =>
        Ok(await caixa.ListarAsync(User.ObterUsuarioId(), filtro, cancellationToken));

    /// <summary>Marca uma notificação como lida (E35).</summary>
    /// <response code="404">Notificação inexistente ou de outro usuário.</response>
    [HttpPatch("{id:guid}/lida")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> MarcarComoLida(Guid id, CancellationToken cancellationToken)
    {
        await caixa.MarcarComoLidaAsync(id, User.ObterUsuarioId(), cancellationToken);
        return NoContent();
    }

    /// <summary>Marca todas as notificações do usuário como lidas (E36).</summary>
    [HttpPatch("lidas")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarTodasComoLidas(CancellationToken cancellationToken)
    {
        await caixa.MarcarTodasComoLidasAsync(User.ObterUsuarioId(), cancellationToken);
        return NoContent();
    }
}
