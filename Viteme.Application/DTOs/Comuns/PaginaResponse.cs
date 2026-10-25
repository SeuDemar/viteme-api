namespace Viteme.Application.DTOs.Comuns;

/// <summary>Resposta paginada padrão da API: <c>{ itens, pagina, tamanhoPagina, total }</c>.</summary>
public sealed record PaginaResponse<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoPagina, int Total);
