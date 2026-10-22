using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

public class DocumentacaoTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task OpenApi_descreve_a_api_e_o_esquema_bearer()
    {
        var cliente = fabrica.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

        var resposta = await cliente.GetAsync("/openapi/v1.json");
        var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Viteme API", documento.GetProperty("info").GetProperty("title").GetString());
        Assert.True(documento.GetProperty("paths").TryGetProperty("/api/v1/saude", out _));
        Assert.Equal("bearer", documento.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("Bearer").GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Swagger_ui_esta_disponivel_em_desenvolvimento()
    {
        var cliente = fabrica.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

        var resposta = await cliente.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Swagger_fica_desligado_em_producao_por_padrao()
    {
        var cliente = fabrica.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        var resposta = await cliente.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Swagger_pode_ser_ligado_em_producao_pela_configuracao()
    {
        var cliente = fabrica.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Swagger:Habilitar", "true");
        }).CreateClient();

        var resposta = await cliente.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
