using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class ContaRecebimentoRepository(VitemeDbContext contexto) : IContaRecebimentoRepository
{
    public Task<ContaRecebimento?> ObterPorUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        contexto.ContasRecebimento.FirstOrDefaultAsync(c => c.UsuarioId == usuarioId, cancellationToken);

    public Task<ContaRecebimento?> ObterPorContaGatewayAsync(string contaGatewayId, CancellationToken cancellationToken = default) =>
        contexto.ContasRecebimento.FirstOrDefaultAsync(c => c.ContaGatewayId == contaGatewayId, cancellationToken);

    public async Task<IReadOnlyList<ContaRecebimento>> ListarPendentesAsync(int limite, CancellationToken cancellationToken = default) =>
        await contexto.ContasRecebimento
            .Where(c => c.Situacao == SituacaoContaRecebimento.Pendente)
            .OrderBy(c => c.AtualizadoEm)
            .Take(limite)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContaRecebimento>> ListarAprovadasSemGarantiaAsync(int limite, CancellationToken cancellationToken = default) =>
        await contexto.ContasRecebimento
            .Where(c => c.Situacao == SituacaoContaRecebimento.Aprovada && c.GarantiaHabilitadaEm == null)
            .OrderBy(c => c.AprovadaEm)
            .Take(limite)
            .ToListAsync(cancellationToken);

    public void Adicionar(ContaRecebimento conta) => contexto.ContasRecebimento.Add(conta);
}
