using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Viteme.Infrastructure.Externos;

/// <summary>Resposta de erro do Asaas. <see cref="Detalhe"/> é a descrição devolvida por ele (texto para o usuário, em português).</summary>
public sealed class ErroAsaasException(string mensagem, string detalhe, System.Net.HttpStatusCode status)
    : HttpRequestException(mensagem, null, status)
{
    public string Detalhe { get; } = detalhe;
}

/// <summary>
/// Cliente HTTP do Asaas que aceita a chave por requisição: a conta raiz usa a da configuração; as chamadas da subconta
/// (situação cadastral, garantia) usam a chave dela. Resposta fora de 2xx sobe como <see cref="ErroAsaasException"/>.
/// </summary>
internal sealed class ApiAsaas : IDisposable
{
    private const int LimiteDiagnostico = 600;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _chaveRaiz;

    public ApiAsaas(HttpMessageHandler handler, GatewayAsaasOptions opcoes)
    {
        // Espaço ou quebra de linha nas pontas (comum ao colar a chave no painel da hospedagem) invalidaria o cabeçalho.
        _chaveRaiz = opcoes.ChaveApi.Trim();
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
            var (detalhe, diagnostico) = await LerErroAsync(resposta, cancellationToken);
            throw new ErroAsaasException(
                $"Asaas respondeu {(int)resposta.StatusCode} em {metodo} {rota}: {detalhe}{diagnostico}", detalhe, resposta.StatusCode);
        }

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Descrição do erro no formato do Asaas (<c>errors[].description</c>). Quando a resposta não vem nesse formato (ex.: 403
    /// de um firewall na frente da API, sem descrição), o diagnóstico leva o corpo cru e os cabeçalhos para o log, para
    /// identificar quem bloqueou. Nunca inclui a chave: ela vai só na requisição.
    /// </summary>
    private static async Task<(string Detalhe, string Diagnostico)> LerErroAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var erro = JsonSerializer.Deserialize<ErroAsaas>(corpo, Json);
            if (erro?.Errors is { Count: > 0 } lista)
            {
                return (string.Join("; ", lista.Select(e => e.Description)), string.Empty);
            }

            // Algumas recusas vêm só com "message" (ex.: 403 "Contas de pessoa física (CPF) não podem criar subcontas").
            if (!string.IsNullOrWhiteSpace(erro?.Message))
            {
                return (erro.Message, string.Empty);
            }
        }
        catch (JsonException)
        {
            // Corpo não é JSON (página de erro de proxy/firewall): segue para o diagnóstico.
        }

        return ("sem detalhe", Diagnostico(resposta, corpo));
    }

    private static string Diagnostico(HttpResponseMessage resposta, string corpo)
    {
        var cabecalhos = resposta.Headers.Concat(resposta.Content.Headers)
            .Where(h => !h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
        var texto = $" [corpo: {Resumir(corpo)}] [cabeçalhos: {string.Join("; ", cabecalhos)}]";
        return texto.Length > LimiteDiagnostico * 2 ? texto[..(LimiteDiagnostico * 2)] + "…" : texto;
    }

    private static string Resumir(string corpo)
    {
        var umaLinha = string.Join(' ', corpo.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return umaLinha.Length == 0 ? "(vazio)" : umaLinha.Length > LimiteDiagnostico ? umaLinha[..LimiteDiagnostico] + "…" : umaLinha;
    }

    private sealed record ErroAsaas(List<DetalheErroAsaas>? Errors, string? Message);

    private sealed record DetalheErroAsaas(string? Code, string? Description);
}
