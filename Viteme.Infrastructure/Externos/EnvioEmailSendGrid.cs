using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Externos;

public class EmailOptions
{
    public const string Secao = "Email";

    public string RemetenteEmail { get; set; } = "nao-responda@viteme.app";

    public string RemetenteNome { get; set; } = "Viteme";

    /// <summary>Endereço da tela de redefinição no frontend; o token vai em <c>?token=</c>.</summary>
    public string UrlRedefinicaoSenha { get; set; } = "https://viteme.app/redefinir-senha";

    public SendGridOptions SendGrid { get; set; } = new();

    /// <summary>Só no stub (desenvolvimento): escreve o texto do e-mail, com o link, no log. Nunca em produção.</summary>
    public bool StubLogarCorpo { get; set; }
}

public class SendGridOptions
{
    /// <summary>Chave de API do SendGrid (permissão "Mail Send"). Só em variável de ambiente ou user-secrets.</summary>
    public string ChaveApi { get; set; } = string.Empty;

    public string UrlBase { get; set; } = "https://api.sendgrid.com/v3/mail/send";

    public int TimeoutSegundos { get; set; } = 10;
}

/// <summary>Envia o e-mail pela API v3 do SendGrid. Qualquer resposta diferente de 2xx sobe como exceção.</summary>
public sealed class EnvioEmailSendGrid : IEnvioEmail, IDisposable
{
    private readonly HttpClient _http;
    private readonly EmailOptions _opcoes;

    public EnvioEmailSendGrid(IOptions<EmailOptions> opcoes)
        : this(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, opcoes)
    {
    }

    public EnvioEmailSendGrid(HttpMessageHandler handler, IOptions<EmailOptions> opcoes)
    {
        _opcoes = opcoes.Value;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(_opcoes.SendGrid.TimeoutSegundos) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.SendGrid.ChaveApi);
    }

    public async Task EnviarAsync(string destinatario, string nomeDestinatario, string assunto, string texto, string html,
        CancellationToken cancellationToken = default)
    {
        var corpo = new
        {
            personalizations = new[] { new { to = new[] { new { email = destinatario, name = nomeDestinatario } } } },
            from = new { email = _opcoes.RemetenteEmail, name = _opcoes.RemetenteNome },
            subject = assunto,
            content = new[]
            {
                new { type = "text/plain", value = texto },
                new { type = "text/html", value = html },
            },
            // Link de uso único: o rastreamento de cliques reescreveria a URL e passaria o token por um terceiro.
            tracking_settings = new { click_tracking = new { enable = false, enable_text = false } },
        };

        using var resposta = await _http.PostAsJsonAsync(_opcoes.SendGrid.UrlBase, corpo, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"SendGrid respondeu {(int)resposta.StatusCode}.");
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Sem chave do SendGrid configurada: não envia nada (desenvolvimento e testes).</summary>
public sealed class EnvioEmailStub(IOptions<EmailOptions> opcoes, ILogger<EnvioEmailStub> logger) : IEnvioEmail
{
    public Task EnviarAsync(string destinatario, string nomeDestinatario, string assunto, string texto, string html,
        CancellationToken cancellationToken = default)
    {
        if (opcoes.Value.StubLogarCorpo)
        {
            logger.LogWarning("E-mail NÃO enviado (sem chave do SendGrid). Para {Destinatario}: {Assunto}\n{Texto}", destinatario, assunto, texto);
        }
        else
        {
            logger.LogWarning("E-mail NÃO enviado (sem chave do SendGrid): {Assunto}", assunto);
        }

        return Task.CompletedTask;
    }
}
