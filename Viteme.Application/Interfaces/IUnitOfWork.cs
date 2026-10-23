namespace Viteme.Application.Interfaces;

/// <summary>Confirma as alterações pendentes e controla transações.</summary>
public interface IUnitOfWork
{
    Task<int> SalvarAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executa a operação em uma transação (commit se terminar, rollback se lançar exceção).
    /// Use para consultas com bloqueio de linha (<c>FOR UPDATE</c>) seguidas da gravação (RI10).
    /// </summary>
    Task<T> ExecutarEmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken cancellationToken = default);

    /// <summary>Descarta as entidades carregadas. Necessário antes de repetir uma operação após conflito de concorrência.</summary>
    void LimparRastreamento();
}
