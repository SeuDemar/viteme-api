using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Viteme.Infrastructure.Externos;

/// <summary>
/// Cliente HTTP do Asaas que aceita a chave por requisição: a conta raiz usa a da configuração; as chamadas da subconta
/// (situação cadastral, garantia) usam a chave dela. Resposta fora de 2xx sobe como <see cref="HttpRequestException"/>.
/// </summary>
internal sealed class ApiAsaas : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _chaveRaiz;

    public ApiAsaas(HttpMessageHandler handler, GatewayAsaasOptions opcoes)
    {
        _chaveRaiz = opcoes.ChaveApi;
        _http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(opcoes.UrlBase.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(opcoes.TimeoutSegundos),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse(opcoes.NomeAplicacao));
    }

    public async Task<T> EnviarAsync<T>(HttpMethod metodo, string rota, object? corpo, string? chave, CancellationToken cancellationToken)
    {
        using var requisicao = new HttpRequestMessage(metodo, rota);
        requisicao.Headers.Add("access_token", chave ?? _chaveRaiz);
        if (corpo is not null)
        {
            requisicao.Content = JsonContent.Create(corpo, options: Json);
        }

        using var resposta = await _http.SendAsync(requisicao, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
        {
            var detalhe = await LerErroAsync(resposta, cancellationToken);
            throw new HttpRequestException($"Asaas respondeu {(int)resposta.StatusCode} em {metodo} {rota}: {detalhe}", null, resposta.StatusCode);
        }

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    public void Dispose() => _http.Dispose();

    private static async Task<string> LerErroAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        try
        {
            var erro = await resposta.Content.ReadFromJsonAsync<ErroAsaas>(Json, cancellationToken);
            return erro?.Errors is { Count: > 0 } lista ? string.Join("; ", lista.Select(e => e.Description)) : "sem detalhe";
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return "resposta não é JSON";
        }
    }

    private sealed record ErroAsaas(List<DetalheErroAsaas>? Errors);

    private sealed record DetalheErroAsaas(string? Code, string? Description);
}
