namespace Viteme.Domain.Excecoes;

/// <summary>
/// Violação de regra de negócio com código estável. A API converte em ProblemDetails
/// com o status informado e o campo adicional <c>codigo</c>.
/// </summary>
public class RegraDeNegocioException(string codigo, string titulo, string detalhe, int statusHttp = 422)
    : Exception(detalhe)
{
    public string Codigo { get; } = codigo;

    public string Titulo { get; } = titulo;

    public int StatusHttp { get; } = statusHttp;
}
