using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;
using Viteme.Infrastructure.Externos;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios;

/// <summary>
/// Subconta e garantia do Asaas (Fases 2 e 3) contra um manipulador HTTP falso. Os formatos seguem a documentação pública;
/// os pontos "confirmar Qn" do adaptador ainda precisam da conta sandbox PJ.
/// </summary>
public class ContasRecebimentoAsaasTests
{
    private static ContasRecebimentoAsaas Criar(RoteiroHttp roteiro, string urlWebhook = "", bool escrow = false) =>
        new(roteiro, Options.Create(new GatewayAsaasOptions
        {
            ChaveApi = "chave-raiz",
            TokenWebhook = "token-webhook",
            ChaveCifragemSubcontas = Convert.ToBase64String(new byte[32]),
            Subcontas = { Habilitar = true, UrlWebhook = urlWebhook },
            Escrow = { Habilitar = escrow, DiasParaExpirar = 30, SubcontaPagaMensalidade = true },
        }), NullLogger<ContasRecebimentoAsaas>.Instance);

    private static DadosSubconta Dados() => new(
        Guid.NewGuid(), "11222333000181", "Empresa Teste LTDA", "org@exemplo.com", "LIMITED", "+5544999990000",
        5000m, "87020000", "Rua A", "100", null, "Centro");

    private static string Chave(HttpRequestMessage r) => r.Headers.GetValues("access_token").Single();

    [Fact]
    public async Task Cria_a_subconta_com_a_chave_da_raiz_e_devolve_a_chave_dela_e_o_link_dos_documentos()
    {
        var roteiro = new RoteiroHttp()
            .Responde(HttpMethod.Post, "accounts", """{"id":"acc_1","walletId":"wal_1","apiKey":"chave-sub"}""")
            .Responde(HttpMethod.Get, "myAccount/documents", """{"data":[{"id":"d1","onboardingUrl":null},{"id":"d2","onboardingUrl":"https://onb/1"}]}""");
        using var contas = Criar(roteiro, urlWebhook: "https://api/api/v1/pagamentos/webhook");

        var criada = await contas.CriarSubcontaAsync(Dados());

        Assert.Equal(new SubcontaCriada("acc_1", "wal_1", "chave-sub", "https://onb/1"), criada);
        var post = roteiro.Chamadas[0];
        Assert.Equal("chave-raiz", Chave(post.Requisicao));
        using var corpo = JsonDocument.Parse(post.Corpo!);
        Assert.Equal("11222333000181", corpo.RootElement.GetProperty("cpfCnpj").GetString());
        Assert.Equal("44999990000", corpo.RootElement.GetProperty("mobilePhone").GetString());
        Assert.Equal(5000m, corpo.RootElement.GetProperty("incomeValue").GetDecimal());
        var webhook = corpo.RootElement.GetProperty("webhooks")[0];
        Assert.Equal("https://api/api/v1/pagamentos/webhook", webhook.GetProperty("url").GetString());
        Assert.Equal("token-webhook", webhook.GetProperty("authToken").GetString());
        Assert.Equal("chave-sub", Chave(roteiro.Chamadas[1].Requisicao));
    }

    [Fact]
    public async Task Sem_url_de_webhook_a_subconta_e_criada_sem_webhook_e_link_indisponivel_nao_falha()
    {
        var roteiro = new RoteiroHttp()
            .Responde(HttpMethod.Post, "accounts", """{"id":"acc_1","walletId":"wal_1","apiKey":"k"}""")
            .Responde(HttpMethod.Get, "myAccount/documents", "{}", HttpStatusCode.InternalServerError);
        using var contas = Criar(roteiro);

        var criada = await contas.CriarSubcontaAsync(Dados());

        Assert.Null(criada.UrlOnboarding);
        Assert.False(JsonDocument.Parse(roteiro.Chamadas[0].Corpo!).RootElement.TryGetProperty("webhooks", out _));
    }

    [Fact]
    public async Task Resposta_sem_a_chave_da_subconta_e_erro()
    {
        var roteiro = new RoteiroHttp().Responde(HttpMethod.Post, "accounts", """{"id":"acc_1","walletId":"wal_1"}""");
        using var contas = Criar(roteiro);

        await Assert.ThrowsAsync<HttpRequestException>(() => contas.CriarSubcontaAsync(Dados()));
    }

