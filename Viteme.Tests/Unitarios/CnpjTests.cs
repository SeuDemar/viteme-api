using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viteme.Application.Validacoes;
using Viteme.Domain.Excecoes;
using Viteme.Infrastructure.Externos;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios;

/// <summary>RN24: validação do CNPJ (numérico e alfanumérico) e leitura da resposta da BrasilAPI.</summary>
public class CnpjTests
{
    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11.222.333/0001-81")]
    [InlineData("33.000.167/0001-01")]
    [InlineData("12.ABC.345/01DE-35")] // exemplo da Receita para o CNPJ alfanumérico
    [InlineData("12abc34501de35")]
    public void Cnpj_valido_com_ou_sem_mascara(string cnpj) => Assert.True(CnpjAttribute.EhValido(cnpj));

    [Theory]
    [InlineData("11222333000182")] // dígito verificador errado
    [InlineData("1122233300018")] // 13 caracteres
    [InlineData("00000000000000")] // todos iguais
    [InlineData("12ABC34501DE3A")] // verificador precisa ser número
    [InlineData("")]
    public void Cnpj_invalido(string cnpj) => Assert.False(CnpjAttribute.EhValido(cnpj));

    [Fact]
    public void Normalizar_remove_mascara_e_usa_maiusculas() =>
        Assert.Equal("12ABC34501DE35", CnpjAttribute.Normalizar(" 12.abc.345/01de-35 "));

    [Fact]
    public void Gerador_dos_testes_produz_cnpj_valido()
    {
        for (var i = 0; i < 50; i++)
        {
            Assert.True(CnpjAttribute.EhValido(Dados.NovoCnpj()));
        }
    }

    [Fact]
    public void Usuario_sem_cnpj_nao_pode_organizar()
    {
        var usuario = Dados.NovoUsuario();

        var erro = Assert.Throws<RegraDeNegocioException>(usuario.GarantirPodeOrganizar);

        Assert.Equal("CNPJ_NAO_VERIFICADO", erro.Codigo);
        Assert.Equal(403, erro.StatusHttp);
    }

    [Fact]
    public void Usuario_com_cnpj_verificado_pode_organizar()
    {
        var usuario = Dados.NovoUsuario();

        usuario.DefinirCnpj("11222333000181", "Centro Acadêmico", Dados.Agora);

        Assert.True(usuario.PodeOrganizar);
        Assert.Equal(Dados.Agora, usuario.CnpjVerificadoEm);
    }

    [Theory]
    [InlineData("""{"razao_social":"ACME LTDA","situacao_cadastral":2,"descricao_situacao_cadastral":"ATIVA"}""", true, "ATIVA")]
    [InlineData("""{"razao_social":"ACME LTDA","descricao_situacao_cadastral":"Ativa"}""", true, "ATIVA")]
    [InlineData("""{"razao_social":"ACME LTDA","situacao_cadastral":"2"}""", true, "CÓDIGO 2")]
    [InlineData("""{"razao_social":"ACME LTDA","situacao_cadastral":8,"descricao_situacao_cadastral":"BAIXADA"}""", false, "BAIXADA")]
    public async Task BrasilApi_interpreta_a_situacao(string json, bool ativo, string situacao)
    {
        using var consulta = Consulta(HttpStatusCode.OK, json);

        var dados = await consulta.ConsultarAsync("11222333000181");

        Assert.NotNull(dados);
        Assert.Equal("ACME LTDA", dados.RazaoSocial);
        Assert.Equal(ativo, dados.Ativo);
        Assert.Equal(situacao, dados.Situacao);
    }

    [Fact]
    public async Task BrasilApi_404_significa_cnpj_desconhecido()
    {
        using var consulta = Consulta(HttpStatusCode.NotFound, """{"message":"CNPJ não encontrado"}""");

        Assert.Null(await consulta.ConsultarAsync("11222333000181"));
    }

    [Fact]
    public async Task BrasilApi_fora_do_ar_lanca_excecao()
    {
        using var consulta = Consulta(HttpStatusCode.ServiceUnavailable, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => consulta.ConsultarAsync("11222333000181"));
    }

    [Fact]
    public async Task BrasilApi_consulta_o_cnpj_no_fim_do_endereco()
    {
        var handler = new HandlerFixo(HttpStatusCode.OK, """{"razao_social":"X","situacao_cadastral":2}""");
        using var consulta = new ConsultaCnpjBrasilApi(handler,
            Options.Create(new ConsultaCnpjOptions { UrlBase = "https://exemplo.test/api/cnpj/v1" }),
            NullLogger<ConsultaCnpjBrasilApi>.Instance);

        await consulta.ConsultarAsync("12ABC34501DE35");

        Assert.Equal("https://exemplo.test/api/cnpj/v1/12ABC34501DE35", handler.Ultima?.ToString());
    }

    private static ConsultaCnpjBrasilApi Consulta(HttpStatusCode status, string json) =>
        new(new HandlerFixo(status, json), Options.Create(new ConsultaCnpjOptions()), NullLogger<ConsultaCnpjBrasilApi>.Instance);

    private sealed class HandlerFixo(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public Uri? Ultima { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ultima = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
