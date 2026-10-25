namespace Viteme.Application.Excecoes;

/// <summary>Restrição UNIQUE violada ao salvar (corrida entre duas requisições). Os services convertem em erro de negócio.</summary>
public class ViolacaoDeUnicidadeException(string? restricao, Exception inner)
    : Exception($"Violação de unicidade na restrição '{restricao}'.", inner)
{
    public string? Restricao { get; } = restricao;
}
