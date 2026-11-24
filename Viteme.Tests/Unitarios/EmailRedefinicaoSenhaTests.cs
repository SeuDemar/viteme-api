using System.Net;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Infrastructure.Externos;

namespace Viteme.Tests.Unitarios;

/// <summary>Modelos locais do e-mail de redefinição e envio pelo SendGrid (sem rede: manipulador HTTP falso).</summary>
public class EmailRedefinicaoSenhaTests
{
    private sealed class EnvioCapturado : IEnvioEmail
    {
        public (string Destinatario, string Assunto, string Texto, string Html)? Ultimo { get; private set; }

        public Task EnviarAsync(string destinatario, string nomeDestinatario, string assunto, string texto, string html,
            CancellationToken cancellationToken = default)
        {
            Ultimo = (destinatario, assunto, texto, html);
            return Task.CompletedTask;
        }
    }

    private sealed class ManipuladorFalso(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Requisicao { get; private set; }

        public string? Corpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken cancellationToken)
        {
            Requisicao = requisicao;
            Corpo = await requisicao.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private static IOptions<EmailOptions> Opcoes() => Options.Create(new EmailOptions
    {
        UrlRedefinicaoSenha = "https://viteme.app/redefinir-senha",
        SendGrid = new SendGridOptions { ChaveApi = "SG.chave-de-teste" },
    });

    [Fact]
    public async Task Os_modelos_locais_sao_preenchidos_com_nome_link_e_prazo()
    {
        var envio = new EnvioCapturado();
        var usuario = new Usuario("Maria Souza", "maria@exemplo.com", "hash");

        await new EmailRedefinicaoSenha(envio, Opcoes()).EnviarAsync(usuario, "tok+en/=", TimeSpan.FromHours(1));

        var email = envio.Ultimo!.Value;
        Assert.Equal("maria@exemplo.com", email.Destinatario);
        Assert.Contains("Olá, Maria!", email.Texto);
        Assert.Contains("https://viteme.app/redefinir-senha?token=tok%2Ben%2F%3D", email.Texto);
        Assert.Contains("1 hora(s)", email.Texto);
        Assert.Contains("<a href=\"https://viteme.app/redefinir-senha?token=tok%2Ben%2F%3D\"", email.Html);
        Assert.DoesNotContain("{{", email.Texto);
        Assert.DoesNotContain("{{", email.Html);
    }

    [Fact]
    public async Task O_nome_do_usuario_e_codificado_no_html()
    {
        var envio = new EnvioCapturado();
        var usuario = new Usuario("<script>alert(1)</script>", "x@exemplo.com", "hash");

        await new EmailRedefinicaoSenha(envio, Opcoes()).EnviarAsync(usuario, "abc", TimeSpan.FromHours(1));

        Assert.DoesNotContain("<script>", envio.Ultimo!.Value.Html);
        Assert.Contains("&lt;script&gt;", envio.Ultimo!.Value.Html);
    }

    [Fact]
    public async Task SendGrid_recebe_bearer_remetente_conteudo_e_sem_rastreio_de_cliques()
    {
        var manipulador = new ManipuladorFalso(HttpStatusCode.Accepted);
        using var envio = new EnvioEmailSendGrid(manipulador, Opcoes());

        await envio.EnviarAsync("maria@exemplo.com", "Maria", "Assunto", "texto", "<p>html</p>");

        Assert.Equal("https://api.sendgrid.com/v3/mail/send", manipulador.Requisicao!.RequestUri!.ToString());
        Assert.Equal("Bearer", manipulador.Requisicao.Headers.Authorization!.Scheme);
        Assert.Equal("SG.chave-de-teste", manipulador.Requisicao.Headers.Authorization.Parameter);
        Assert.Contains("\"maria@exemplo.com\"", manipulador.Corpo);
        Assert.Contains("\"text/html\"", manipulador.Corpo);
        Assert.Contains("\"enable\":false", manipulador.Corpo);
    }

    [Fact]
    public async Task SendGrid_com_resposta_de_erro_lanca_excecao()
    {
        using var envio = new EnvioEmailSendGrid(new ManipuladorFalso(HttpStatusCode.Unauthorized), Opcoes());

        await Assert.ThrowsAsync<HttpRequestException>(() => envio.EnviarAsync("a@b.com", "A", "x", "t", "h"));
    }
}