    [Theory]
    [InlineData("APPROVED", SituacaoContaRecebimento.Aprovada)]
    [InlineData("REJECTED", SituacaoContaRecebimento.Rejeitada)]
    [InlineData("AWAITING_APPROVAL", SituacaoContaRecebimento.Pendente)]
    [InlineData("PENDING", SituacaoContaRecebimento.Pendente)]
    public async Task Situacao_e_consultada_com_a_chave_da_subconta(string geral, SituacaoContaRecebimento esperada)
    {
        var roteiro = new RoteiroHttp()
            .Responde(HttpMethod.Get, "myAccount/status", $$"""{"id":"acc_1","general":"{{geral}}"}""")
            .Responde(HttpMethod.Get, "myAccount/documents", """{"data":[]}""");
        using var contas = Criar(roteiro);

        var situacao = await contas.ConsultarSituacaoAsync("chave-sub");

        Assert.Equal(esperada, situacao.Situacao);
        Assert.All(roteiro.Chamadas, c => Assert.Equal("chave-sub", Chave(c.Requisicao)));
    }

    [Fact]
    public void Webhook_de_conta_e_traduzido_e_o_de_cobranca_e_ignorado()
    {
        using var contas = Criar(new RoteiroHttp());

        Assert.Equal(
            new NotificacaoConta("acc_1", SituacaoContaRecebimento.Aprovada),
            contas.InterpretarWebhookConta("""{"event":"ACCOUNT_STATUS_GENERAL_APPROVAL_APPROVED","accountStatus":{"id":"acc_1"}}"""));
        Assert.Equal(
            new NotificacaoConta("acc_1", SituacaoContaRecebimento.Rejeitada),
            contas.InterpretarWebhookConta("""{"event":"ACCOUNT_STATUS_DOCUMENT_REJECTED","accountStatus":{"id":"acc_1","general":"REJECTED"}}"""));
        Assert.Null(contas.InterpretarWebhookConta("""{"event":"PAYMENT_RECEIVED","payment":{"id":"pay_1"}}"""));
        Assert.Null(contas.InterpretarWebhookConta("nao e json"));
        Assert.Throws<FormatException>(() => contas.InterpretarWebhookConta("""{"event":"ACCOUNT_STATUS_GENERAL_APPROVAL_APPROVED"}"""));
    }

    [Fact]
    public async Task Garantia_e_ligada_com_prazo_e_quem_paga_a_mensalidade()
    {
        var roteiro = new RoteiroHttp().Responde(HttpMethod.Post, "accounts/acc_1/escrow", "{}");
        using var contas = Criar(roteiro, escrow: true);

        await contas.HabilitarGarantiaAsync("acc_1");

        using var corpo = JsonDocument.Parse(roteiro.Chamadas[0].Corpo!);
        Assert.True(corpo.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(30, corpo.RootElement.GetProperty("daysToExpire").GetInt32());
        Assert.True(corpo.RootElement.GetProperty("isFeePayer").GetBoolean());
        Assert.Equal("chave-raiz", Chave(roteiro.Chamadas[0].Requisicao));
    }

    [Fact]
    public async Task Garantia_de_cobranca_nao_retida_volta_nula()
    {
        using var contas = Criar(new RoteiroHttp());

        Assert.Null(await contas.ObterGarantiaAsync("pay_1", "chave-sub"));
    }

    [Fact]
    public async Task Encerrar_garantia_ativa_chama_finish_e_ja_encerrada_nao_chama_de_novo()
    {
        var ativa = new RoteiroHttp()
            .Responde(HttpMethod.Get, "payments/pay_1/escrow", """{"id":"esc_1","status":"ACTIVE"}""")
            .Responde(HttpMethod.Post, "escrow/esc_1/finish", "{}");
        using (var contas = Criar(ativa))
        {
            await contas.EncerrarGarantiaAsync("pay_1", "esc_1", "chave-sub");
        }

        Assert.Contains(ativa.Chamadas, c => c.Metodo == HttpMethod.Post && c.Rota == "/v3/escrow/esc_1/finish" && Chave(c.Requisicao) == "chave-sub");

        var encerrada = new RoteiroHttp()
            .Responde(HttpMethod.Get, "payments/pay_1/escrow", """{"id":"esc_1","status":"DONE","finishDate":"2026-11-30"}""");
        using (var contas = Criar(encerrada))
        {
            await contas.EncerrarGarantiaAsync("pay_1", "esc_1", "chave-sub");
        }

        Assert.DoesNotContain(encerrada.Chamadas, c => c.Metodo == HttpMethod.Post);
    }

    [Theory]
    [InlineData("+5544999990000", "44999990000")]
    [InlineData("+554433334444", "4433334444")]
    [InlineData("44999990000", "44999990000")]
    public void Telefone_vai_sem_o_codigo_do_pais(string e164, string nacional) =>
        Assert.Equal(nacional, ContasRecebimentoAsaas.TelefoneNacional(e164));
}
