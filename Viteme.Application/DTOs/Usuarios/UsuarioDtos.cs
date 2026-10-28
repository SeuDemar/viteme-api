using System.ComponentModel.DataAnnotations;
using Viteme.Application.Validacoes;

namespace Viteme.Application.DTOs.Usuarios;

/// <summary>E05 — perfil do usuário autenticado. <c>PodeOrganizar</c> diz se o CNPJ já foi verificado (RN24).</summary>
public sealed record PerfilResponse(
    Guid Id, string Nome, string Email, string? Telefone, bool TelefoneVerificado, bool AceitaSms, DateTime? AceiteSmsEm,
    string? Cnpj, string? RazaoSocial, DateTime? CnpjVerificadoEm, bool PodeOrganizar);

/// <summary>E06 — atualização de nome e telefone. Trocar o telefone zera a verificação e o aceite de SMS.</summary>
public sealed record AtualizarPerfilRequest
{
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "O nome deve ter entre 3 e 120 caracteres.")]
    public string Nome { get; init; } = string.Empty;

    [TelefoneE164]
    public string? Telefone { get; init; }
}

/// <summary>E07 — validade do código enviado por SMS.</summary>
public sealed record CodigoEnviadoResponse(DateTime ExpiraEm);

/// <summary>E08 — código de 6 dígitos recebido por SMS.</summary>
public sealed record VerificarTelefoneRequest
{
    /// <example>123456</example>
    [Required(ErrorMessage = "Informe o código.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "O código deve ter exatamente 6 dígitos numéricos.")]
    public string Codigo { get; init; } = string.Empty;
}

public sealed record TelefoneVerificadoResponse(bool TelefoneVerificado);

/// <summary>E09 — ativar ou revogar o aceite de SMS.</summary>
public sealed record PreferenciasSmsRequest
{
    [Required(ErrorMessage = "Informe se aceita receber SMS.")]
    public bool? AceitaSms { get; init; }
}

public sealed record PreferenciasSmsResponse(bool AceitaSms, DateTime? AceiteSmsEm);

/// <summary>E40 — CNPJ do organizador, com ou sem máscara (RN24).</summary>
public sealed record DefinirCnpjRequest
{
    /// <example>11.222.333/0001-81</example>
    [Required(ErrorMessage = "Informe o CNPJ.")]
    [Cnpj]
    public string Cnpj { get; init; } = string.Empty;
}
