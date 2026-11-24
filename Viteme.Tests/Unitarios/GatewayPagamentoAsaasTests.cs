using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;
using Viteme.Infrastructure.Externos;
using Viteme.Tests.Suporte;
using Roteiro = Viteme.Tests.Suporte.RoteiroHttp;

namespace Viteme.Tests.Unitarios;

/// <summary>Adaptador do Asaas contra um manipulador HTTP falso (sem rede). O roteiro real no sandbox está em docs/asaas.</summary>
public class GatewayPagamentoAsaasTests
{
    private const string Token = "token-do-webhook-de-teste";
    private static readonly Guid Referencia = Guid.Parse("11111111-2222-3333-4444-555555555555");

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
        Assert.DoesNotContain("split", pagamento.Corpo);
    }

    [Fact]
    public async Task Com_a_chave_da_subconta_cliente_e_cobranca_nascem_nela_e_sem_split()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Get, "customers?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Post, "customers", """{"id":"cus_sub","deleted":false}""")
            .Responde(HttpMethod.Post, "payments", """{"id":"pay_sub","status":"PENDING","invoiceUrl":"u"}""");
        using var gateway = Criar(roteiro);

        var cobranca = await gateway.CriarCobrancaAsync(Pedido() with { WalletRecebedor = "wal_org", ChaveApiRecebedor = "chave-da-subconta" });

        Assert.Equal("pay_sub", cobranca.IdTransacao);
        Assert.All(roteiro.Chamadas, c => Assert.Equal("chave-da-subconta", c.Requisicao.Headers.GetValues("access_token").Single()));
        Assert.DoesNotContain("split", roteiro.Chamadas.Single(c => c.Metodo == HttpMethod.Post && c.Rota.EndsWith("/payments")).Corpo);
    }

    [Fact]
    public async Task Pix_consulta_e_estorno_da_cobranca_da_subconta_usam_a_chave_dela()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments/pay_sub/pixQrCode", """{"encodedImage":"aW1n","payload":"000201","expirationDate":"2026-11-25 23:59:59"}""")
            .Responde(HttpMethod.Get, "payments/pay_sub", """{"id":"pay_sub","status":"RECEIVED","deleted":false}""")
            .Responde(HttpMethod.Post, "payments/pay_sub/refund", """{"id":"pay_sub","status":"REFUNDED","deleted":false}""");
        using var gateway = Criar(roteiro);

        await gateway.ObterPixAsync("pay_sub", "chave-da-subconta");
        await gateway.ConsultarCobrancaAsync("pay_sub", "chave-da-subconta");
        await gateway.SolicitarEstornoAsync("pay_sub", 50m, "chave-da-subconta");

        Assert.Equal(4, roteiro.Chamadas.Count);
        Assert.All(roteiro.Chamadas, c => Assert.Equal("chave-da-subconta", c.Requisicao.Headers.GetValues("access_token").Single()));
    }

    [Fact]
    public async Task Com_carteira_do_organizador_a_cobranca_leva_split_de_100_por_cento()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Get, "customers?externalReference=", """{"data":[{"id":"cus_1","deleted":false}]}""")
            .Responde(HttpMethod.Post, "payments", """{"id":"pay_1","status":"PENDING","invoiceUrl":"u"}""");
        using var gateway = Criar(roteiro);

        await gateway.CriarCobrancaAsync(Pedido() with { WalletRecebedor = "wal_org" });

        var pagamento = roteiro.Chamadas.Single(c => c.Metodo == HttpMethod.Post && c.Rota.EndsWith("/payments"));
        Assert.Contains("\"split\":[{\"walletId\":\"wal_org\",\"percentualValue\":100}]", pagamento.Corpo);
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
    public async Task Cpf_do_pagador_tem_prioridade_sobre_o_cpf_padrao_do_sandbox()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Get, "customers?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Post, "customers", """{"id":"cus_1","deleted":false}""")
            .Responde(HttpMethod.Post, "payments", """{"id":"pay_1","invoiceUrl":"u"}""");
        using var gateway = Criar(roteiro);
        var pedido = Pedido() with { Pagador = Pedido().Pagador! with { CpfCnpj = "12345678909" } };

        await gateway.CriarCobrancaAsync(pedido);

        var cliente = roteiro.Chamadas.Single(c => c.Metodo == HttpMethod.Post && c.Rota.EndsWith("/customers"));
        Assert.Contains("\"cpfCnpj\":\"12345678909\"", cliente.Corpo);
        Assert.DoesNotContain("24971563792", cliente.Corpo);
    }

    [Fact]
    public async Task Cliente_existente_sem_cpf_e_atualizado_com_o_cpf_do_pagador()
    {
        var roteiro = new Roteiro()
            .Responde(HttpMethod.Get, "payments?externalReference=", """{"data":[]}""")
            .Responde(HttpMethod.Get, "customers?externalReference=", """{"data":[{"id":"cus_7","deleted":false,"cpfCnpj":null}]}""")
            .Responde(HttpMethod.Put, "customers/cus_7", """{"id":"cus_7","deleted":false,"cpfCnpj":"12345678909"}""")
            .Responde(HttpMethod.Post, "payments", """{"id":"pay_2","invoiceUrl":"u"}""");
        using var gateway = Criar(roteiro);
        var pedido = Pedido() with { Pagador = Pedido().Pagador! with { CpfCnpj = "12345678909" } };

        await gateway.CriarCobrancaAsync(pedido);

        var atualizacao = roteiro.Chamadas.Single(c => c.Metodo == HttpMethod.Put);
        Assert.EndsWith("/customers/cus_7", atualizacao.Rota);
        Assert.Contains("\"cpfCnpj\":\"12345678909\"", atualizacao.Corpo);
    }

    [Fact]
    public void Asaas_exige_o_documento_do_pagador()
    {
        using var gateway = Criar(new Roteiro());

        Assert.True(gateway.ExigeDocumentoDoPagador);
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

        var erro = await Assert.ThrowsAnyAsync<HttpRequestException>(() => gateway.CriarCobrancaAsync(Pedido()));

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
    public async Task Pix_devolve_copia_e_cola_e_imagem_do_qr_code()
    {
        var roteiro = new Roteiro().Responde(HttpMethod.Get, "payments/pay_1/pixQrCode",
            """{"encodedImage":"iVBORw0KGgo=","payload":"00020126580014br.gov.bcb.pix","expirationDate":"2026-12-24 23:59:59"}""");
        using var gateway = Criar(roteiro);

        var pix = await gateway.ObterPixAsync("pay_1");

        Assert.NotNull(pix);
        Assert.Equal("00020126580014br.gov.bcb.pix", pix.CopiaECola);
        Assert.Equal("iVBORw0KGgo=", pix.ImagemBase64);
        Assert.Equal(new DateTime(2026, 12, 24, 23, 59, 59), pix.ExpiraEm);
    }

    [Fact]
    public async Task Pix_sem_chave_cadastrada_no_asaas_devolve_nulo_em_vez_de_falhar()
    {
        var roteiro = new Roteiro().Responde(HttpMethod.Get, "payments/pay_1/pixQrCode",
            """{"errors":[{"code":"invalid_action","description":"Você não possui uma chave Pix cadastrada para recebimentos de cobranças via Pix."}]}""",
            HttpStatusCode.BadRequest);
        using var gateway = Criar(roteiro);

        Assert.Null(await gateway.ObterPixAsync("pay_1"));
    }

    [Fact]
    public async Task Pix_com_erro_de_servidor_sobe_excecao()
    {
        var roteiro = new Roteiro().Responde(HttpMethod.Get, "payments/pay_1/pixQrCode", "{}", HttpStatusCode.InternalServerError);
        using var gateway = Criar(roteiro);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => gateway.ObterPixAsync("pay_1"));
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
