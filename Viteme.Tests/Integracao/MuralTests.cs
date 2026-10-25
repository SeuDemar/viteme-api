using System.Net;
using System.Text.Json;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E10 (mural) e E16 (meus eventos).</summary>
public class MuralTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> ConsultarAsync(AmbienteApi ambiente, string consulta) =>
        await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos?{consulta}"));

    private static List<string> Titulos(JsonElement pagina) =>
        pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("titulo").GetString()!).ToList();

    [FactBanco]
    public async Task Mural_filtra_por_busca_categoria_e_gratuito()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Oficina de Python {marca}", categoria: "workshop"), publicar: true);
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Palestra de IA {marca}", preco: 30m, categoria: "palestra"), publicar: true);
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Semana Acadêmica {marca}", categoria: "Palestra"), publicar: true);

        var todos = await ConsultarAsync(ambiente, $"busca={marca}");
        var porTexto = await ConsultarAsync(ambiente, $"busca=python%20{marca}");
        var porTextoParcial = await ConsultarAsync(ambiente, $"busca=OFICINA");
        var porCategoria = await ConsultarAsync(ambiente, $"busca={marca}&categoria=PALESTRA");
        var gratuitos = await ConsultarAsync(ambiente, $"busca={marca}&gratuito=true");
        var pagos = await ConsultarAsync(ambiente, $"busca={marca}&gratuito=false");

        Assert.Equal(3, todos.GetProperty("total").GetInt32());
        Assert.Equal([$"Oficina de Python {marca}"], Titulos(porTexto));
        Assert.Contains($"Oficina de Python {marca}", Titulos(porTextoParcial));
        Assert.Equal(2, porCategoria.GetProperty("total").GetInt32());
        Assert.Equal(2, gratuitos.GetProperty("total").GetInt32());
        Assert.Equal([$"Palestra de IA {marca}"], Titulos(pagos));
    }

    [FactBanco]
    public async Task Busca_trata_porcentagem_e_sublinhado_como_texto_comum()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Evento {marca}"), publicar: true);

        var curinga = await ConsultarAsync(ambiente, $"busca=%25{marca[..6]}");
        var sublinhado = await ConsultarAsync(ambiente, "busca=_");

        Assert.Equal(0, curinga.GetProperty("total").GetInt32());
        Assert.DoesNotContain(Titulos(sublinhado), t => t.Contains(marca));
    }

    [FactBanco]
    public async Task Mural_ordena_por_data_de_inicio_e_pagina_os_resultados()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"C {marca}", inicioEm: TimeSpan.FromDays(30)), publicar: true);
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"A {marca}", inicioEm: TimeSpan.FromDays(10)), publicar: true);
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"B {marca}", inicioEm: TimeSpan.FromDays(20)), publicar: true);

        var primeira = await ConsultarAsync(ambiente, $"busca={marca}&tamanhoPagina=2&pagina=1");
        var segunda = await ConsultarAsync(ambiente, $"busca={marca}&tamanhoPagina=2&pagina=2");

        Assert.Equal([$"A {marca}", $"B {marca}"], Titulos(primeira));
        Assert.Equal([$"C {marca}"], Titulos(segunda));
        Assert.Equal(3, primeira.GetProperty("total").GetInt32());
        Assert.Equal(2, primeira.GetProperty("tamanhoPagina").GetInt32());
        Assert.Equal(2, segunda.GetProperty("pagina").GetInt32());
    }

    [FactBanco]
    public async Task Mural_filtra_por_intervalo_de_datas_inclusive()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        var hoje = ambiente.Relogio.UtcAgora.Date;
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Cedo {marca}", inicioEm: TimeSpan.FromDays(5) + TimeSpan.FromHours(1)), publicar: true);
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Tarde {marca}", inicioEm: TimeSpan.FromDays(40)), publicar: true);
        var diaCedo = DateOnly.FromDateTime(ambiente.Relogio.UtcAgora.AddDays(5).AddHours(1));

        var exato = await ConsultarAsync(ambiente, $"busca={marca}&dataDe={diaCedo:yyyy-MM-dd}&dataAte={diaCedo:yyyy-MM-dd}");
        var depois = await ConsultarAsync(ambiente, $"busca={marca}&dataDe={diaCedo.AddDays(1):yyyy-MM-dd}");

        Assert.Equal([$"Cedo {marca}"], Titulos(exato));
        Assert.Equal([$"Tarde {marca}"], Titulos(depois));
        _ = hoje;
    }

    [FactBanco]
    public async Task Mural_mostra_apenas_publicados_e_ainda_nao_encerrados()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Rascunho {marca}"));
        var cancelado = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Cancelado {marca}"), publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{cancelado}/cancelar", new { motivo = "Cancelado por falta de público" }, organizador.Token);
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Ativo {marca}"), publicar: true);

        var mural = await ConsultarAsync(ambiente, $"busca={marca}");
        ambiente.Relogio.Avancar(TimeSpan.FromDays(11));
        var depoisDoFim = await ConsultarAsync(ambiente, $"busca={marca}");

        Assert.Equal([$"Ativo {marca}"], Titulos(mural));
        Assert.Equal(0, depoisDoFim.GetProperty("total").GetInt32());
    }

    [FactBanco]
    public async Task Mural_informa_vagas_restantes_e_ilimitado_como_nulo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        var limitado = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Limitado {marca}", capacidade: 5), publicar: true);
        await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Ilimitado {marca}"), publicar: true);
        await ambiente.InscreverNoBancoAsync(limitado, (await ambiente.NovaSessaoAsync()).UsuarioId);
        await ambiente.InscreverNoBancoAsync(limitado, (await ambiente.NovaSessaoAsync()).UsuarioId, StatusInscricao.PendentePagamento);

        var itens = (await ConsultarAsync(ambiente, $"busca={marca}")).GetProperty("itens");
        var porTitulo = itens.EnumerateArray().ToDictionary(i => i.GetProperty("titulo").GetString()!);

        Assert.Equal(3, porTitulo[$"Limitado {marca}"].GetProperty("vagasRestantes").GetInt32());
        Assert.Equal(JsonValueKind.Null, porTitulo[$"Ilimitado {marca}"].GetProperty("vagasRestantes").ValueKind);
    }

    [FactBanco]
    public async Task Reserva_vencida_nao_ocupa_vaga_no_mural()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Reserva {marca}", capacidade: 4), publicar: true);
        await ambiente.InscreverNoBancoAsync(id, (await ambiente.NovaSessaoAsync()).UsuarioId, StatusInscricao.PendentePagamento);

        var antes = (await ConsultarAsync(ambiente, $"busca={marca}")).GetProperty("itens")[0].GetProperty("vagasRestantes").GetInt32();
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        var depois = (await ConsultarAsync(ambiente, $"busca={marca}")).GetProperty("itens")[0].GetProperty("vagasRestantes").GetInt32();

        Assert.Equal(3, antes);
        Assert.Equal(4, depois);
    }

    [FactBanco]
    public async Task Mural_rejeita_parametros_invalidos()
    {
        using var ambiente = new AmbienteApi(banco, api);

        foreach (var consulta in new[]
        {
            "tamanhoPagina=51", "tamanhoPagina=0", "pagina=0",
            "dataDe=2026-12-31&dataAte=2026-01-01", "dataDe=amanha", "gratuito=talvez",
        })
        {
            var resposta = await ambiente.GetAsync($"/api/v1/eventos?{consulta}");
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        }
    }

    [FactBanco]
    public async Task Meus_eventos_lista_organizados_e_colaborados_com_o_papel()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var rascunho = await ambiente.CriarEventoAsync(organizador);
        var publicado = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{publicado}/colaboradores", new { email = emailColab }, organizador.Token);
        var proprio = await ambiente.CriarEventoAsync(colaborador, publicar: true);

        var doOrganizador = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/eventos/meus", organizador.Token));
        var doColaborador = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/eventos/meus", colaborador.Token));
        var soColaborando = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/eventos/meus?papel=colaborador", colaborador.Token));
        var soPublicados = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/eventos/meus?status=PUBLICADO", organizador.Token));

        Assert.Equal(2, doOrganizador.GetArrayLength());
        Assert.All(doOrganizador.EnumerateArray(), e => Assert.Equal("ORGANIZADOR", e.GetProperty("papel").GetString()));
        Assert.Equal(2, doColaborador.GetArrayLength());
        Assert.Equal(publicado, soColaborando[0].GetProperty("id").GetGuid());
        Assert.Equal(1, soColaborando.GetArrayLength());
        Assert.Equal(publicado, soPublicados[0].GetProperty("id").GetGuid());
        Assert.Equal(1, soPublicados.GetArrayLength());
        Assert.DoesNotContain(doColaborador.EnumerateArray(), e => e.GetProperty("id").GetGuid() == rascunho);
        _ = proprio;
    }

    [FactBanco]
    public async Task Meus_eventos_exige_autenticacao_e_rejeita_papel_invalido()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var semToken = await ambiente.GetAsync("/api/v1/eventos/meus");
        var papelInvalido = await ambiente.GetAsync("/api/v1/eventos/meus?papel=participante", sessao.Token);
        var statusInvalido = await ambiente.GetAsync("/api/v1/eventos/meus?status=XYZ", sessao.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, papelInvalido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, statusInvalido.StatusCode);
    }
}
