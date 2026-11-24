using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;
using Viteme.Infrastructure.Externos;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>
/// Fase 3 de ponta a ponta no sandbox do Asaas, com os adaptadores reais: subconta, aprovação simulada, escrow, cobrança
/// criada na subconta, pagamento simulado, garantia retida e liberada. Opt-in (veja <see cref="FactAsaasSandboxAttribute"/>).
/// </summary>
public class AsaasSandboxTests
{
    private const string UrlBase = "https://api-sandbox.asaas.com/v3";

    private static IOptions<GatewayAsaasOptions> Opcoes() => Options.Create(new GatewayAsaasOptions
    {
        UrlBase = UrlBase,
        ChaveApi = Environment.GetEnvironmentVariable("ASAAS_API_KEY")!,
        TokenWebhook = "nao-usado-neste-teste",
        ChaveCifragemSubcontas = Convert.ToBase64String(new byte[32]),
        Subcontas = { Habilitar = true },
        Escrow = { Habilitar = true, DiasParaExpirar = 30 },
    });

    /// <summary>Chamada só do sandbox, fora dos adaptadores (aprovar a subconta, simular o pagamento).</summary>
    private static async Task PostSandboxAsync(string rota, string chave)
    {
        using var http = new HttpClient { BaseAddress = new Uri(UrlBase + "/") };
        http.DefaultRequestHeaders.Add("access_token", chave);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Viteme-Testes/1.0");
        using var resposta = await http.PostAsJsonAsync(rota, new { });
        resposta.EnsureSuccessStatusCode();
    }

    private static string NovoCnpj()
    {
        var d = Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(10)).Concat([0, 0, 0, 1]).ToList();
        foreach (var pesos in new[] { new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } })
        {
            var resto = pesos.Select((p, i) => p * d[i]).Sum() % 11;
            d.Add(resto < 2 ? 0 : 11 - resto);
        }

        return string.Concat(d);
    }

    private static string Letras() => new(Enumerable.Range(0, 8).Select(_ => (char)('a' + Random.Shared.Next(26))).ToArray());

    [FactAsaasSandbox]
    public async Task Fase3_cobranca_na_subconta_fica_retida_e_e_liberada()
    {
        using var contas = new ContasRecebimentoAsaas(Opcoes(), NullLogger<ContasRecebimentoAsaas>.Instance);
        using var gateway = new GatewayPagamentoAsaas(Opcoes(), NullLogger<GatewayPagamentoAsaas>.Instance);

        // Subconta (nome só com letras: número no nome impede a aprovação do sandbox), aprovada e com escrow.
        var subconta = await contas.CriarSubcontaAsync(new DadosSubconta(
            Guid.NewGuid(), NovoCnpj(), $"Organizador Teste {Letras()}", $"teste-{Letras()}@exemplo.com", "ASSOCIATION",
            "+5544999990000", 5000m, "87020900", "Av. Colombo", "5790", null, "Zona Sete"));
        Assert.Equal(SituacaoContaRecebimento.Pendente, (await contas.ConsultarSituacaoAsync(subconta.ChaveApi)).Situacao);
        await PostSandboxAsync("sandbox/myAccount/approve", subconta.ChaveApi);
        Assert.Equal(SituacaoContaRecebimento.Aprovada, (await contas.ConsultarSituacaoAsync(subconta.ChaveApi)).Situacao);
        await contas.HabilitarGarantiaAsync(subconta.ContaId);

        // Cobrança criada na subconta (Fase 3), paga no simulador.
        var referencia = Guid.NewGuid();
        var pedido = new PedidoCobranca(
            referencia, 10m, "Inscrição em evento de teste", DateTime.UtcNow.AddHours(2),
            new Pagador(Guid.NewGuid(), "Pagador Teste", "pagador@exemplo.com", "12345678909"), ChaveApiRecebedor: subconta.ChaveApi);
        var cobranca = await gateway.CriarCobrancaAsync(pedido);
        Assert.Equal(cobranca.IdTransacao, (await gateway.CriarCobrancaAsync(pedido)).IdTransacao); // idempotente na subconta
        Assert.Equal(StatusPagamento.Pendente, await gateway.ConsultarCobrancaAsync(cobranca.IdTransacao, subconta.ChaveApi));

        await PostSandboxAsync($"sandbox/payment/{cobranca.IdTransacao}/confirm", subconta.ChaveApi);
        Assert.Equal(StatusPagamento.Aprovado, await gateway.ConsultarCobrancaAsync(cobranca.IdTransacao, subconta.ChaveApi));

        // Garantia retida, depois liberada (encerrar de novo não falha: o adaptador consulta antes).
        var garantiaId = await contas.ObterGarantiaAsync(cobranca.IdTransacao, subconta.ChaveApi);
        Assert.NotNull(garantiaId);
        await contas.EncerrarGarantiaAsync(cobranca.IdTransacao, garantiaId, subconta.ChaveApi);
        await contas.EncerrarGarantiaAsync(cobranca.IdTransacao, garantiaId, subconta.ChaveApi);
    }
}
