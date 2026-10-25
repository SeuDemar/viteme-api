using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E38 e E39: certificado de participação em PDF e sua validação pública. Casos CT56–CT57 e regra RN21.</summary>
public class CertificadosTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private sealed record Cenario(Sessao Organizador, Sessao Participante, Guid Evento, Guid Inscricao, string Codigo);

    /// <summary>Evento de 4 horas que começa em 3, com um participante confirmado. Retorna o relógio no início do check-in.</summary>
    private static async Task<Cenario> MontarAsync(AmbienteApi ambiente, short? cargaHoraria = 8)
    {
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(
            organizador,
            ambiente.CorpoEvento(titulo: "Semana Acadêmica de Computação", inicioEm: TimeSpan.FromHours(3), duracao: TimeSpan.FromHours(4), cargaHoraria: cargaHoraria),
            publicar: true);
        var inscricao = await ambiente.InscreverComSucessoAsync(participante, evento);
        var minhas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas", participante.Token));
        return new Cenario(organizador, participante, evento, inscricao, minhas[0].GetProperty("codigoCheckin").GetString()!);
    }

    private static async Task FazerCheckinAsync(AmbienteApi ambiente, Cenario c)
    {
        ambiente.Relogio.Avancar(TimeSpan.FromHours(2)); // 1 h antes do início
        (await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Organizador.Token)).EnsureSuccessStatusCode();
    }

    private static async Task EncerrarAsync(AmbienteApi ambiente)
    {
        ambiente.Relogio.Avancar(TimeSpan.FromHours(6));
        await ambiente.ExecutarAsync(async s => await s.GetRequiredService<EncerramentoEventosService>().EncerrarVencidosAsync());
    }

    private static async Task<string> CodigoDoPdfAsync(HttpResponseMessage resposta)
    {
        var texto = Encoding.Latin1.GetString(await resposta.Content.ReadAsByteArrayAsync());
        return System.Text.RegularExpressions.Regex.Match(texto, @"C\\363digo de valida\\347\\343o: ([A-Z0-9]+)").Groups[1].Value;
    }

    [FactBanco]
    public async Task CT56_participante_com_presenca_em_evento_encerrado_baixa_o_pdf_com_o_mesmo_codigo_sempre()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        await FazerCheckinAsync(ambiente, c);
        await EncerrarAsync(ambiente);

        var primeira = await ambiente.GetAsync($"/api/v1/inscricoes/{c.Inscricao}/certificado", c.Participante.Token);
        var segunda = await ambiente.GetAsync($"/api/v1/inscricoes/{c.Inscricao}/certificado", c.Participante.Token);
        var bytes = await primeira.Content.ReadAsByteArrayAsync();
        var codigo = await CodigoDoPdfAsync(primeira);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal("application/pdf", primeira.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal(16, codigo.Length);
        Assert.Equal(codigo, await CodigoDoPdfAsync(segunda));
        Assert.Equal($"certificado-{codigo}.pdf", primeira.Content.Headers.ContentDisposition!.FileName ?? primeira.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Contains("Semana Acad", Encoding.Latin1.GetString(bytes));
    }

    [FactBanco]
    public async Task CT57_certificado_sem_presenca_evento_nao_encerrado_ou_sem_carga_horaria_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var semPresenca = await MontarAsync(ambiente);
        var semCarga = await MontarAsync(ambiente, cargaHoraria: null);
        var naoEncerrado = await MontarAsync(ambiente);
        foreach (var c in new[] { semCarga, naoEncerrado })
        {
            await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Organizador.Token);
        }

        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));
        foreach (var c in new[] { semCarga, naoEncerrado })
        {
            (await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Organizador.Token)).EnsureSuccessStatusCode();
        }

        var antesDoFim = await ambiente.GetAsync($"/api/v1/inscricoes/{naoEncerrado.Inscricao}/certificado", naoEncerrado.Participante.Token);
        await EncerrarAsync(ambiente);
        var semPresencaResp = await ambiente.GetAsync($"/api/v1/inscricoes/{semPresenca.Inscricao}/certificado", semPresenca.Participante.Token);
        var semCargaResp = await ambiente.GetAsync($"/api/v1/inscricoes/{semCarga.Inscricao}/certificado", semCarga.Participante.Token);
        var ok = await ambiente.GetAsync($"/api/v1/inscricoes/{naoEncerrado.Inscricao}/certificado", naoEncerrado.Participante.Token);

        Assert.Equal((HttpStatusCode)422, antesDoFim.StatusCode);
        Assert.Equal("CERTIFICADO_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(antesDoFim));
        Assert.Equal("CERTIFICADO_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(semPresencaResp));
        Assert.Equal("CERTIFICADO_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(semCargaResp));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [FactBanco]
    public async Task Certificado_de_outro_usuario_ou_inexistente_retorna_404_e_sem_login_401()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        await FazerCheckinAsync(ambiente, c);
        await EncerrarAsync(ambiente);
        var intruso = await ambiente.NovaSessaoAsync();

        var alheio = await ambiente.GetAsync($"/api/v1/inscricoes/{c.Inscricao}/certificado", intruso.Token);
        var inexistente = await ambiente.GetAsync($"/api/v1/inscricoes/{Guid.NewGuid()}/certificado", c.Participante.Token);
        var anonimo = await ambiente.GetAsync($"/api/v1/inscricoes/{c.Inscricao}/certificado");

        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [FactBanco]
    public async Task Duas_emissoes_simultaneas_geram_um_unico_certificado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        await FazerCheckinAsync(ambiente, c);
        await EncerrarAsync(ambiente);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => ambiente.GetAsync($"/api/v1/inscricoes/{c.Inscricao}/certificado", c.Participante.Token)));
        var codigos = new HashSet<string>();
        foreach (var r in respostas)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            codigos.Add(await CodigoDoPdfAsync(r));
        }

        Assert.Single(codigos);
    }

    [FactBanco]
    public async Task Validacao_publica_retorna_os_dados_do_certificado_sem_login_e_ignora_caixa()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        await FazerCheckinAsync(ambiente, c);
        await EncerrarAsync(ambiente);
        var codigo = await CodigoDoPdfAsync(await ambiente.GetAsync($"/api/v1/inscricoes/{c.Inscricao}/certificado", c.Participante.Token));

        var resposta = await ambiente.GetAsync($"/api/v1/certificados/{codigo.ToLowerInvariant()}/validacao");
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(corpo.GetProperty("valido").GetBoolean());
        Assert.Equal("Maria Souza", corpo.GetProperty("participante").GetString());
        Assert.Equal("Semana Acadêmica de Computação", corpo.GetProperty("evento").GetString());
        Assert.Equal(8, corpo.GetProperty("cargaHoraria").GetInt32());
        Assert.False(corpo.TryGetProperty("email", out _));
    }

    [FactBanco]
    public async Task Validacao_com_codigo_desconhecido_404_e_formato_invalido_400()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var desconhecido = await ambiente.GetAsync("/api/v1/certificados/ABCDEF1234567890/validacao");
        var longo = await ambiente.GetAsync($"/api/v1/certificados/{new string('A', 21)}/validacao");
        var simbolos = await ambiente.GetAsync("/api/v1/certificados/AB-12/validacao");

        Assert.Equal(HttpStatusCode.NotFound, desconhecido.StatusCode);
        Assert.Equal("CERTIFICADO_NAO_ENCONTRADO", await AmbienteApi.CodigoErroAsync(desconhecido));
        Assert.Equal(HttpStatusCode.BadRequest, longo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, simbolos.StatusCode);
    }

    [FactBanco]
    public async Task Validacao_publica_tem_limite_de_consultas_por_minuto()
    {
        using var ambiente = new AmbienteApi(banco, api, limitacao: true);
        var codigos = new List<HttpStatusCode>();

        for (var i = 0; i < 31; i++)
        {
            codigos.Add((await ambiente.GetAsync("/api/v1/certificados/ABCDEF1234567890/validacao")).StatusCode);
        }

        Assert.Equal(30, codigos.Count(c => c == HttpStatusCode.NotFound));
        Assert.Equal(HttpStatusCode.TooManyRequests, codigos[30]);
    }
}
