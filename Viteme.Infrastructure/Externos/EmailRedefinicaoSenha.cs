using System.Net;
using System.Reflection;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Externos;

/// <summary>
/// Monta o e-mail de redefinição de senha a partir dos modelos locais (<c>Externos/Emails/redefinicao-senha.html</c> e
/// <c>.txt</c>, embutidos no assembly). Os modelos ficam no repositório: o SendGrid só entrega o e-mail pronto.
/// Marcadores: <c>{{nome}}</c>, <c>{{link}}</c> e <c>{{prazo}}</c>.
/// </summary>
public class EmailRedefinicaoSenha(IEnvioEmail email, IOptions<EmailOptions> opcoes) : IEmailRedefinicaoSenha
{
    private const string Assunto = "Redefinição de senha no Viteme";

    private static readonly Lazy<string> ModeloHtml = new(() => LerModelo("redefinicao-senha.html"));
    private static readonly Lazy<string> ModeloTexto = new(() => LerModelo("redefinicao-senha.txt"));

    public Task EnviarAsync(Usuario usuario, string token, TimeSpan validade, CancellationToken cancellationToken = default)
    {
        var link = $"{opcoes.Value.UrlRedefinicaoSenha}?token={Uri.EscapeDataString(token)}";
        var minutos = (int)validade.TotalMinutes;
        var prazo = minutos % 60 == 0 ? $"{minutos / 60} hora(s)" : $"{minutos} minutos";
        var nome = usuario.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        var texto = Preencher(ModeloTexto.Value, nome, link, prazo, codificar: false);
        var html = Preencher(ModeloHtml.Value, nome, link, prazo, codificar: true);

        return email.EnviarAsync(usuario.Email, usuario.Nome, Assunto, texto, html, cancellationToken);
    }

    // No HTML, os valores são codificados: o nome vem do usuário e não pode injetar marcação.
    internal static string Preencher(string modelo, string nome, string link, string prazo, bool codificar)
    {
        static string Cod(string valor, bool codificar) => codificar ? WebUtility.HtmlEncode(valor) : valor;

        return modelo
            .Replace("{{nome}}", Cod(nome, codificar))
            .Replace("{{link}}", Cod(link, codificar))
            .Replace("{{prazo}}", Cod(prazo, codificar));
    }

    private static string LerModelo(string arquivo)
    {
        var assembly = typeof(EmailRedefinicaoSenha).Assembly;
        var recurso = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Emails.{arquivo}", StringComparison.Ordinal));
        using var fluxo = assembly.GetManifestResourceStream(recurso)!;
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }
}
