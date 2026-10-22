using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Viteme.Tests.Integracao;

public class SaudeTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Saude_retorna_200()
    {
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.GetAsync("/api/v1/saude");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
