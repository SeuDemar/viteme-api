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

    public async Task<IReadOnlyList<PagamentoParaReconciliacao>> ListarParaReconciliacaoAsync(
        DateTime criadosAntes, DateTime criadosDepois, int limite, CancellationToken cancellationToken = default)
    {
        var itens = await contexto.Pagamentos.AsNoTracking()
            .Where(p => (p.Status == StatusPagamento.Pendente || p.Status == StatusPagamento.Expirado)
                        && p.IdTransacaoGateway != null && p.CriadoEm <= criadosAntes && p.CriadoEm >= criadosDepois)
            .OrderBy(p => p.CriadoEm)
            .Take(limite)
            .Select(p => new { p.Id, p.IdTransacaoGateway, p.Valor, p.ContaRecebimentoId })
            .ToListAsync(cancellationToken);
        return itens.Select(x => new PagamentoParaReconciliacao(x.Id, x.IdTransacaoGateway!, x.Valor, x.ContaRecebimentoId)).ToList();
    }

    public async Task<IReadOnlyList<PagamentoComGarantia>> ListarGarantiasParaLiberarAsync(
        DateTime terminoAntes, int limite, CancellationToken cancellationToken = default)
    {
        var itens = await (from p in contexto.Pagamentos.AsNoTracking()
                           where p.Status == StatusPagamento.Aprovado && p.GarantiaLiberadaEm == null && p.IdTransacaoGateway != null
                           join i in contexto.Inscricoes.AsNoTracking() on p.InscricaoId equals i.Id
                           where i.Status == StatusInscricao.Confirmada
                           join e in contexto.Eventos.AsNoTracking() on i.EventoId equals e.Id
                           where e.Status == StatusEvento.Encerrado && e.DataFim < terminoAntes
                           orderby e.DataFim, p.CriadoEm
                           select new { p.Id, p.IdTransacaoGateway, p.GarantiaId, p.ContaRecebimentoId })
            .Take(limite)
            .ToListAsync(cancellationToken);
        return itens.Select(x => new PagamentoComGarantia(x.Id, x.IdTransacaoGateway!, x.GarantiaId, x.ContaRecebimentoId)).ToList();
    }

    public void Adicionar(Pagamento pagamento) => contexto.Pagamentos.Add(pagamento);
}
