using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Viteme.Api.Middlewares;
using Viteme.Domain.Excecoes;

namespace Viteme.Tests.Unitarios;

public class TratamentoExcecoesMiddlewareTests
{
    private static async Task<(DefaultHttpContext Contexto, JsonElement Corpo)> ExecutarAsync(RequestDelegate proximo)
    {
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();
        var middleware = new TratamentoExcecoesMiddleware(proximo, NullLogger<TratamentoExcecoesMiddleware>.Instance);

        await middleware.InvokeAsync(contexto);

        contexto.Response.Body.Position = 0;
        var corpo = await JsonSerializer.DeserializeAsync<JsonElement>(contexto.Response.Body);
        return (contexto, corpo);
    }

    [Fact]
    public async Task Regra_de_negocio_vira_problem_details_com_codigo()
    {
        var (contexto, corpo) = await ExecutarAsync(_ =>
            throw new RegraDeNegocioException("SEM_VAGAS", "Evento sem vagas", "Todas as vagas já foram preenchidas."));

        Assert.Equal(422, contexto.Response.StatusCode);
        Assert.Equal("application/problem+json", contexto.Response.ContentType);
        Assert.Equal("SEM_VAGAS", corpo.GetProperty("codigo").GetString());
        Assert.Equal("https://viteme.app/erros/sem-vagas", corpo.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Erro_inesperado_vira_500_sem_detalhes_internos()
    {
        var (contexto, corpo) = await ExecutarAsync(_ => throw new InvalidOperationException("segredo interno"));

        Assert.Equal(500, contexto.Response.StatusCode);
        Assert.DoesNotContain("segredo interno", corpo.GetRawText());
    }
}
