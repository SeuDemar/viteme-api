using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viteme.Api.Autenticacao;
using Viteme.Api.Configuracao;
using Viteme.Application.DTOs.Certificados;
using Viteme.Application.Services;
using Viteme.Domain.Excecoes;

namespace Viteme.Api.Controllers;

/// <summary>Certificado de participação (E38, E39).</summary>
[ApiController]
[Route("api/v1")]
public partial class CertificadosController(CertificadoService certificados) : ControllerBase
{
    /// <summary>Emite (na primeira chamada) e baixa o certificado em PDF (E38).</summary>
    /// <remarks>
    /// RN21: inscrição confirmada com presença registrada, evento encerrado e carga horária informada. Chamadas seguintes
    /// devolvem o mesmo certificado, com o mesmo código de validação.
    /// </remarks>
    /// <response code="404">Inscrição inexistente ou de outro usuário.</response>
    /// <response code="422"><c>CERTIFICADO_INDISPONIVEL</c>.</response>
    [HttpGet("inscricoes/{id:guid}/certificado")]
    [Authorize]
    [Produces("application/pdf", "application/problem+json")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken cancellationToken)
    {
        var arquivo = await certificados.ObterOuEmitirAsync(id, User.ObterUsuarioId(), cancellationToken);
        return File(arquivo.Conteudo, "application/pdf", arquivo.NomeArquivo);
    }

    /// <summary>Valida a autenticidade de um certificado pelo código impresso nele (E39). Público.</summary>
    /// <param name="codigo">Até 20 letras e números.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="400">Código fora do formato.</response>
    /// <response code="404"><c>CERTIFICADO_NAO_ENCONTRADO</c>.</response>
    /// <response code="429">Limite de consultas por minuto excedido (por IP).</response>
    [HttpGet("certificados/{codigo}/validacao")]
    [AllowAnonymous]
    [EnableRateLimiting(LimitacaoExtensions.ValidacaoCertificado)]
    [Produces("application/json")]
    [ProducesResponseType<ValidacaoCertificadoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Validar(string codigo, CancellationToken cancellationToken)
    {
        if (!CodigoValido().IsMatch(codigo))
        {
            throw new RegraDeNegocioException("DADOS_INVALIDOS", "Dados inválidos",
                "O código do certificado deve ter até 20 letras e números.", StatusCodes.Status400BadRequest);
        }

        return Ok(await certificados.ValidarAsync(codigo, cancellationToken));
    }

    [GeneratedRegex("^[A-Za-z0-9]{1,20}$")]
    private static partial Regex CodigoValido();
}
