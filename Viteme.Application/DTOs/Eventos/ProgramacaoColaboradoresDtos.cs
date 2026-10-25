using System.ComponentModel.DataAnnotations;

namespace Viteme.Application.DTOs.Eventos;

/// <summary>E18/E19 — atividade da programação. Em evento publicado, o motivo é obrigatório (RN13).</summary>
public sealed class AtividadeRequest : IValidatableObject
{
    [Required(ErrorMessage = "Informe o título da atividade.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "O título deve ter entre 3 e 150 caracteres.")]
    public string Titulo { get; init; } = string.Empty;

    [StringLength(120, ErrorMessage = "O palestrante deve ter no máximo 120 caracteres.")]
    public string? Palestrante { get; init; }

    /// <summary>Sala, se diferente do local do evento.</summary>
    [StringLength(200, ErrorMessage = "O local deve ter no máximo 200 caracteres.")]
    public string? Local { get; init; }

    [Required(ErrorMessage = "Informe o início da atividade.")]
    public DateTimeOffset? Inicio { get; init; }

    [Required(ErrorMessage = "Informe o término da atividade.")]
    public DateTimeOffset? Fim { get; init; }

    [StringLength(300, MinimumLength = 10, ErrorMessage = "O motivo deve ter entre 10 e 300 caracteres.")]
    public string? MotivoAlteracao { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Inicio is { } inicio && Fim is { } fim && fim <= inicio)
        {
            yield return new ValidationResult("O término deve ser posterior ao início.", [nameof(Fim)]);
        }
    }
}

/// <summary>E20 — motivo da remoção, obrigatório quando o evento está publicado.</summary>
public sealed class MotivoQuery
{
    [StringLength(300, MinimumLength = 10, ErrorMessage = "O motivo deve ter entre 10 e 300 caracteres.")]
    public string? Motivo { get; init; }
}

/// <summary>E22 — colaborador identificado pelo e-mail de um usuário cadastrado.</summary>
public sealed class ColaboradorRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [StringLength(160, ErrorMessage = "O e-mail deve ter no máximo 160 caracteres.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string Email { get; init; } = string.Empty;
}

public sealed record ColaboradorResponse(Guid UsuarioId, string Nome, string Email, DateTime AdicionadoEm);
