using System.ComponentModel.DataAnnotations;
using Viteme.Domain.Enums;

namespace Viteme.Application.DTOs.Denuncias;

/// <summary>E37 — denúncia de um evento indevido. A descrição é obrigatória quando o motivo é <c>OUTRO</c>.</summary>
public sealed class DenunciarEventoRequest : IValidatableObject
{
    /// <summary><c>EVENTO_FALSO</c>, <c>GOLPE</c>, <c>CONTEUDO_IMPROPRIO</c>, <c>SPAM</c> ou <c>OUTRO</c>.</summary>
    [Required(ErrorMessage = "Informe o motivo da denúncia.")]
    public MotivoDenuncia? Motivo { get; init; }

    [StringLength(500, ErrorMessage = "A descrição deve ter no máximo 500 caracteres.")]
    public string? Descricao { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Motivo == MotivoDenuncia.Outro && string.IsNullOrWhiteSpace(Descricao))
        {
            yield return new ValidationResult("Descreva o motivo quando escolher \"Outro\".", [nameof(Descricao)]);
        }
    }
}
