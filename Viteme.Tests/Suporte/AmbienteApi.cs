using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Viteme.Tests.Suporte;

public record Sessao(string Token, string Refresh, JsonElement Corpo);

/// <summary>
/// API em memória ligada a um banco temporário, com relógio e SMS controlados pelo teste.
/// Cookies são tratados à mão para os testes enxergarem e reenviarem o refresh token.
/// </summary>
public sealed class AmbienteApi : IDisposable
{
    public const string Senha = "Senha123";

    private readonly WebApplicationFactory<Program> _fabrica;

    public AmbienteApi(BancoFixture banco, ApiFactory api, bool limitacao = false)
    {
        Banco = banco;
        _fabrica = api.ComBanco(banco.Conexao, Relogio, Sms, limitacao);
        Cliente = _fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    public BancoFixture Banco { get; }

    public RelogioFixo Relogio { get; } = new(DateTime.UtcNow);

    public SmsEmMemoria Sms { get; } = new();

    public HttpClient Cliente { get; }

    public static string EmailNovo() => $"{Guid.NewGuid():N}@exemplo.com";

    public Task<HttpResponseMessage> EnviarAsync(
        HttpMethod metodo, string rota, object? corpo = null, string? token = null, string? refresh = null)
    {
        var requisicao = new HttpRequestMessage(metodo, rota);
        if (corpo is not null)
        {
            requisicao.Content = JsonContent.Create(corpo);
        }

        if (token is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (refresh is not null)
        {
            requisicao.Headers.Add("Cookie", $"refresh_token={refresh}");
        }

        return Cliente.SendAsync(requisicao);
    }

    public Task<HttpResponseMessage> PostAsync(string rota, object? corpo = null, string? token = null, string? refresh = null) =>
        EnviarAsync(HttpMethod.Post, rota, corpo, token, refresh);

    public Task<HttpResponseMessage> GetAsync(string rota, string? token = null) =>
        EnviarAsync(HttpMethod.Get, rota, null, token);

    public Task<HttpResponseMessage> PutAsync(string rota, object corpo, string? token = null) =>
        EnviarAsync(HttpMethod.Put, rota, corpo, token);

    public async Task<string> CadastrarAsync(string? telefone = null, string? email = null)
    {
        email ??= EmailNovo();
        var resposta = await PostAsync("/api/v1/auth/cadastro", new { nome = "Maria Souza", email, senha = Senha, telefone });
        resposta.EnsureSuccessStatusCode();
        return email;
    }

    public async Task<Sessao> LoginAsync(string email, string senha = Senha)
    {
        var resposta = await PostAsync("/api/v1/auth/login", new { email, senha });
        resposta.EnsureSuccessStatusCode();
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        return new Sessao(corpo.GetProperty("tokenAcesso").GetString()!, ExtrairRefresh(resposta)!, corpo);
    }

    /// <summary>Cadastra e autentica um usuário novo.</summary>
    public async Task<Sessao> NovaSessaoAsync(string? telefone = null)
    {
        var email = await CadastrarAsync(telefone);
        return await LoginAsync(email);
    }

    public static string? ExtrairRefresh(HttpResponseMessage resposta)
    {
        if (!resposta.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var cookie = cookies.FirstOrDefault(c => c.StartsWith("refresh_token=", StringComparison.Ordinal));
        return cookie?.Split(';')[0]["refresh_token=".Length..];
    }

    /// <summary>Lê o corpo JSON como texto, o que permite ao teste lê-lo mais de uma vez.</summary>
    public static async Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task<string?> CodigoErroAsync(HttpResponseMessage resposta) =>
        (await CorpoAsync(resposta)).GetProperty("codigo").GetString();

    public void Dispose()
    {
        Cliente.Dispose();
        _fabrica.Dispose();
    }
}
