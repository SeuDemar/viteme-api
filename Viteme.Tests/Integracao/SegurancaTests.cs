using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>Etapa 6: matriz de acesso (papel × endpoint), renovação concorrente, expiração do JWT e limite por telefone.</summary>
public class SegurancaTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private sealed record Papeis(Guid Evento, Sessao Organizador, Sessao Colaborador, Sessao Participante);

    private static async Task<Papeis> MontarAsync(AmbienteApi ambiente)
    {
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        (await ambiente.PostAsync($"/api/v1/eventos/{evento}/colaboradores", new { email = emailColab }, organizador.Token)).EnsureSuccessStatusCode();
        return new Papeis(evento, organizador, colaborador, participante);
    }

    /// <summary>Corpo mínimo; a autorização vem antes da validação, então o corpo só importa para quem passa.</summary>
    private static readonly object CorpoVazio = new { };

    [FactBanco]
    public async Task Matriz_de_acesso_so_organizador_gerencia_o_evento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var p = await MontarAsync(ambiente);
        var id = p.Evento;
        (HttpMethod Metodo, string Rota)[] soOrganizador =
        [
            (HttpMethod.Put, $"/api/v1/eventos/{id}"),
            (HttpMethod.Post, $"/api/v1/eventos/{id}/cancelar"),
            (HttpMethod.Post, $"/api/v1/eventos/{id}/atividades"),
            (HttpMethod.Post, $"/api/v1/eventos/{id}/colaboradores"),
            (HttpMethod.Delete, $"/api/v1/eventos/{id}/colaboradores/{Guid.NewGuid()}"),
        ];

        foreach (var (metodo, rota) in soOrganizador)
        {
            var anonimo = await ambiente.EnviarAsync(metodo, rota, CorpoVazio);
            var participante = await ambiente.EnviarAsync(metodo, rota, CorpoVazio, p.Participante.Token);
            var colaborador = await ambiente.EnviarAsync(metodo, rota, CorpoVazio, p.Colaborador.Token);
            var organizador = await ambiente.EnviarAsync(metodo, rota, CorpoVazio, p.Organizador.Token);

            Assert.True(anonimo.StatusCode == HttpStatusCode.Unauthorized, $"{metodo} {rota} anônimo: {anonimo.StatusCode}");
            Assert.True(participante.StatusCode == HttpStatusCode.Forbidden, $"{metodo} {rota} participante: {participante.StatusCode}");
            Assert.True(colaborador.StatusCode == HttpStatusCode.Forbidden, $"{metodo} {rota} colaborador: {colaborador.StatusCode}");
            Assert.True(organizador.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden), $"{metodo} {rota} organizador: {organizador.StatusCode}");
        }
    }

    [FactBanco]
    public async Task Matriz_de_acesso_inscritos_e_checkin_para_organizador_e_colaborador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var p = await MontarAsync(ambiente);
        (HttpMethod Metodo, string Rota, object? Corpo)[] equipe =
        [
            (HttpMethod.Get, $"/api/v1/eventos/{p.Evento}/inscricoes", null),
            (HttpMethod.Post, $"/api/v1/eventos/{p.Evento}/checkin", new { codigoCheckin = "AAAAAAAAAAAA" }),
        ];

        foreach (var (metodo, rota, corpo) in equipe)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await ambiente.EnviarAsync(metodo, rota, corpo)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ambiente.EnviarAsync(metodo, rota, corpo, p.Participante.Token)).StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, (await ambiente.EnviarAsync(metodo, rota, corpo, p.Colaborador.Token)).StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, (await ambiente.EnviarAsync(metodo, rota, corpo, p.Organizador.Token)).StatusCode);
        }
    }

    [FactBanco]
    public async Task Rascunho_so_aparece_para_organizador_e_colaborador_e_os_outros_recebem_404()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var estranho = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var rascunho = await ambiente.CriarEventoAsync(organizador);
        await ambiente.PostAsync($"/api/v1/eventos/{rascunho}/colaboradores", new { email = emailColab }, organizador.Token);

        Assert.Equal(HttpStatusCode.NotFound, (await ambiente.GetAsync($"/api/v1/eventos/{rascunho}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ambiente.GetAsync($"/api/v1/eventos/{rascunho}", estranho.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ambiente.GetAsync($"/api/v1/eventos/{rascunho}/inscricoes", estranho.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ambiente.GetAsync($"/api/v1/eventos/{rascunho}", colaborador.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ambiente.GetAsync($"/api/v1/eventos/{rascunho}", organizador.Token)).StatusCode);
    }

    [FactBanco]
    public async Task Recursos_pessoais_exigem_login()
    {
        using var ambiente = new AmbienteApi(banco, api);
        string[] rotas =
        [
            "/api/v1/usuarios/me", "/api/v1/inscricoes/minhas", "/api/v1/notificacoes", "/api/v1/eventos/meus",
            $"/api/v1/inscricoes/{Guid.NewGuid()}/pagamento", $"/api/v1/inscricoes/{Guid.NewGuid()}/certificado",
        ];

        foreach (var rota in rotas)
        {
            Assert.True((await ambiente.GetAsync(rota)).StatusCode == HttpStatusCode.Unauthorized, rota);
        }
    }

    [FactBanco]
    public async Task Pagamento_e_inscricao_de_outro_usuario_retornam_404()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var dono = await ambiente.NovaSessaoAsync();
        var intruso = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 25, capacidade: 5), publicar: true);
        var pago = await ambiente.InscreverEmEventoPagoAsync(dono, evento);

        var status = await ambiente.GetAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", intruso.Token);
        var cobrar = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: intruso.Token);
        var cancelar = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/cancelar", token: intruso.Token);

        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cobrar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cancelar.StatusCode);
    }

    [FactBanco]
    public async Task Token_de_acesso_expirado_retorna_401_depois_da_tolerancia()
    {
        using var ambiente = new AmbienteApi(banco, api, tokenRealista: true);
        var sessao = await ambiente.NovaSessaoAsync();

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(15));
        var dentroDaTolerancia = await ambiente.GetAsync("/api/v1/usuarios/me", sessao.Token);
        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(31));
        var expirado = await ambiente.GetAsync("/api/v1/usuarios/me", sessao.Token);

        Assert.Equal(HttpStatusCode.OK, dentroDaTolerancia.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expirado.StatusCode);
        Assert.Equal("NAO_AUTENTICADO", await AmbienteApi.CodigoErroAsync(expirado));
    }

    [FactBanco]
    public async Task Duas_renovacoes_simultaneas_com_o_mesmo_refresh_emitem_um_unico_par()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ambiente.PostAsync("/api/v1/auth/renovar", refresh: sessao.Refresh)));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, respostas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));

        // Quem chega depois do vencedor usa um token já revogado: isso é tratado como reuso e revoga a sessão (RN03/seguranca.md),
        // então no fim sobra no máximo um token ativo, nunca dois.
        await using var contexto = banco.CriarContexto();
        Assert.InRange(contexto.TokensRenovacao.Count(t => t.UsuarioId == sessao.UsuarioId && t.RevogadoEm == null), 0, 1);
    }

    [FactBanco]
    public async Task Telefone_nao_recebe_mais_de_5_codigos_por_hora_mesmo_com_contas_diferentes()
    {
        using var ambiente = new AmbienteApi(banco, api);
        const string telefone = "+5544955550000";
        var codigos = new List<HttpStatusCode>();

        for (var i = 0; i < 6; i++)
        {
            var sessao = await ambiente.NovaSessaoAsync(telefone);
            codigos.Add((await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token)).StatusCode);
        }

        var depois = await ambiente.NovaSessaoAsync(telefone);
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(61));
        var aposUmaHora = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: depois.Token);

        Assert.Equal(5, codigos.Count(c => c == HttpStatusCode.Accepted));
        Assert.Equal(HttpStatusCode.TooManyRequests, codigos[5]);
        Assert.Equal(HttpStatusCode.Accepted, aposUmaHora.StatusCode);
        Assert.Equal(5, ambiente.Sms.Enviados.Count(m => m.Telefone == telefone) - 1);
    }

    [Fact]
    public async Task Respostas_trazem_cabecalhos_de_seguranca_e_nao_podem_ser_guardadas_em_cache()
    {
        using var fabrica = new ApiFactory();
        using var cliente = fabrica.CreateClient();

        var resposta = await cliente.GetAsync("/api/v1/saude");

        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resposta.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", resposta.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", resposta.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task Cors_libera_apenas_a_origem_do_frontend_com_credenciais()
    {
        using var fabrica = new ApiFactory().WithWebHostBuilder(b => b.UseSetting("Cors:OrigemFrontend", "https://app.viteme.test/"));
        using var cliente = fabrica.CreateClient();

        var requisicao = (string origem) => new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login")
        {
            Headers = { { "Origin", origem }, { "Access-Control-Request-Method", "POST" }, { "Access-Control-Request-Headers", "content-type" } },
        };
        var permitida = await cliente.SendAsync(requisicao("https://app.viteme.test"));
        var negada = await cliente.SendAsync(requisicao("https://malicioso.test"));

        Assert.Equal("https://app.viteme.test", permitida.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", permitida.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.False(negada.Headers.Contains("Access-Control-Allow-Origin"));
    }

    /// <summary>
    /// API com a origem do frontend configurada. A conexão aponta para um banco inexistente: os casos aqui não chegam
    /// ao banco (a proteção responde antes, ou não há cookie de renovação).
    /// </summary>
    private static WebApplicationFactory<Program> FabricaComFrontend() => new ApiFactory().WithWebHostBuilder(b =>
    {
        b.UseSetting("Cors:OrigemFrontend", "https://app.viteme.test");
        b.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=1;Database=inexistente;Username=x;Password=x");
    });

    private static HttpRequestMessage Auth(string rota, string? requestedWith, string? origem)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, rota);
        if (requestedWith is not null)
        {
            requisicao.Headers.Add("X-Requested-With", requestedWith);
        }

        if (origem is not null)
        {
            requisicao.Headers.Add("Origin", origem);
        }

        return requisicao;
    }

    [Theory]
    [InlineData("/api/v1/auth/renovar")]
    [InlineData("/api/v1/auth/logout")]
    public async Task Renovar_e_logout_sem_o_cabecalho_do_frontend_retornam_403(string rota)
    {
        using var fabrica = FabricaComFrontend();
        using var cliente = fabrica.CreateClient();

        var semCabecalho = await cliente.SendAsync(Auth(rota, null, "https://app.viteme.test"));
        var valorErrado = await cliente.SendAsync(Auth(rota, "XMLHttpRequest", "https://app.viteme.test"));

        Assert.Equal(HttpStatusCode.Forbidden, semCabecalho.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, valorErrado.StatusCode);
        Assert.Equal("REQUISICAO_NAO_PERMITIDA", await AmbienteApi.CodigoErroAsync(semCabecalho));
    }

    [Theory]
    [InlineData("/api/v1/auth/renovar")]
    [InlineData("/api/v1/auth/logout")]
    public async Task Renovar_e_logout_de_origem_diferente_da_do_frontend_retornam_403(string rota)
    {
        using var fabrica = FabricaComFrontend();
        using var cliente = fabrica.CreateClient();

        var outraOrigem = await cliente.SendAsync(Auth(rota, "viteme-web", "https://malicioso.test"));

        Assert.Equal(HttpStatusCode.Forbidden, outraOrigem.StatusCode);
        Assert.Equal("REQUISICAO_NAO_PERMITIDA", await AmbienteApi.CodigoErroAsync(outraOrigem));
    }

    [Fact]
    public async Task Renovar_com_cabecalho_e_origem_do_frontend_passa_pela_protecao_e_cai_na_regra_normal()
    {
        using var fabrica = FabricaComFrontend();
        using var cliente = fabrica.CreateClient();

        var doFrontend = await cliente.SendAsync(Auth("/api/v1/auth/renovar", "viteme-web", "https://app.viteme.test/"));
        var semOrigem = await cliente.SendAsync(Auth("/api/v1/auth/renovar", "viteme-web", null));
        var logout = await cliente.SendAsync(Auth("/api/v1/auth/logout", "viteme-web", "https://app.viteme.test"));

        Assert.Equal(HttpStatusCode.Unauthorized, doFrontend.StatusCode); // sem cookie: TOKEN_INVALIDO
        Assert.Equal(HttpStatusCode.Unauthorized, semOrigem.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Cors_permite_o_cabecalho_x_requested_with_do_frontend()
    {
        using var fabrica = FabricaComFrontend();
        using var cliente = fabrica.CreateClient();
        var requisicao = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/renovar")
        {
            Headers = { { "Origin", "https://app.viteme.test" }, { "Access-Control-Request-Method", "POST" }, { "Access-Control-Request-Headers", "x-requested-with" } },
        };

        var resposta = await cliente.SendAsync(requisicao);

        Assert.Contains("x-requested-with", resposta.Headers.GetValues("Access-Control-Allow-Headers").Single().ToLowerInvariant());
    }

    [Fact]
    public async Task Sem_origem_configurada_o_cors_nao_libera_ninguem()
    {
        using var fabrica = new ApiFactory();
        using var cliente = fabrica.CreateClient();
        var requisicao = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login")
        {
            Headers = { { "Origin", "https://qualquer.test" }, { "Access-Control-Request-Method", "POST" } },
        };

        var resposta = await cliente.SendAsync(requisicao);

        Assert.False(resposta.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
