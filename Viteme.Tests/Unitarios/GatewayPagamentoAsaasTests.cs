using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;
using Viteme.Infrastructure.Externos;

namespace Viteme.Tests.Unitarios;

/// <summary>Adaptador do Asaas contra um manipulador HTTP falso (sem rede). O roteiro real no sandbox está em docs/asaas.</summary>
public class GatewayPagamentoAsaasTests
{
    private const string Token = "token-do-webhook-de-teste";
    private static readonly Guid Referencia = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private sealed class Roteiro : HttpMessageHandler
    {
        private readonly List<(Func<HttpRequestMessage, bool> Casa, HttpStatusCode Status, string Corpo)> _respostas = [];

        public List<(HttpMethod Metodo, string Rota, string? Corpo, HttpRequestMessage Requisicao)> Chamadas { get; } = [];

        public Roteiro Responde(HttpMethod metodo, string rotaComeca, string corpo, HttpStatusCode status = HttpStatusCode.OK)
        {
            _respostas.Add((r => r.Method == metodo && r.RequestUri!.PathAndQuery.StartsWith("/v3/" + rotaComeca, StringComparison.Ordinal), status, corpo));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken cancellationToken)
        {
            var corpo = requisicao.Content is null ? null : await requisicao.Content.ReadAsStringAsync(cancellationToken);
            Chamadas.Add((requisicao.Method, requisicao.RequestUri!.PathAndQuery, corpo, requisicao));

            var resposta = _respostas.FirstOrDefault(r => r.Casa(requisicao));
            if (resposta.Casa is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }

            return new HttpResponseMessage(resposta.Status) { Content = new StringContent(resposta.Corpo, Encoding.UTF8, "application/json") };
        }
    }

    private static GatewayPagamentoAsaas Criar(Roteiro roteiro, string urlBase = "https://api-sandbox.asaas.com/v3", string cpfPadrao = "24971563792") =>
        new(roteiro, Options.Create(new GatewayAsaasOptions
        {
            UrlBase = urlBase,
            ChaveApi = "chave-secreta-de-teste",
            TokenWebhook = Token,
            CpfCnpjPagadorPadraoSandbox = cpfPadrao,
        }), NullLogger<GatewayPagamentoAsaas>.Instance);

    private static PedidoCobranca Pedido(bool comPagador = true) => new(
        Referencia, 50m, "Inscrição em Evento", DateTime.UtcNow.AddHours(1),
        comPagador ? new Pagador(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "Maria Souza", "maria@exemplo.com") : null);

