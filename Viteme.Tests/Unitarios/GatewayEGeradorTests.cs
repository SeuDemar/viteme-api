using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;
using Viteme.Infrastructure.Seguranca;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios;

public class GatewayEGeradorTests
{
    private readonly GatewayEmMemoria _gateway = new();

    [Fact]
    public async Task Stub_cria_cobranca_idempotente_para_a_mesma_referencia()
    {
        var referencia = Guid.NewGuid();
        var pedido = new PedidoCobranca(referencia, 50m, "Inscrição", DateTime.UtcNow.AddMinutes(30));

        var primeira = await _gateway.CriarCobrancaAsync(pedido);
        var segunda = await _gateway.CriarCobrancaAsync(pedido);

        Assert.Equal(primeira, segunda);
        Assert.Equal($"stub_{referencia:N}", primeira.IdTransacao);
        Assert.EndsWith(primeira.IdTransacao, primeira.UrlPagamento);
    }

    [Fact]
    public void Webhook_assinado_e_validado_e_interpretado()
    {
        var referencia = Guid.NewGuid();
        var corpo = _gateway.MontarWebhook("stub_1", referencia, StatusPagamento.Aprovado, 49.9m, "PIX");
        var cabecalhos = new Dictionary<string, string> { [GatewayPagamentoStubNome()] = _gateway.Assinar(corpo) };

        var valido = _gateway.ValidarAssinaturaWebhook(corpo, cabecalhos);
        var notificacao = _gateway.InterpretarWebhook(corpo);

        Assert.True(valido);
        Assert.Equal(new NotificacaoPagamento("stub_1", referencia, StatusPagamento.Aprovado, 49.9m, "PIX"), notificacao);
    }

    [Fact]
    public void Webhook_adulterado_ou_sem_assinatura_e_rejeitado()
    {
        var corpo = _gateway.MontarWebhook("stub_1", Guid.NewGuid(), StatusPagamento.Aprovado, 50m, null);
        var adulterado = corpo.Replace("50", "1");

        Assert.False(_gateway.ValidarAssinaturaWebhook(adulterado,
            new Dictionary<string, string> { [GatewayPagamentoStubNome()] = _gateway.Assinar(corpo) }));
        Assert.False(_gateway.ValidarAssinaturaWebhook(corpo, new Dictionary<string, string>()));
        Assert.False(_gateway.ValidarAssinaturaWebhook(corpo, new Dictionary<string, string> { [GatewayPagamentoStubNome()] = " " }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao é json")]
    [InlineData("{}")]
    [InlineData("{\"idTransacao\":\"x\",\"referenciaExterna\":\"00000000-0000-0000-0000-000000000000\",\"status\":\"APROVADO\",\"valor\":1}")]
    [InlineData("{\"idTransacao\":\"x\",\"referenciaExterna\":\"7b1d0e9a-0000-4000-8000-000000000001\",\"status\":\"QUALQUER\",\"valor\":1}")]
    public void Webhook_invalido_lanca_FormatException(string corpo)
    {
        Assert.Throws<FormatException>(() => _gateway.InterpretarWebhook(corpo));
    }

    [Theory]
    [InlineData("APROVADO", StatusPagamento.Aprovado)]
    [InlineData("recusado", StatusPagamento.Recusado)]
    [InlineData("ESTORNADO", StatusPagamento.Estornado)]
    [InlineData("EXPIRADO", StatusPagamento.Expirado)]
    public void Status_do_webhook_aceita_maiusculas_e_minusculas(string texto, StatusPagamento esperado)
    {
        var corpo = $"{{\"idTransacao\":\"x\",\"referenciaExterna\":\"{Guid.NewGuid()}\",\"status\":\"{texto}\",\"valor\":10}}";

        Assert.Equal(esperado, _gateway.InterpretarWebhook(corpo).Status);
    }

    [Fact]
    public void Codigo_alfanumerico_tem_o_tamanho_pedido_e_evita_caracteres_ambiguos()
    {
        var gerador = new GeradorAleatorio();

        var codigos = Enumerable.Range(0, 2000).Select(_ => gerador.GerarCodigoAlfanumerico(12)).ToList();

        Assert.All(codigos, c =>
        {
            Assert.Equal(12, c.Length);
            Assert.Matches("^[A-HJ-NP-Z2-9]{12}$", c);
        });
        Assert.Equal(codigos.Count, codigos.Distinct().Count());
    }

    private static string GatewayPagamentoStubNome() => "X-Viteme-Assinatura";
}
