using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viteme.Api.Autenticacao;
using Viteme.Api.Configuracao;
using Viteme.Application.DTOs.Usuarios;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Perfil do usuário autenticado, verificação de telefone e consentimento de SMS (LGPD).</summary>
[ApiController]
[Authorize]
[Route("api/v1/usuarios/me")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public class UsuariosController(UsuarioService usuarios) : ControllerBase
{
    /// <summary>Consulta o próprio perfil (E05).</summary>
    [HttpGet]
    [ProducesResponseType<PerfilResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken) =>
        Ok(await usuarios.ObterPerfilAsync(User.ObterUsuarioId(), cancellationToken));

    /// <summary>Atualiza nome e telefone (E06). Trocar o telefone zera a verificação e o aceite de SMS.</summary>
    [HttpPut]
    [ProducesResponseType<PerfilResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Atualizar([FromBody] AtualizarPerfilRequest requisicao, CancellationToken cancellationToken) =>
        Ok(await usuarios.AtualizarPerfilAsync(User.ObterUsuarioId(), requisicao, cancellationToken));

    /// <summary>Envia por SMS o código de verificação do telefone (E07).</summary>
    /// <response code="202">Código enviado; vale por 10 minutos.</response>
    /// <response code="422"><c>TELEFONE_NAO_INFORMADO</c> ou <c>TELEFONE_JA_VERIFICADO</c>.</response>
    /// <response code="429"><c>AGUARDE_NOVO_CODIGO</c> (60 segundos entre envios) ou limite de requisições.</response>
    /// <response code="502"><c>SMS_INDISPONIVEL</c>.</response>
    [HttpPost("telefone/codigo")]
    [EnableRateLimiting(LimitacaoExtensions.CodigoSms)]
    [ProducesResponseType<CodigoEnviadoResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<IActionResult> EnviarCodigo(CancellationToken cancellationToken) =>
        Accepted(await usuarios.EnviarCodigoTelefoneAsync(User.ObterUsuarioId(), cancellationToken));

    /// <summary>Confirma o telefone com o código recebido (E08).</summary>
    /// <response code="422"><c>CODIGO_INVALIDO</c>, <c>CODIGO_EXPIRADO</c> ou <c>TENTATIVAS_EXCEDIDAS</c> (máx. 5 tentativas).</response>
    [HttpPost("telefone/verificar")]
    [ProducesResponseType<TelefoneVerificadoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> VerificarTelefone([FromBody] VerificarTelefoneRequest requisicao, CancellationToken cancellationToken) =>
        Ok(await usuarios.VerificarTelefoneAsync(User.ObterUsuarioId(), requisicao, cancellationToken));

    /// <summary>Ativa ou revoga o aceite para receber SMS (E09). Exige telefone verificado.</summary>
    /// <response code="422"><c>TELEFONE_NAO_VERIFICADO</c>.</response>
    [HttpPut("preferencias-sms")]
    [ProducesResponseType<PreferenciasSmsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> DefinirPreferenciasSms([FromBody] PreferenciasSmsRequest requisicao, CancellationToken cancellationToken) =>
        Ok(await usuarios.DefinirPreferenciasSmsAsync(User.ObterUsuarioId(), requisicao, cancellationToken));

    /// <summary>Cadastra o CNPJ do organizador (E40), conferindo na Receita se está ativo. Necessário para criar eventos (RN24).</summary>
    /// <response code="400">CNPJ ausente ou com dígitos verificadores inválidos.</response>
    /// <response code="409"><c>CNPJ_JA_CADASTRADO</c>: o CNPJ pertence a outra conta.</response>
    /// <response code="422"><c>CNPJ_NAO_ENCONTRADO</c> ou <c>CNPJ_INATIVO</c>.</response>
    /// <response code="429">Limite de consultas excedido.</response>
    /// <response code="502"><c>CONSULTA_CNPJ_INDISPONIVEL</c>.</response>
    [HttpPut("cnpj")]
    [EnableRateLimiting(LimitacaoExtensions.ConsultaCnpj)]
    [ProducesResponseType<PerfilResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<IActionResult> DefinirCnpj([FromBody] DefinirCnpjRequest requisicao, CancellationToken cancellationToken) =>
        Ok(await usuarios.DefinirCnpjAsync(User.ObterUsuarioId(), requisicao, cancellationToken));
}
