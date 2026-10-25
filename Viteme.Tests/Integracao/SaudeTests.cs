using System.Net;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

public class SaudeTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Saude_retorna_200()
    {
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.GetAsync("/api/v1/saude");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