    [Fact]
    public async Task Criar_cobranca_cria_cliente_e_cobranca_pix_com_a_referencia_externa()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Get, "customers?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Post, "customers", """{"id":"cus_1","deleted":false}""")
            .Responde(HttpMethod.Post, "payments", """{"id":"pay_1","status":"PENDING","invoiceUrl":"https://sandbox.asaas.com/i/pay_1"}""");
        using var gateway = Criar(roteiro);

        var cobranca = await gateway.CriarCobrancaAsync(Pedido());

        Assert.Equal("pay_1", cobranca.IdTransacao);
        Assert.Equal("https://sandbox.asaas.com/i/pay_1", cobranca.UrlPagamento);

        var cliente = roteiro.Chamadas.Single(c => c.Metodo == HttpMethod.Post && c.Rota.EndsWith("/customers"));
        Assert.Contains("\"cpfCnpj\":\"24971563792\"", cliente.Corpo);
        Assert.Contains("\"externalReference\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"", cliente.Corpo);

        var pagamento = roteiro.Chamadas.Single(c => c.Metodo == HttpMethod.Post && c.Rota.EndsWith("/payments"));
        Assert.Contains("\"customer\":\"cus_1\"", pagamento.Corpo);
        Assert.Contains("\"billingType\":\"PIX\"", pagamento.Corpo);
        Assert.Contains("\"value\":50", pagamento.Corpo);
        Assert.Contains($"\"externalReference\":\"{Referencia}\"", pagamento.Corpo);
        Assert.Matches("\"dueDate\":\"\\d{4}-\\d{2}-\\d{2}\"", pagamento.Corpo);
    }

    [Fact]
    public async Task Requisicoes_levam_a_chave_no_cabecalho_access_token_e_o_user_agent()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[{"id":"pay_1","deleted":false,"invoiceUrl":"u"}]}""");
        using var gateway = Criar(roteiro);

        await gateway.CriarCobrancaAsync(Pedido());

        var requisicao = roteiro.Chamadas[0].Requisicao;
        Assert.Equal("chave-secreta-de-teste", requisicao.Headers.GetValues("access_token").Single());
        Assert.Contains("Viteme", requisicao.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Criar_cobranca_e_idempotente_pela_referencia_externa()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[{"id":"pay_9","deleted":false,"invoiceUrl":"https://sandbox.asaas.com/i/pay_9"}]}""");
        using var gateway = Criar(roteiro);

        var cobranca = await gateway.CriarCobrancaAsync(Pedido());

        Assert.Equal("pay_9", cobranca.IdTransacao);
        Assert.DoesNotContain(roteiro.Chamadas, c => c.Metodo == HttpMethod.Post);
    }

    [Fact]
    public async Task Cliente_ja_cadastrado_e_reaproveitado()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Get, "customers?externalReference=", """{"data":[{"id":"cus_7","deleted":false}]}""")
            .Responde(HttpMethod.Post, "payments", """{"id":"pay_2","invoiceUrl":"u"}""");
        using var gateway = Criar(roteiro);

        await gateway.CriarCobrancaAsync(Pedido());

        Assert.DoesNotContain(roteiro.Chamadas, c => c.Metodo == HttpMethod.Post && c.Rota.EndsWith("/customers"));
        Assert.Contains("\"customer\":\"cus_7\"", roteiro.Chamadas.Single(c => c.Rota.EndsWith("/payments") && c.Metodo == HttpMethod.Post).Corpo);
    }

    [Fact]
    public async Task Sem_cpf_padrao_ou_em_producao_nao_cria_cliente()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Get, "customers?externalReference=", """{"data":[]}""");

        using var semCpf = Criar(roteiro, cpfPadrao: "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => semCpf.CriarCobrancaAsync(Pedido()));

        using var producao = Criar(roteiro, urlBase: "https://api.asaas.com/v3");
        await Assert.ThrowsAsync<InvalidOperationException>(() => producao.CriarCobrancaAsync(Pedido()));
    }

    [Fact]
    public async Task Sem_pagador_a_cobranca_falha()
    {
        var roteiro = new Roteiro().Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""");
        using var gateway = Criar(roteiro);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CriarCobrancaAsync(Pedido(comPagador: false)));
    }

    [Fact]
    public async Task Erro_do_asaas_sobe_como_excecao_sem_vazar_a_chave()
    {
        var roteiro = new Roteiro().Responde(
            HttpMethod.Get, "payments?externalReference=", """{"errors":[{"code":"invalid_access_token","description":"A chave de API informada é inválida."}]}""",
            HttpStatusCode.Unauthorized);
        using var gateway = Criar(roteiro);

        var erro = await Assert.ThrowsAsync<HttpRequestException>(() => gateway.CriarCobrancaAsync(Pedido()));

        Assert.Contains("401", erro.Message);
        Assert.Contains("A chave de API informada é inválida.", erro.Message);
        Assert.DoesNotContain("chave-secreta-de-teste", erro.Message);
    }

    [Theory]
    [InlineData("PENDING", StatusPagamento.Pendente)]
    [InlineData("RECEIVED", StatusPagamento.Aprovado)]
    [InlineData("CONFIRMED", StatusPagamento.Aprovado)]
    [InlineData("OVERDUE", StatusPagamento.Expirado)]
    [InlineData("REFUNDED", StatusPagamento.Estornado)]
    [InlineData("REFUND_IN_PROGRESS", StatusPagamento.Pendente)]
    public async Task Consultar_traduz_o_status_do_asaas(string statusAsaas, StatusPagamento esperado)
    {
        var roteiro = new Roteiro().Responde(HttpMethod.Get, "payments/pay_1", $$"""{"id":"pay_1","status":"{{statusAsaas}}","deleted":false}""");
        using var gateway = Criar(roteiro);

        Assert.Equal(esperado, await gateway.ConsultarCobrancaAsync("pay_1"));
    }

    [Fact]
    public async Task Cobranca_apagada_conta_como_expirada()
    {
        var roteiro = new Roteiro().Responde(HttpMethod.Get, "payments/pay_1", """{"id":"pay_1","status":"PENDING","deleted":true}""");
        using var gateway = Criar(roteiro);

        Assert.Equal(StatusPagamento.Expirado, await gateway.ConsultarCobrancaAsync("pay_1"));
    }

    [Fact]
    public async Task Estorno_pede_o_valor_ao_asaas()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments/pay_1", """{"id":"pay_1","status":"RECEIVED"}""")
            .Responde(HttpMethod.Post, "payments/pay_1/refund", """{"id":"pay_1","status":"REFUND_REQUESTED"}""");
        using var gateway = Criar(roteiro);

        await gateway.SolicitarEstornoAsync("pay_1", 50m);

        var estorno = roteiro.Chamadas.Single(c => c.Metodo == HttpMethod.Post);
        Assert.EndsWith("/payments/pay_1/refund", estorno.Rota);
        Assert.Contains("\"value\":50", estorno.Corpo);
    }

    [Theory]
    [InlineData("REFUNDED")]
    [InlineData("REFUND_REQUESTED")]
    [InlineData("REFUND_IN_PROGRESS")]
    public async Task Estorno_ja_feito_ou_em_andamento_nao_e_pedido_de_novo(string statusAsaas)
    {
        var roteiro = new Roteiro().Responde(HttpMethod.Get, "payments/pay_1", $$"""{"id":"pay_1","status":"{{statusAsaas}}"}""");
        using var gateway = Criar(roteiro);

        await gateway.SolicitarEstornoAsync("pay_1", 50m);

        Assert.DoesNotContain(roteiro.Chamadas, c => c.Metodo == HttpMethod.Post);
    }

    [Fact]
    public void Webhook_exige_o_token_no_cabecalho_asaas_access_token()
    {
        using var gateway = Criar(new Roteiro());
        var cabecalhos = (string? valor) => valor is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["asaas-access-token"] = valor };

        Assert.True(gateway.ValidarAssinaturaWebhook("{}", cabecalhos(Token)));
        Assert.False(gateway.ValidarAssinaturaWebhook("{}", cabecalhos("outro-token")));
        Assert.False(gateway.ValidarAssinaturaWebhook("{}", cabecalhos("")));
        Assert.False(gateway.ValidarAssinaturaWebhook("{}", cabecalhos(null)));
    }

    [Fact]
    public void Webhook_nunca_e_aceito_sem_token_configurado()
    {
        using var gateway = new GatewayPagamentoAsaas(
            new Roteiro(), Options.Create(new GatewayAsaasOptions { ChaveApi = "x", TokenWebhook = "" }), NullLogger<GatewayPagamentoAsaas>.Instance);

        Assert.False(gateway.ValidarAssinaturaWebhook("{}", new Dictionary<string, string> { ["asaas-access-token"] = "" }));
    }

    private static string Evento(string evento, string? referencia = null, string valor = "50.0") => $$$"""
        {"id":"evt_1","event":"{{{evento}}}","dateCreated":"2026-11-24 10:00:00",
         "payment":{"id":"pay_1","value":{{{valor}}},"billingType":"PIX","status":"RECEIVED","externalReference":"{{{referencia ?? Referencia.ToString()}}}"}}
        """;

    [Theory]
    [InlineData("PAYMENT_RECEIVED", StatusPagamento.Aprovado)]
    [InlineData("PAYMENT_CONFIRMED", StatusPagamento.Aprovado)]
    [InlineData("PAYMENT_OVERDUE", StatusPagamento.Expirado)]
    [InlineData("PAYMENT_DELETED", StatusPagamento.Expirado)]
    [InlineData("PAYMENT_REFUNDED", StatusPagamento.Estornado)]
    public void Webhook_traduz_o_evento_para_o_status_do_viteme(string evento, StatusPagamento esperado)
    {
        using var gateway = Criar(new Roteiro());

        var n = gateway.InterpretarWebhook(Evento(evento));

        Assert.NotNull(n);
        Assert.Equal(esperado, n.Status);
        Assert.Equal("pay_1", n.IdTransacao);
        Assert.Equal(Referencia, n.ReferenciaExterna);
        Assert.Equal(50m, n.Valor);
        Assert.Equal("PIX", n.Metodo);
    }

    [Theory]
    [InlineData("PAYMENT_CREATED")]
    [InlineData("PAYMENT_UPDATED")]
    [InlineData("PAYMENT_SPLIT_DONE")]
    [InlineData("PAYMENT_REFUND_IN_PROGRESS")]
    [InlineData("PAYMENT_PARTIALLY_REFUNDED")]
    public void Webhook_de_evento_sem_efeito_e_ignorado(string evento)
    {
        using var gateway = Criar(new Roteiro());

        Assert.Null(gateway.InterpretarWebhook(Evento(evento)));
    }

    [Fact]
    public void Webhook_de_outra_entidade_sem_cobranca_e_ignorado()
    {
        using var gateway = Criar(new Roteiro());

        Assert.Null(gateway.InterpretarWebhook("""{"id":"evt_2","event":"ACCOUNT_STATUS_UPDATED"}"""));
    }

    [Theory]
    [InlineData("isso não é json")]
    [InlineData("{}")]
    [InlineData("""{"event":"PAYMENT_RECEIVED","payment":{"id":"pay_1","value":50}}""")]
    [InlineData("""{"event":"PAYMENT_RECEIVED","payment":{"id":"pay_1","externalReference":"não-é-guid","value":50}}""")]
    public void Webhook_malformado_lanca_format_exception(string corpo)
    {
        using var gateway = Criar(new Roteiro());

        Assert.Throws<FormatException>(() => gateway.InterpretarWebhook(corpo));
    }
}
