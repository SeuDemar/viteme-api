using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Externos;

public class ConsultaCnpjOptions
{
    public const string Secao = "Cnpj";

    /// <summary>Endereço da consulta; o CNPJ (14 caracteres) é acrescentado ao final.</summary>
    public string UrlBase { get; set; } = "https://brasilapi.com.br/api/cnpj/v1/";

    public int TimeoutSegundos { get; set; } = 10;

    /// <summary>Sem acesso à internet (desenvolvimento): aceita qualquer CNPJ válido como ativo. Nunca em produção.</summary>
    public bool UsarStub { get; set; }
}

/// <summary>
/// Consulta o CNPJ na BrasilAPI (gratuita, sem chave, espelha os dados abertos da Receita Federal).
/// 404 = CNPJ desconhecido; outros erros e timeouts sobem como exceção (o serviço responde 502).
/// </summary>
public sealed class ConsultaCnpjBrasilApi : IConsultaCnpj, IDisposable
{
    // Código da Receita para situação cadastral ATIVA (as demais: 1 nula, 3 suspensa, 4 inapta, 8 baixada).
    private const int SituacaoAtiva = 2;

    private readonly HttpClient _http;
    private readonly ILogger<ConsultaCnpjBrasilApi> _logger;

    public ConsultaCnpjBrasilApi(IOptions<ConsultaCnpjOptions> opcoes, ILogger<ConsultaCnpjBrasilApi> logger)
        : this(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, opcoes, logger)
    {
    }

    public ConsultaCnpjBrasilApi(HttpMessageHandler handler, IOptions<ConsultaCnpjOptions> opcoes, ILogger<ConsultaCnpjBrasilApi> logger)
    {
        _logger = logger;
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(opcoes.Value.UrlBase.EndsWith('/') ? opcoes.Value.UrlBase : opcoes.Value.UrlBase + "/"),
            Timeout = TimeSpan.FromSeconds(opcoes.Value.TimeoutSegundos),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("viteme-api/1.0");
    }

    public async Task<DadosCnpj?> ConsultarAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        using var resposta = await _http.GetAsync(Uri.EscapeDataString(cnpj), cancellationToken);
        if (resposta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!resposta.IsSuccessStatusCode)
        {
            _logger.LogWarning("Consulta de CNPJ respondeu {Status}", (int)resposta.StatusCode);
            resposta.EnsureSuccessStatusCode();
        }

        await using var fluxo = await resposta.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(fluxo, cancellationToken: cancellationToken);
        return Interpretar(json.RootElement);
    }

    /// <summary>Aceita a situação como código (<c>situacao_cadastral: 2</c>) ou como texto (<c>"ATIVA"</c>).</summary>
    public static DadosCnpj Interpretar(JsonElement corpo)
    {
        var razaoSocial = Texto(corpo, "razao_social")
            ?? throw new InvalidOperationException("Resposta da consulta de CNPJ sem razao_social.");
        var descricao = Texto(corpo, "descricao_situacao_cadastral");

        int? codigo = corpo.TryGetProperty("situacao_cadastral", out var s) ? s.ValueKind switch
        {
            JsonValueKind.Number when s.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(s.GetString(), out var n) => n,
            _ => null,
        } : null;

        var ativo = codigo == SituacaoAtiva || string.Equals(descricao, "ATIVA", StringComparison.OrdinalIgnoreCase);
        var situacao = descricao ?? (codigo is { } c ? $"código {c}" : "desconhecida");
        return new DadosCnpj(razaoSocial.Trim(), ativo, situacao.ToUpperInvariant());
    }

    private static string? Texto(JsonElement corpo, string campo) =>
        corpo.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(valor.GetString())
            ? valor.GetString()
            : null;

    public void Dispose() => _http.Dispose();
}

/// <summary>Stub para desenvolvimento sem internet: todo CNPJ com dígitos válidos é tratado como ativo.</summary>
public sealed class ConsultaCnpjStub(ILogger<ConsultaCnpjStub> logger) : IConsultaCnpj
{
    public Task<DadosCnpj?> ConsultarAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[CNPJ stub] consulta simulada para {Cnpj}", cnpj);
        return Task.FromResult<DadosCnpj?>(new DadosCnpj($"Empresa de demonstração {cnpj[..8]}", true, "ATIVA"));
    }
}
