using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Tests.Suporte;

public record Sessao(string Token, string Refresh, JsonElement Corpo)
{
    public Guid UsuarioId => Corpo.GetProperty("usuario").GetProperty("id").GetGuid();
}

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
        _fabrica = api.ComBanco(banco.Conexao, Relogio, Sms, Gateway, limitacao);
        Cliente = _fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    public BancoFixture Banco { get; }

    public RelogioFixo Relogio { get; } = new(DateTime.UtcNow);

    public SmsEmMemoria Sms { get; } = new();

    public GatewayEmMemoria Gateway { get; } = new();

    public HttpClient Cliente { get; }

    /// <summary>Contêiner de serviços da aplicação, para executar serviços (ex.: tarefas) dentro de um escopo.</summary>
    public IServiceProvider Servicos => _fabrica.Services;

    public static string EmailNovo() => $"{Guid.NewGuid():N}@exemplo.com";

    public Task<HttpResponseMessage> EnviarAsync(
        HttpMethod metodo, string rota, object? corpo = null, string? token = null, string? refresh = null, string? ifMatch = null)
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

        if (ifMatch is not null)
        {
            requisicao.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return Cliente.SendAsync(requisicao);
    }

    public Task<HttpResponseMessage> PostAsync(string rota, object? corpo = null, string? token = null, string? refresh = null) =>
        EnviarAsync(HttpMethod.Post, rota, corpo, token, refresh);

    public Task<HttpResponseMessage> GetAsync(string rota, string? token = null) =>
        EnviarAsync(HttpMethod.Get, rota, null, token);

    public Task<HttpResponseMessage> PutAsync(string rota, object corpo, string? token = null, string? ifMatch = null) =>
        EnviarAsync(HttpMethod.Put, rota, corpo, token, null, ifMatch);

    public Task<HttpResponseMessage> DeleteAsync(string rota, string? token = null) =>
        EnviarAsync(HttpMethod.Delete, rota, null, token);

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

    /// <summary>Cadastra um usuário com telefone verificado e aceite de SMS ativo (RN16).</summary>
    public async Task<Sessao> NovaSessaoComSmsAsync()
    {
        var telefone = $"+55449{Random.Shared.Next(10000000, 99999999)}";
        var sessao = await NovaSessaoAsync(telefone);
        (await PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token)).EnsureSuccessStatusCode();
        (await PostAsync("/api/v1/usuarios/me/telefone/verificar", new { codigo = Sms.UltimoCodigo() }, sessao.Token)).EnsureSuccessStatusCode();
        (await PutAsync("/api/v1/usuarios/me/preferencias-sms", new { aceitaSms = true }, sessao.Token)).EnsureSuccessStatusCode();
        Sms.Enviados.Clear();
        return sessao;
    }

    /// <summary>Executa algo com os serviços da aplicação, em um escopo próprio (ex.: tarefas periódicas).</summary>
    public async Task<T> ExecutarAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        using var escopo = Servicos.CreateScope();
        return await acao(escopo.ServiceProvider);
    }

    public Task<HttpResponseMessage> PatchAsync(string rota, string? token = null) =>
        EnviarAsync(HttpMethod.Patch, rota, null, token);

    // ---- Eventos ----------------------------------------------------------------------------------

    /// <summary>Data sem frações de segundo (o banco guarda microssegundos).</summary>
    public static DateTime Truncar(DateTime d) => new(d.Ticks - (d.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    /// <summary>Corpo válido de evento, com datas relativas ao relógio do teste.</summary>
    public object CorpoEvento(
        string? titulo = null, decimal preco = 0, int? capacidade = null, string local = "Auditório",
        string categoria = "palestra", TimeSpan? inicioEm = null, TimeSpan? duracao = null, short? cargaHoraria = 8)
    {
        var agora = Truncar(Relogio.UtcAgora);
        var inicio = agora + (inicioEm ?? TimeSpan.FromDays(10));
        return new
        {
            titulo = titulo ?? $"Evento {Guid.NewGuid():N}",
            descricao = "Palestras e oficinas para a comunidade acadêmica.",
            categoria,
            local,
            dataInicio = inicio,
            dataFim = inicio + (duracao ?? TimeSpan.FromHours(8)),
            inscricaoInicio = agora.AddDays(-1),
            inscricaoFim = inicio.AddHours(-1),
            capacidade,
            preco,
            cargaHoraria,
        };
    }

    /// <summary>Corpo de edição montado a partir do detalhe lido, com as mudanças informadas.</summary>
    public static Dictionary<string, object?> CorpoDeEdicao(JsonElement detalhe, params (string Campo, object? Valor)[] mudancas)
    {
        string[] campos =
        [
            "titulo", "descricao", "categoria", "local", "imagemUrl", "dataInicio", "dataFim",
            "inscricaoInicio", "inscricaoFim", "capacidade", "preco", "cargaHoraria",
        ];

        var corpo = campos.ToDictionary(c => c, c => (object?)detalhe.GetProperty(c).Clone());
        foreach (var (campo, valor) in mudancas)
        {
            corpo[campo] = valor;
        }

        return corpo;
    }

    /// <summary>Cria um evento (rascunho) e, se pedido, publica. Retorna o id.</summary>
    public async Task<Guid> CriarEventoAsync(Sessao organizador, object? corpo = null, bool publicar = false)
    {
        var resposta = await PostAsync("/api/v1/eventos", corpo ?? CorpoEvento(), organizador.Token);
        resposta.EnsureSuccessStatusCode();
        var id = (await CorpoAsync(resposta)).GetProperty("id").GetGuid();

        if (publicar)
        {
            var publicacao = await PostAsync($"/api/v1/eventos/{id}/publicar", token: organizador.Token);
            publicacao.EnsureSuccessStatusCode();
        }

        return id;
    }

    public async Task<(JsonElement Corpo, string Etag)> DetalheAsync(Guid eventoId, string? token = null)
    {
        var resposta = await GetAsync($"/api/v1/eventos/{eventoId}", token);
        resposta.EnsureSuccessStatusCode();
        return (await CorpoAsync(resposta), resposta.Headers.ETag!.ToString());
    }

    public async Task InscreverNoBancoAsync(Guid eventoId, Guid usuarioId, StatusInscricao status = StatusInscricao.Confirmada)
    {
        await using var contexto = Banco.CriarContexto(Relogio);
        var codigo = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        contexto.Add(status == StatusInscricao.PendentePagamento
            ? Inscricao.CriarPendentePagamento(eventoId, usuarioId, codigo, Relogio.UtcAgora)
            : Inscricao.CriarGratuita(eventoId, usuarioId, codigo));
        await contexto.SaveChangesAsync();
    }

    public async Task InteresseNoBancoAsync(Guid eventoId, Guid usuarioId)
    {
        await using var contexto = Banco.CriarContexto(Relogio);
        contexto.Add(new InteresseEvento(usuarioId, eventoId));
        await contexto.SaveChangesAsync();
    }

    // ---- Inscrições e pagamentos ------------------------------------------------------------------

    public Task<HttpResponseMessage> InscreverAsync(Sessao participante, Guid eventoId) =>
        PostAsync($"/api/v1/eventos/{eventoId}/inscricoes", token: participante.Token);

    /// <summary>Inscreve e exige sucesso. Retorna o id da inscrição.</summary>
    public async Task<Guid> InscreverComSucessoAsync(Sessao participante, Guid eventoId)
    {
        var resposta = await InscreverAsync(participante, eventoId);
        resposta.EnsureSuccessStatusCode();
        return (await CorpoAsync(resposta)).GetProperty("id").GetGuid();
    }

    /// <summary>Inscrição em evento pago: retorna a inscrição, o pagamento e o id de transação do gateway.</summary>
    public async Task<(Guid InscricaoId, Guid PagamentoId, string IdTransacao, decimal Valor)> InscreverEmEventoPagoAsync(
        Sessao participante, Guid eventoId)
    {
        var resposta = await InscreverAsync(participante, eventoId);
        resposta.EnsureSuccessStatusCode();
        var corpo = await CorpoAsync(resposta);
        var pagamento = corpo.GetProperty("pagamento");
        var pagamentoId = pagamento.GetProperty("id").GetGuid();
        return (corpo.GetProperty("id").GetGuid(), pagamentoId, $"stub_{pagamentoId:N}", pagamento.GetProperty("valor").GetDecimal());
    }

    /// <summary>Envia um webhook ao endpoint do gateway, assinado como o gateway de teste (ou com a assinatura informada).</summary>
    public async Task<HttpResponseMessage> WebhookAsync(
        string idTransacao, Guid referenciaExterna, string status, decimal valor, string? assinatura = null, string? corpoBruto = null)
    {
        var corpo = corpoBruto ?? System.Text.Json.JsonSerializer.Serialize(
            new { idTransacao, referenciaExterna, status, valor, metodo = "PIX" });

        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pagamentos/webhook")
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
        };
        requisicao.Headers.Add("X-Viteme-Assinatura", assinatura ?? Gateway.Assinar(corpo));
        return await Cliente.SendAsync(requisicao);
    }

    public Task<HttpResponseMessage> WebhookDeAsync((Guid InscricaoId, Guid PagamentoId, string IdTransacao, decimal Valor) pagamento, string status) =>
        WebhookAsync(pagamento.IdTransacao, pagamento.PagamentoId, status, pagamento.Valor);

    public async Task<string> StatusDoPagamentoAsync(Sessao dono, Guid inscricaoId) =>
        (await CorpoAsync(await GetAsync($"/api/v1/inscricoes/{inscricaoId}/pagamento", dono.Token))).GetProperty("status").GetString()!;

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
