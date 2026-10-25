using System.ComponentModel.DataAnnotations;
using Viteme.Application.Validacoes;
using Viteme.Domain.Enums;

namespace Viteme.Application.DTOs.Eventos;

/// <summary>E10 — filtros e paginação do mural.</summary>
public sealed class MuralFiltro : IValidatableObject
{
    /// <summary>Texto procurado no título, na descrição ou no local.</summary>
    [StringLength(100, ErrorMessage = "A busca deve ter no máximo 100 caracteres.")]
    public string? Busca { get; init; }

    [StringLength(60, ErrorMessage = "A categoria deve ter no máximo 60 caracteres.")]
    public string? Categoria { get; init; }

    /// <summary>Início do intervalo, por data de início do evento (inclusive).</summary>
    public DateOnly? DataDe { get; init; }

    /// <summary>Fim do intervalo, por data de início do evento (inclusive, o dia inteiro).</summary>
    public DateOnly? DataAte { get; init; }

    /// <summary><c>true</c> só eventos gratuitos; <c>false</c> só eventos pagos.</summary>
    public bool? Gratuito { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "A página deve ser 1 ou maior.")]
    public int Pagina { get; init; } = 1;

    [Range(1, 50, ErrorMessage = "O tamanho da página deve estar entre 1 e 50.")]
    public int TamanhoPagina { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DataDe is { } de && DataAte is { } ate && de > ate)
        {
            yield return new ValidationResult("A data inicial deve ser menor ou igual à data final.", [nameof(DataDe)]);
        }
    }
}

/// <summary>E12 — dados do evento. Datas em ISO 8601 (ex.: <c>2026-11-05T19:00:00Z</c>).</summary>
public class EventoRequest : IValidatableObject
{
    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(150, MinimumLength = 5, ErrorMessage = "O título deve ter entre 5 e 150 caracteres.")]
    public string Titulo { get; init; } = string.Empty;

    [Required(ErrorMessage = "Informe a descrição.")]
    [StringLength(5000, ErrorMessage = "A descrição deve ter no máximo 5000 caracteres.")]
    public string Descricao { get; init; } = string.Empty;

    /// <example>semana acadêmica</example>
    [Required(ErrorMessage = "Informe a categoria.")]
    [StringLength(60, ErrorMessage = "A categoria deve ter no máximo 60 caracteres.")]
    public string Categoria { get; init; } = string.Empty;

    /// <summary>Local físico ou link.</summary>
    [Required(ErrorMessage = "Informe o local.")]
    [StringLength(200, ErrorMessage = "O local deve ter no máximo 200 caracteres.")]
    public string Local { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "A URL da imagem deve ter no máximo 500 caracteres.")]
    [UrlHttps]
    public string? ImagemUrl { get; init; }

    [Required(ErrorMessage = "Informe a data de início.")]
    public DateTimeOffset? DataInicio { get; init; }

    [Required(ErrorMessage = "Informe a data de término.")]
    public DateTimeOffset? DataFim { get; init; }

    [Required(ErrorMessage = "Informe o início das inscrições.")]
    public DateTimeOffset? InscricaoInicio { get; init; }

    [Required(ErrorMessage = "Informe o fim das inscrições.")]
    public DateTimeOffset? InscricaoFim { get; init; }

    /// <summary>Vagas. Nulo significa ilimitado.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A capacidade deve ser maior que zero.")]
    public int? Capacidade { get; init; }

    /// <summary>Preço em reais (0 = gratuito), com no máximo 2 casas decimais.</summary>
    [Required(ErrorMessage = "Informe o preço (0 para evento gratuito).")]
    [Range(typeof(decimal), "0", "99999.99", ErrorMessage = "O preço deve estar entre 0 e 99.999,99.")]
    [DuasCasasDecimais]
    public decimal? Preco { get; init; }

    [Range(1, short.MaxValue, ErrorMessage = "A carga horária deve ser maior que zero.")]
    public short? CargaHoraria { get; init; }

    /// <summary>RN05: término após o início; inscrições terminam até o início do evento.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DataInicio is not { } inicio || DataFim is not { } fim
            || InscricaoInicio is not { } inscricaoInicio || InscricaoFim is not { } inscricaoFim)
        {
            yield break;
        }

        if (fim <= inicio)
        {
            yield return new ValidationResult("A data de término deve ser posterior à data de início.", [nameof(DataFim)]);
        }

        if (inscricaoFim <= inscricaoInicio)
        {
            yield return new ValidationResult("O fim das inscrições deve ser posterior ao início das inscrições.", [nameof(InscricaoFim)]);
        }
        else if (inscricaoFim > inicio)
        {
            yield return new ValidationResult("As inscrições devem terminar até o início do evento.", [nameof(InscricaoFim)]);
        }
    }
}

/// <summary>E13 — edição do evento. O motivo é obrigatório quando título, data, horário ou local de um evento publicado mudam (RN13).</summary>
public sealed class EventoAtualizarRequest : EventoRequest
{
    [StringLength(300, MinimumLength = 10, ErrorMessage = "O motivo deve ter entre 10 e 300 caracteres.")]
    public string? MotivoAlteracao { get; init; }
}

/// <summary>E15 — cancelamento do evento.</summary>
public sealed class CancelarEventoRequest
{
    [Required(ErrorMessage = "Informe o motivo do cancelamento.")]
    [StringLength(300, MinimumLength = 10, ErrorMessage = "O motivo deve ter entre 10 e 300 caracteres.")]
    public string Motivo { get; init; } = string.Empty;
}

public sealed record StatusEventoResponse(StatusEvento Status);

public sealed record EventoResumoResponse(
    Guid Id, string Titulo, string Categoria, string Local, DateTime DataInicio, decimal Preco,
    int? VagasRestantes, string? ImagemUrl, bool AlteradoRecentemente);

public sealed record EventoMeuResponse(
    Guid Id, string Titulo, string Categoria, string Local, DateTime DataInicio, DateTime DataFim,
    decimal Preco, StatusEvento Status, PapelEvento Papel, string? ImagemUrl);

public sealed record OrganizadorResumo(Guid Id, string Nome);

public sealed record AtividadeResponse(Guid Id, string Titulo, string? Palestrante, string? Local, DateTime Inicio, DateTime Fim);

public sealed record AlteracaoResponse(string Campo, string? ValorAnterior, string? ValorNovo, string Motivo, DateTime CriadoEm);

public sealed record MinhaInscricaoResumo(Guid Id, StatusInscricao Status, string CodigoCheckin, DateTime? ReservadaAte);

/// <summary>E11 — detalhes do evento. Os campos de relação com o usuário só têm valor para quem está autenticado.</summary>
public sealed record EventoDetalheResponse(
    Guid Id, string Titulo, string Descricao, string Categoria, string Local, string? ImagemUrl,
    DateTime DataInicio, DateTime DataFim, DateTime InscricaoInicio, DateTime InscricaoFim,
    int? Capacidade, decimal Preco, short? CargaHoraria, StatusEvento Status, DateTime? ReabertoEm,
    int? VagasRestantes, OrganizadorResumo Organizador,
    IReadOnlyList<AtividadeResponse> Atividades, IReadOnlyList<AlteracaoResponse> UltimasAlteracoes,
    MinhaInscricaoResumo? MinhaInscricao, bool SouOrganizador, bool SouColaborador, bool Interessado);

/// <summary>Evento com a versão (<c>xmin</c>) lida, que a API devolve como ETag.</summary>
public sealed record EventoDetalheResultado(EventoDetalheResponse Dados, uint Versao);
