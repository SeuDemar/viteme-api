namespace Viteme.Application.Excecoes;

/// <summary>A linha foi alterada por outra requisição desde a leitura (concorrência otimista, RI11).</summary>
public class ConflitoDeConcorrenciaException(Exception inner)
    : Exception("A versão do registro está desatualizada.", inner);
