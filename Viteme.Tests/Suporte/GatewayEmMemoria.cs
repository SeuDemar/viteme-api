using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Externos;

namespace Viteme.Tests.Suporte;

/// <summary>Gateway falso: registra as chamadas, pode falhar sob comando e assina webhooks com um segredo conhecido.</summary>
public class GatewayEmMemoria() : GatewayPagamentoStub(
    Options.Create(new GatewayStubOptions { SegredoWebhook = Segredo, HabilitarSimulador = true }),
    NullLogger<GatewayPagamentoStub>.Instance), IConfirmacaoSandbox
{
    public const string Segredo = "segredo-do-gateway-de-teste";

    public ConcurrentQueue<PedidoCobranca> Cobrancas { get; } = new();

    public ConcurrentQueue<(string IdTransacao, decimal Valor)> Estornos { get; } = new();

    /// <summary>Chave da subconta usada em cada estorno (nula para cobrança da raiz), na mesma ordem de <see cref="Estornos"/>.</summary>
    public ConcurrentQueue<string?> ChavesDosEstornos { get; } = new();

    /// <summary>Situação de cada cobrança no "gateway" (padrão: pendente), lida pela consulta da reconciliação.</summary>
    public ConcurrentDictionary<string, Viteme.Domain.Enums.StatusPagamento> StatusNoGateway { get; } = new();

    /// <summary>Quando definido, o "gateway" devolve este Pix na consulta do QR Code (padrão: indisponível).</summary>
    public PixCobranca? Pix { get; set; }

    public override Task<PixCobranca?> ObterPixAsync(string idTransacao, string? chaveRecebedor = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Pix);

    public bool ExigirDocumento { get; set; }

    public override bool ExigeDocumentoDoPagador => ExigirDocumento;

    public override Task<Viteme.Domain.Enums.StatusPagamento> ConsultarCobrancaAsync(string idTransacao, string? chaveRecebedor = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(StatusNoGateway.TryGetValue(idTransacao, out var status) ? status : Viteme.Domain.Enums.StatusPagamento.Pendente);

    /// <summary>Faz o "gateway" se comportar como o Asaas sandbox no simulador (padrão: desligado, como o stub).</summary>
    public bool Disponivel { get; set; }

    public ConcurrentQueue<string> ConfirmadasNoSandbox { get; } = new();

    public Task ConfirmarPagamentoAsync(string idTransacao, string? chaveRecebedor = null, CancellationToken cancellationToken = default)
    {
        ConfirmadasNoSandbox.Enqueue(idTransacao);
        StatusNoGateway[idTransacao] = Viteme.Domain.Enums.StatusPagamento.Aprovado;
        return Task.CompletedTask;
    }

    public bool FalharAoCriar { get; set; }

    public bool FalharEstorno { get; set; }

    public override async Task<CobrancaCriada> CriarCobrancaAsync(PedidoCobranca pedido, CancellationToken cancellationToken = default)
    {
        if (FalharAoCriar)
        {
            throw new InvalidOperationException("gateway fora do ar");
        }

        Cobrancas.Enqueue(pedido);
        return await base.CriarCobrancaAsync(pedido, cancellationToken);
    }

    public override Task SolicitarEstornoAsync(string idTransacao, decimal valor, string? chaveRecebedor = null, CancellationToken cancellationToken = default)
    {
        if (FalharEstorno)
        {
            throw new InvalidOperationException("estorno recusado pelo gateway");
        }

        Estornos.Enqueue((idTransacao, valor));
        ChavesDosEstornos.Enqueue(chaveRecebedor);
        return Task.CompletedTask;
    }
}
