using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Externos;

namespace Viteme.Tests.Suporte;

/// <summary>Gateway falso: registra as chamadas, pode falhar sob comando e assina webhooks com um segredo conhecido.</summary>
public class GatewayEmMemoria() : GatewayPagamentoStub(
    Options.Create(new GatewayStubOptions { SegredoWebhook = Segredo, HabilitarSimulador = true }),
    NullLogger<GatewayPagamentoStub>.Instance)
{
    public const string Segredo = "segredo-do-gateway-de-teste";

    public ConcurrentQueue<PedidoCobranca> Cobrancas { get; } = new();

    public ConcurrentQueue<(string IdTransacao, decimal Valor)> Estornos { get; } = new();

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

    public override Task SolicitarEstornoAsync(string idTransacao, decimal valor, CancellationToken cancellationToken = default)
    {
        if (FalharEstorno)
        {
            throw new InvalidOperationException("estorno recusado pelo gateway");
        }

        Estornos.Enqueue((idTransacao, valor));
        return Task.CompletedTask;
    }
}
