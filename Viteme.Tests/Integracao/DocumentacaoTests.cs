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
        var caminhos = documento.GetProperty("paths");
        foreach (var caminho in new[]
        {
            "/api/v1/saude", "/api/v1/auth/login", "/api/v1/usuarios/me", "/api/v1/eventos", "/api/v1/eventos/{id}",
            "/api/v1/eventos/{id}/atividades/{atividadeId}", "/api/v1/eventos/{id}/colaboradores",
            "/api/v1/eventos/{id}/inscricoes", "/api/v1/inscricoes/minhas", "/api/v1/inscricoes/{id}/pagamento",
            "/api/v1/pagamentos/webhook", "/api/v1/eventos/{id}/checkin",
        })
        {
            Assert.True(caminhos.TryGetProperty(caminho, out _), $"faltou {caminho} no OpenAPI");
        }

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
