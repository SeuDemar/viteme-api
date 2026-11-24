using System.ComponentModel.DataAnnotations;
using Viteme.Application.Validacoes;
using Viteme.Domain.Enums;

namespace Viteme.Application.DTOs.Usuarios;

/// <summary>
/// E41 — dados da empresa para abrir a conta de recebimento (subconta no gateway). CNPJ, razão social e e-mail vêm do perfil.
/// Campos conforme <c>POST /v3/accounts</c> do Asaas para PJ (confirmar Q3 na conta sandbox PJ).
/// </summary>
public sealed record CriarContaRecebimentoRequest
{
    /// <summary><c>MEI</c>, <c>LIMITED</c> (LTDA), <c>INDIVIDUAL</c> (empresário individual) ou <c>ASSOCIATION</c>.</summary>
    /// <example>LIMITED</example>
    [Required(ErrorMessage = "Informe o tipo de empresa.")]
    [RegularExpression("^(MEI|LIMITED|INDIVIDUAL|ASSOCIATION)$", ErrorMessage = "Tipo de empresa inválido.")]
    public string TipoEmpresa { get; init; } = string.Empty;

    /// <example>+5544999990000</example>
    [Required(ErrorMessage = "Informe o telefone.")]
    [TelefoneE164]
    public string Telefone { get; init; } = string.Empty;

    /// <summary>Faturamento ou renda mensal da empresa, em reais (exigido pelo gateway).</summary>
    /// <example>5000</example>
    [Required(ErrorMessage = "Informe o faturamento mensal.")]
    [Range(typeof(decimal), "1", "100000000", ErrorMessage = "Informe um faturamento mensal válido.")]
    public decimal? FaturamentoMensal { get; init; }

    /// <example>87020-000</example>
    [Required(ErrorMessage = "Informe o CEP.")]
    [RegularExpression(@"^\d{5}-?\d{3}$", ErrorMessage = "Informe o CEP com 8 dígitos.")]
    public string Cep { get; init; } = string.Empty;

    [Required(ErrorMessage = "Informe o endereço.")]
    [StringLength(200, ErrorMessage = "O endereço deve ter até 200 caracteres.")]
    public string Endereco { get; init; } = string.Empty;

    [Required(ErrorMessage = "Informe o número.")]
    [StringLength(20, ErrorMessage = "O número deve ter até 20 caracteres.")]
    public string Numero { get; init; } = string.Empty;

    [StringLength(100, ErrorMessage = "O complemento deve ter até 100 caracteres.")]
    public string? Complemento { get; init; }

    [Required(ErrorMessage = "Informe o bairro.")]
    [StringLength(100, ErrorMessage = "O bairro deve ter até 100 caracteres.")]
    public string Bairro { get; init; } = string.Empty;
}

/// <summary>E41/E42 — situação da conta de recebimento. <c>UrlOnboarding</c> é onde o organizador envia os documentos.</summary>
public sealed record ContaRecebimentoResponse(
    SituacaoContaRecebimento Situacao, string? UrlOnboarding, DateTime CriadoEm, DateTime? AprovadaEm, bool GarantiaHabilitada);
