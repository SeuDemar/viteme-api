using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autenticacao;
using Viteme.Api.Autorizacao;
using Viteme.Application.DTOs.Comuns;
using Viteme.Application.DTOs.Inscricoes;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Inscrições em eventos, check-in e lista de inscritos. Recursos pessoais são sempre filtrados pelo usuário do token.</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public class InscricoesController(InscricaoService inscricoes) : ControllerBase
{
    /// <summary>Inscreve o usuário autenticado em um evento (E24).</summary>
    /// <remarks>
    /// Evento gratuito: a inscrição já nasce <c>CONFIRMADA</c>. Evento pago: nasce <c>PENDENTE_PAGAMENTO</c> com reserva de
    /// 30 minutos e devolve a URL de pagamento. Se o gateway falhar, a inscrição expira e a vaga é liberada.
    /// </remarks>
    /// <response code="404">Evento inexistente ou que o usuário não pode ver.</response>
    /// <response code="409"><c>INSCRICAO_EXISTENTE</c>.</response>
    /// <response code="422"><c>EVENTO_INDISPONIVEL</c>, <c>INSCRICOES_ENCERRADAS</c>, <c>SEM_VAGAS</c> ou <c>PAPEL_NAO_PERMITE_INSCRICAO</c>.</response>
    /// <response code="502"><c>GATEWAY_INDISPONIVEL</c>.</response>
    [HttpPost("eventos/{id:guid}/inscricoes")]
    [Authorize]
    [ProducesResponseType<InscricaoCriadaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<IActionResult> Inscrever(Guid id, CancellationToken cancellationToken)
    {
        var resposta = await inscricoes.InscreverAsync(id, User.ObterUsuarioId(), cancellationToken);
        return Created("/api/v1/inscricoes/minhas", resposta);
    }

    /// <summary>Lista as inscrições do usuário autenticado (E25).</summary>
    /// <param name="situacao"><c>proximas</c>, <c>passadas</c> ou <c>todas</c> (padrão).</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    [HttpGet("inscricoes/minhas")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<MinhaInscricaoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ListarMinhas([FromQuery] SituacaoInscricoes situacao, CancellationToken cancellationToken) =>
        Ok(await inscricoes.ListarMinhasAsync(User.ObterUsuarioId(), situacao, cancellationToken));

    /// <summary>Cancela a própria inscrição (E26), até 24 horas antes do início do evento (RN15).</summary>
    /// <remarks>Se houver pagamento aprovado, o estorno integral é solicitado ao gateway.</remarks>
    /// <response code="404">Inscrição inexistente ou de outro usuário.</response>
    /// <response code="409"><c>INSCRICAO_JA_CANCELADA</c>.</response>
    /// <response code="422"><c>PRAZO_CANCELAMENTO_ENCERRADO</c>.</response>
    [HttpPost("inscricoes/{id:guid}/cancelar")]
    [Authorize]
    [ProducesResponseType<CancelarInscricaoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken cancellationToken) =>
        Ok(await inscricoes.CancelarAsync(id, User.ObterUsuarioId(), cancellationToken));

    /// <summary>Lista os inscritos do evento (E27). Organizador ou colaborador. O telefone não é exibido.</summary>
    [HttpGet("eventos/{id:guid}/inscricoes")]
    [Authorize(Policy = Politicas.OrganizadorOuColaborador)]
    [ProducesResponseType<PaginaResponse<InscritoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> ListarInscritos(Guid id, [FromQuery] InscritosFiltro filtro, CancellationToken cancellationToken) =>
        Ok(await inscricoes.ListarInscritosAsync(id, filtro, cancellationToken));

    /// <summary>Registra a presença de um inscrito pelo código de check-in (E28). Organizador ou colaborador.</summary>
    /// <remarks>Só para inscrição confirmada, de 2 horas antes do início até o fim do evento, e uma única vez (RN20).</remarks>
    /// <response code="404"><c>INSCRICAO_NAO_ENCONTRADA</c>.</response>
    /// <response code="409"><c>CHECKIN_JA_REALIZADO</c>.</response>
    /// <response code="422"><c>INSCRICAO_NAO_CONFIRMADA</c> ou <c>FORA_DO_HORARIO_DE_CHECKIN</c>.</response>
    [HttpPost("eventos/{id:guid}/checkin")]
    [Authorize(Policy = Politicas.OrganizadorOuColaborador)]
    [ProducesResponseType<CheckinResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Checkin(Guid id, [FromBody] CheckinRequest requisicao, CancellationToken cancellationToken) =>
        Ok(await inscricoes.RegistrarCheckinAsync(id, requisicao, cancellationToken));
}
