using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class PagamentoRepository(VitemeDbContext contexto) : IPagamentoRepository
{
    public Task<Pagamento?> ObterPorIdAsync(Guid pagamentoId, CancellationToken cancellationToken = default) =>
        contexto.Pagamentos.FirstOrDefaultAsync(p => p.Id == pagamentoId, cancellationToken);

    public Task<Pagamento?> ObterPorIdTransacaoAsync(string idTransacao, CancellationToken cancellationToken = default) =>
        contexto.Pagamentos.FirstOrDefaultAsync(p => p.IdTransacaoGateway == idTransacao, cancellationToken);

    public async Task<IReadOnlyList<Pagamento>> ListarDaInscricaoAsync(Guid inscricaoId, CancellationToken cancellationToken = default) =>
        await contexto.Pagamentos
            .Where(p => p.InscricaoId == inscricaoId)
            .OrderByDescending(p => p.CriadoEm)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Pagamento>> ListarPendentesDoEventoAsync(Guid eventoId, CancellationToken cancellationToken = default) =>
        await (from p in contexto.Pagamentos
               where p.Status == StatusPagamento.Pendente
               join i in contexto.Inscricoes on p.InscricaoId equals i.Id
               where i.EventoId == eventoId
               select p)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PagamentoParaEstorno>> ListarParaEstornoAsync(
        Guid? eventoId, int limite, CancellationToken cancellationToken = default)
    {
        var consulta = from p in contexto.Pagamentos.AsNoTracking()
                       where p.Status == StatusPagamento.Aprovado
                       join i in contexto.Inscricoes.AsNoTracking() on p.InscricaoId equals i.Id
                       where i.Status != StatusInscricao.Confirmada
                       select new { p.Id, p.CriadoEm, i.UsuarioId, i.EventoId };

        if (eventoId is { } id)
        {
            consulta = consulta.Where(x => x.EventoId == id);
        }

        var itens = await consulta.OrderBy(x => x.CriadoEm).Take(limite).ToListAsync(cancellationToken);
        return itens.Select(x => new PagamentoParaEstorno(x.Id, x.UsuarioId, x.EventoId)).ToList();
    }

    public void Adicionar(Pagamento pagamento) => contexto.Pagamentos.Add(pagamento);
}
