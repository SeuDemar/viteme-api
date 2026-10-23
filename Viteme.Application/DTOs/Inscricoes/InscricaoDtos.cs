using System.ComponentModel.DataAnnotations;
using Viteme.Domain.Enums;

namespace Viteme.Application.DTOs.Inscricoes;

/// <summary>E25 — recorte das inscrições do usuário.</summary>
public enum SituacaoInscricoes
{
    Todas,
    Proximas,
    Passadas,
}

public sealed record PagamentoDaInscricao(Guid Id, decimal Valor, string? UrlPagamento);

/// <summary>E24 — resultado da inscrição. O código de check-in só aparece quando a inscrição está confirmada.</summary>
public sealed record InscricaoCriadaResponse(
    Guid Id, StatusInscricao Status, string? CodigoCheckin, DateTime? ReservadaAte, PagamentoDaInscricao? Pagamento);

public sealed record EventoDaInscricao(Guid Id, string Titulo, DateTime DataInicio, string Local);

public sealed record MinhaInscricaoResponse(
    Guid Id, EventoDaInscricao Evento, StatusInscricao Status, string CodigoCheckin, DateTime? ReservadaAte,
    bool PresencaConfirmada, bool CertificadoDisponivel);

public sealed record CancelarInscricaoResponse(StatusInscricao Status, bool EstornoSolicitado);

/// <summary>E27 — filtros e paginação dos inscritos.</summary>
public sealed class InscritosFiltro : IValidatableObject
{
    /// <summary>Situação da inscrição, ex.: <c>CONFIRMADA</c> ou <c>PENDENTE_PAGAMENTO</c>.</summary>
    public string? Status { get; init; }

    /// <summary>Texto procurado no nome ou no e-mail.</summary>
    [StringLength(100, ErrorMessage = "A busca deve ter no máximo 100 caracteres.")]
    public string? Busca { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "A página deve ser 1 ou maior.")]
    public int Pagina { get; init; } = 1;

    [Range(1, 50, ErrorMessage = "O tamanho da página deve estar entre 1 e 50.")]
    public int TamanhoPagina { get; init; } = 20;

    /// <summary>Status já convertido; nulo se não informado.</summary>
    public StatusInscricao? StatusFiltro =>
        string.IsNullOrWhiteSpace(Status) ? null
        : Enum.TryParse<StatusInscricao>(Status.Replace("_", string.Empty), ignoreCase: true, out var valor) ? valor : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Status) && StatusFiltro is null)
        {
            yield return new ValidationResult(
                "Situação inválida. Use PENDENTE_PAGAMENTO, CONFIRMADA, CANCELADA ou EXPIRADA.", [nameof(Status)]);
        }
    }
}

/// <summary>E27 — o telefone não é exibido (minimização de dados).</summary>
public sealed record InscritoResponse(
    Guid InscricaoId, string Nome, string Email, StatusInscricao Status, bool PresencaConfirmada, DateTime? CheckinEm);

/// <summary>E28 — código de 12 caracteres alfanuméricos apresentado pelo participante.</summary>
public sealed class CheckinRequest
{
    [Required(ErrorMessage = "Informe o código de check-in.")]
    [RegularExpression("^[A-Za-z0-9]{12}$", ErrorMessage = "O código deve ter 12 caracteres alfanuméricos.")]
    public string CodigoCheckin { get; init; } = string.Empty;
}

public sealed record CheckinResponse(string Nome, DateTime CheckinEm);
