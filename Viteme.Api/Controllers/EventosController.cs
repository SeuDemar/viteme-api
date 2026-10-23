using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viteme.Api.Autenticacao;
using Viteme.Api.Autorizacao;
using Viteme.Api.Configuracao;
using Viteme.Application.DTOs.Comuns;
using Viteme.Application.DTOs.Eventos;
using Viteme.Application.Services;
using Viteme.Domain.Enums;

namespace Viteme.Api.Controllers;

/// <summary>Mural, detalhes e ciclo de vida dos eventos. Quem cria o evento é o organizador (RN04).</summary>
[ApiController]
[Route("api/v1/eventos")]
[Produces("application/json")]
public class EventosController(EventoService eventos) : ControllerBase
{
    /// <summary>Lista os eventos do mural (E10): publicados e ainda não encerrados, do mais próximo ao mais distante.</summary>
    /// <response code="400">Parâmetros inválidos (<c>dataDe</c> maior que <c>dataAte</c>, página fora do limite).</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PaginaResponse<EventoResumoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Listar([FromQuery] MuralFiltro filtro, CancellationToken cancellationToken) =>
        Ok(await eventos.ListarMuralAsync(filtro, cancellationToken));

    /// <summary>Lista os eventos que o usuário organiza ou em que colabora (E16).</summary>
    /// <param name="papel"><c>organizador</c> ou <c>colaborador</c>.</param>
    /// <param name="status">Situação do evento (ex.: <c>PUBLICADO</c>).</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    [HttpGet("meus")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<EventoMeuResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ListarMeus(
        [FromQuery] PapelEvento? papel, [FromQuery] StatusEvento? status, CancellationToken cancellationToken)
    {
        if (papel == PapelEvento.Participante)
        {
            ModelState.AddModelError(nameof(papel), "Use organizador ou colaborador.");
            return ValidationProblem(ModelState);
        }

        return Ok(await eventos.ListarMeusAsync(User.ObterUsuarioId(), papel, status, cancellationToken));
    }

    /// <summary>Detalhes de um evento (E11). Devolve o <c>ETag</c> para edição (If-Match).</summary>
    /// <remarks>Rascunho só é visível ao organizador e aos colaboradores; evento oculto também aos inscritos. Quem não pode ver recebe 404.</remarks>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<EventoDetalheResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Detalhar(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await eventos.ObterDetalheAsync(id, User.ObterUsuarioIdOuNull(), cancellationToken);
        return ComEtag(resultado);
    }

    /// <summary>Cria um evento como rascunho (E12). O criador passa a ser o organizador.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<EventoDetalheResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Criar([FromBody] EventoRequest requisicao, CancellationToken cancellationToken)
    {
        var resultado = await eventos.CriarAsync(User.ObterUsuarioId(), requisicao, cancellationToken);

        Response.Headers.ETag = VersaoEvento.ParaEtag(resultado.Versao);
        return Created($"/api/v1/eventos/{resultado.Dados.Id}", resultado.Dados);
    }

    /// <summary>Edita o evento (E13). Exige o cabeçalho <c>If-Match</c> com o ETag lido.</summary>
    /// <remarks>
    /// Em evento publicado, alterar título, data, horário ou local exige <c>motivoAlteracao</c> (10 a 300 caracteres),
    /// registra o histórico e avisa inscritos ativos e interessados (RN13).
    /// </remarks>
    /// <response code="403">Sem o papel de organizador neste evento.</response>
    /// <response code="409"><c>EVENTO_FINALIZADO</c>: evento cancelado ou encerrado.</response>
    /// <response code="412"><c>VERSAO_DESATUALIZADA</c>: o evento mudou desde a leitura.</response>
    /// <response code="422"><c>PRECO_BLOQUEADO</c>, <c>CAPACIDADE_ABAIXO_DAS_VAGAS_OCUPADAS</c>, <c>MOTIVO_OBRIGATORIO</c> ou <c>ATIVIDADE_FORA_DO_PERIODO</c>.</response>
    /// <response code="428"><c>VERSAO_OBRIGATORIA</c>: faltou o cabeçalho If-Match.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.OrganizadorDoEvento)]
    [ProducesResponseType<EventoDetalheResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public async Task<IActionResult> Atualizar(
        Guid id, [FromBody] EventoAtualizarRequest requisicao,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        var resultado = await eventos.AtualizarAsync(
            id, User.ObterUsuarioId(), VersaoEvento.Interpretar(ifMatch), requisicao, cancellationToken);
        return ComEtag(resultado);
    }

    /// <summary>Publica o evento no mural, ou o republica após ocultação (E14).</summary>
    /// <response code="409"><c>EVENTO_FINALIZADO</c> ou <c>EVENTO_JA_PUBLICADO</c>.</response>
    /// <response code="422"><c>DADOS_INCOMPLETOS</c> ou <c>DATA_INICIO_PASSADA</c>.</response>
    [HttpPost("{id:guid}/publicar")]
    [Authorize(Policy = Politicas.OrganizadorDoEvento)]
    [ProducesResponseType<StatusEventoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Publicar(Guid id, CancellationToken cancellationToken) =>
        Ok(await eventos.PublicarAsync(id, cancellationToken));

    /// <summary>Cancela o evento (E15): cancela as inscrições ativas e avisa os envolvidos (RN14).</summary>
    /// <response code="409"><c>EVENTO_JA_CANCELADO</c>.</response>
    /// <response code="422"><c>EVENTO_ENCERRADO</c>: evento encerrado não pode ser cancelado.</response>
    [HttpPost("{id:guid}/cancelar")]
    [Authorize(Policy = Politicas.OrganizadorDoEvento)]
    [ProducesResponseType<StatusEventoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarEventoRequest requisicao, CancellationToken cancellationToken) =>
        Ok(await eventos.CancelarAsync(id, User.ObterUsuarioId(), requisicao, cancellationToken));

    /// <summary>Histórico de alterações do evento (E17), da mais recente para a mais antiga.</summary>
    [HttpGet("{id:guid}/alteracoes")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<AlteracaoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> ListarAlteracoes(Guid id, CancellationToken cancellationToken) =>
        Ok(await eventos.ListarAlteracoesAsync(id, User.ObterUsuarioIdOuNull(), cancellationToken));

    private OkObjectResult ComEtag(EventoDetalheResultado resultado)
    {
        Response.Headers.ETag = VersaoEvento.ParaEtag(resultado.Versao);
        return Ok(resultado.Dados);
    }
}
