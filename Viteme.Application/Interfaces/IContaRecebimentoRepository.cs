using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface IContaRecebimentoRepository
{
    Task<ContaRecebimento?> ObterPorUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    Task<ContaRecebimento?> ObterPorContaGatewayAsync(string contaGatewayId, CancellationToken cancellationToken = default);

    /// <summary>Contas ainda em análise no gateway, para a tarefa periódica consultar (caso um webhook se perca).</summary>
    Task<IReadOnlyList<ContaRecebimento>> ListarPendentesAsync(int limite, CancellationToken cancellationToken = default);

    /// <summary>Contas aprovadas em que a garantia (Fase 3) ainda não foi ligada no gateway.</summary>
    Task<IReadOnlyList<ContaRecebimento>> ListarAprovadasSemGarantiaAsync(int limite, CancellationToken cancellationToken = default);

    void Adicionar(ContaRecebimento conta);
}
