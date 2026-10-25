using System.ComponentModel.DataAnnotations;
using Viteme.Application.Validacoes;

namespace Viteme.Application.DTOs.Auth;

/// <summary>E01 — dados do cadastro.</summary>
public sealed record CadastroRequest
{
    /// <example>Maria Souza</example>
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "O nome deve ter entre 3 e 120 caracteres.")]
    public string Nome { get; init; } = string.Empty;

    /// <example>maria@exemplo.com</example>
    [Required(ErrorMessage = "Informe o e-mail.")]
    [StringLength(160, ErrorMessage = "O e-mail deve ter no máximo 160 caracteres.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string Email { get; init; } = string.Empty;

    /// <example>Senha123</example>
    [Required(ErrorMessage = "Informe a senha.")]
    [SenhaForte]
    public string Senha { get; init; } = string.Empty;

    /// <example>+5544999990000</example>
    [TelefoneE164]
    public string? Telefone { get; init; }
}

public sealed record CadastroResponse(Guid Id, string Nome, string Email);

/// <summary>E02 — credenciais.</summary>
public sealed record LoginRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    public string Senha { get; init; } = string.Empty;
}

public sealed record UsuarioResumo(Guid Id, string Nome, string Email, bool TelefoneVerificado, bool AceitaSms);

/// <summary>Corpo da resposta de login. O refresh token vai somente no cookie HttpOnly.</summary>
public sealed record LoginResponse(string TokenAcesso, DateTime ExpiraEm, UsuarioResumo Usuario);

/// <summary>Corpo da resposta de renovação (E03).</summary>
public sealed record RenovarResponse(string TokenAcesso, DateTime ExpiraEm);

/// <summary>Resultado interno do login: o controller grava o refresh token no cookie.</summary>
public sealed record LoginResultado(
    string TokenAcesso, DateTime ExpiraEm, UsuarioResumo Usuario, string RefreshToken, DateTime RefreshExpiraEm);

/// <summary>Resultado interno da renovação.</summary>
public sealed record RenovarResultado(string TokenAcesso, DateTime ExpiraEm, string RefreshToken, DateTime RefreshExpiraEm);
