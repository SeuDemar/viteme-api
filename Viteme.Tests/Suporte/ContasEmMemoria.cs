using System.Collections.Concurrent;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;

namespace Viteme.Tests.Suporte;

/// <summary>
/// Subconta e garantia falsas para os testes de integração. As Fases 2 e 3 começam desligadas (como na configuração
/// padrão); cada teste liga o que precisa.
/// </summary>
public class ContasEmMemoria : IContasRecebimento
{
    public bool SubcontasHabilitadas { get; set; }

    public bool GarantiaHabilitada { get; set; }

    /// <summary>Situação devolvida pela consulta ao "gateway" (padrão: pendente).</summary>
    public SituacaoContaRecebimento SituacaoNoGateway { get; set; } = SituacaoContaRecebimento.Pendente;

    public bool FalharAoCriar { get; set; }

    /// <summary>Quando definido, o "gateway" recusa os dados com este motivo (ex.: CNPJ já em uso no Asaas).</summary>
    public string? RecusarComMotivo { get; set; }

    public ConcurrentQueue<DadosSubconta> Criadas { get; } = new();

    /// <summary>Chave de API de cada subconta criada, para conferir que só a cifrada vai para o banco.</summary>
    public ConcurrentQueue<string> ChavesEntregues { get; } = new();

    public ConcurrentQueue<string> GarantiasHabilitadas { get; } = new();

    /// <summary>Garantia de cada cobrança no "gateway" (id da transação → id da garantia). Sem entrada, a cobrança não ficou retida.</summary>
    public ConcurrentDictionary<string, string> Garantias { get; } = new();

    public ConcurrentQueue<(string IdTransacao, string GarantiaId, string Chave)> Encerradas { get; } = new();

    public Task<SubcontaCriada> CriarSubcontaAsync(DadosSubconta dados, CancellationToken cancellationToken = default)
    {
        if (FalharAoCriar)
        {
            throw new HttpRequestException("gateway fora do ar");
        }

        if (RecusarComMotivo is not null)
        {
            throw new Viteme.Application.Excecoes.DadosRecusadosPeloGatewayException(RecusarComMotivo);
        }

        Criadas.Enqueue(dados);
        var sufixo = Guid.NewGuid().ToString("N")[..12];
        var chave = $"chave_{sufixo}";
        ChavesEntregues.Enqueue(chave);
        return Task.FromResult(new SubcontaCriada($"acc_{sufixo}", $"wallet_{sufixo}", chave, $"https://onboarding/{sufixo}"));
    }

    public Task<SituacaoSubconta> ConsultarSituacaoAsync(string chaveApiSubconta, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SituacaoSubconta(SituacaoNoGateway, null));

    public NotificacaoConta? InterpretarWebhookConta(string corpoBruto) => null;

    public Task HabilitarGarantiaAsync(string contaId, CancellationToken cancellationToken = default)
    {
        GarantiasHabilitadas.Enqueue(contaId);
        return Task.CompletedTask;
    }

    public Task<string?> ObterGarantiaAsync(string idTransacao, string chaveApiSubconta, CancellationToken cancellationToken = default) =>
        Task.FromResult(Garantias.TryGetValue(idTransacao, out var id) ? id : null);

    public Task EncerrarGarantiaAsync(string idTransacao, string garantiaId, string chaveApiSubconta, CancellationToken cancellationToken = default)
    {
        Encerradas.Enqueue((idTransacao, garantiaId, chaveApiSubconta));
        return Task.CompletedTask;
    }
}
