using System.Net;
using System.Text;

namespace Viteme.Tests.Suporte;

/// <summary>Manipulador HTTP falso: responde por método e início da rota (após <c>/v3/</c>) e registra cada chamada.</summary>
public sealed class RoteiroHttp : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Casa, HttpStatusCode Status, string Corpo)> _respostas = [];

    public List<(HttpMethod Metodo, string Rota, string? Corpo, HttpRequestMessage Requisicao)> Chamadas { get; } = [];

    public RoteiroHttp Responde(HttpMethod metodo, string rotaComeca, string corpo, HttpStatusCode status = HttpStatusCode.OK)
    {
        _respostas.Add((r => r.Method == metodo && r.RequestUri!.PathAndQuery.StartsWith("/v3/" + rotaComeca, StringComparison.Ordinal), status, corpo));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken cancellationToken)
    {
        var corpo = requisicao.Content is null ? null : await requisicao.Content.ReadAsStringAsync(cancellationToken);
        Chamadas.Add((requisicao.Method, requisicao.RequestUri!.PathAndQuery, corpo, requisicao));

        var resposta = _respostas.FirstOrDefault(r => r.Casa(requisicao));
        if (resposta.Casa is null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }

        return new HttpResponseMessage(resposta.Status) { Content = new StringContent(resposta.Corpo, Encoding.UTF8, "application/json") };
    }
}
