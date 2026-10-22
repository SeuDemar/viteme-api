using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viteme.Api.Configuracao;
using Viteme.Application.DTOs.Auth;
using Viteme.Application.Services;

namespace Viteme.Api.Controllers;

/// <summary>Cadastro, login e ciclo de vida da sessão (token de acesso em memória + refresh token em cookie HttpOnly).</summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>Nome do cookie do refresh token, restrito à rota de autenticação.</summary>
    public const string CookieRefresh = "refresh_token";

    private const string RotaCookie = "/api/v1/auth";

    /// <summary>Cria uma conta de usuário (E01).</summary>
    /// <response code="201">Conta criada.</response>
    /// <response code="400">Dados inválidos (erros por campo em <c>errors</c>).</response>
    /// <response code="409"><c>EMAIL_JA_CADASTRADO</c>.</response>
    /// <response code="429">Limite de requisições excedido.</response>
    [HttpPost("cadastro")]
    [EnableRateLimiting(LimitacaoExtensions.Cadastro)]
    [ProducesResponseType<CadastroResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Cadastrar([FromBody] CadastroRequest requisicao, CancellationToken cancellationToken)
    {
        var resposta = await auth.CadastrarAsync(requisicao, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, resposta);
    }

    /// <summary>Autentica o usuário e emite os tokens (E02). O refresh token vai no cookie <c>refresh_token</c>.</summary>
    /// <response code="200">Autenticado.</response>
    /// <response code="401"><c>CREDENCIAIS_INVALIDAS</c> (mesma mensagem para e-mail inexistente e senha incorreta).</response>
    /// <response code="423"><c>CONTA_BLOQUEADA</c> após 5 tentativas inválidas seguidas (15 minutos).</response>
    /// <response code="429">Limite de 5 logins por minuto por IP.</response>
    [HttpPost("login")]
    [EnableRateLimiting(LimitacaoExtensions.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Login([FromBody] LoginRequest requisicao, CancellationToken cancellationToken)
    {
        var resultado = await auth.LoginAsync(requisicao, cancellationToken);

        GravarCookie(resultado.RefreshToken, resultado.RefreshExpiraEm);
        return Ok(new LoginResponse(resultado.TokenAcesso, resultado.ExpiraEm, resultado.Usuario));
    }

    /// <summary>Emite novo token de acesso a partir do cookie de renovação, com rotação (E03).</summary>
    /// <remarks>Reutilizar um refresh token já revogado revoga todos os tokens do usuário.</remarks>
    /// <response code="200">Novo token de acesso e novo cookie.</response>
    /// <response code="401"><c>TOKEN_INVALIDO</c>.</response>
    [HttpPost("renovar")]
    [ProducesResponseType<RenovarResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Renovar(CancellationToken cancellationToken)
    {
        var resultado = await auth.RenovarAsync(Request.Cookies[CookieRefresh], cancellationToken);

        GravarCookie(resultado.RefreshToken, resultado.RefreshExpiraEm);
        return Ok(new RenovarResponse(resultado.TokenAcesso, resultado.ExpiraEm));
    }

    /// <summary>Encerra a sessão (E04). Não exige token de acesso e é idempotente: sempre 204.</summary>
    /// <response code="204">Sessão encerrada e cookie removido.</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(Request.Cookies[CookieRefresh], cancellationToken);

        Response.Cookies.Delete(CookieRefresh, OpcoesCookie(null));
        return NoContent();
    }

    private void GravarCookie(string refreshToken, DateTime expiraEm) =>
        Response.Cookies.Append(CookieRefresh, refreshToken, OpcoesCookie(expiraEm));

    private static CookieOptions OpcoesCookie(DateTime? expiraEm) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RotaCookie,
        IsEssential = true,
        Expires = expiraEm is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiraEm.Value, DateTimeKind.Utc)),
    };
}
