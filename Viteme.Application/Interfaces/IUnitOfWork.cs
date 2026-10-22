namespace Viteme.Application.Interfaces;

/// <summary>Confirma as alterações pendentes em uma única transação.</summary>
public interface IUnitOfWork
{
    Task<int> SalvarAsync(CancellationToken cancellationToken = default);
}
