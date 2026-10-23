using System.Net;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E32–E36: acompanhar eventos e caixa de notificações. Regra RN16.</summary>
public class InteresseENotificacoesTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    [FactBanco]
    public async Task Seguir_evento_publicado_retorna_204_e_e_idempotente()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var primeira = await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: seguidor.Token);
        var segunda = await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: seguidor.Token);

        Assert.Equal(HttpStatusCode.NoContent, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, segunda.StatusCode);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, contexto.Interesses.Count(i => i.EventoId == evento && i.UsuarioId == seguidor.UsuarioId));
    }

    [FactBanco]
    public async Task Seguir_sem_login_retorna_401()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{Guid.NewGuid()}/interesse");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Seguir_rascunho_de_outro_organizador_retorna_404_e_evento_cancelado_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoAsync();
        var rascunho = await ambiente.CriarEventoAsync(organizador);
        var cancelado = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{cancelado}/cancelar", new { motivo = "Sem local disponível" }, organizador.Token);

        var doRascunho = await ambiente.PostAsync($"/api/v1/eventos/{rascunho}/interesse", token: seguidor.Token);
        var doCancelado = await ambiente.PostAsync($"/api/v1/eventos/{cancelado}/interesse", token: seguidor.Token);
        var doOrganizadorNoRascunho = await ambiente.PostAsync($"/api/v1/eventos/{rascunho}/interesse", token: organizador.Token);

        Assert.Equal(HttpStatusCode.NotFound, doRascunho.StatusCode);
        Assert.Equal((HttpStatusCode)422, doCancelado.StatusCode);
        Assert.Equal("EVENTO_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(doCancelado));
        Assert.Equal((HttpStatusCode)422, doOrganizadorNoRascunho.StatusCode);
    }

    [FactBanco]
    public async Task Deixar_de_seguir_remove_o_interesse_e_e_idempotente()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: seguidor.Token);

        var primeira = await ambiente.DeleteAsync($"/api/v1/eventos/{evento}/interesse", seguidor.Token);
        var segunda = await ambiente.DeleteAsync($"/api/v1/eventos/{evento}/interesse", seguidor.Token);

        Assert.Equal(HttpStatusCode.NoContent, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, segunda.StatusCode);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(0, contexto.Interesses.Count(i => i.EventoId == evento && i.UsuarioId == seguidor.UsuarioId));
    }

    /// <summary>Gera 3 notificações para o seguidor: cancelamento de 3 eventos que ele acompanha.</summary>
    private static async Task<Sessao> SeguidorComNotificacoesAsync(AmbienteApi ambiente, int quantidade = 3)
    {
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoAsync();
        for (var i = 0; i < quantidade; i++)
        {
            var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
            await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: seguidor.Token);
            await ambiente.PostAsync($"/api/v1/eventos/{evento}/cancelar", new { motivo = "Sem local disponível" }, organizador.Token);
            ambiente.Relogio.Avancar(TimeSpan.FromSeconds(1));
        }

        return seguidor;
    }

    [FactBanco]
    public async Task Listar_notificacoes_traz_as_mais_novas_primeiro_e_o_total_de_nao_lidas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var seguidor = await SeguidorComNotificacoesAsync(ambiente);

        var resposta = await ambiente.GetAsync("/api/v1/notificacoes", seguidor.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(3, corpo.GetProperty("total").GetInt32());
        Assert.Equal(3, corpo.GetProperty("naoLidas").GetInt32());
        var itens = corpo.GetProperty("itens");
        Assert.Equal(3, itens.GetArrayLength());
        Assert.Equal("CANCELAMENTO", itens[0].GetProperty("tipo").GetString());
        Assert.False(itens[0].GetProperty("lida").GetBoolean());
        var datas = itens.EnumerateArray().Select(n => n.GetProperty("criadoEm").GetDateTime()).ToList();
        Assert.Equal(datas.OrderByDescending(d => d), datas);
    }

    [FactBanco]
    public async Task Marcar_uma_como_lida_atualiza_o_filtro_e_o_contador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var seguidor = await SeguidorComNotificacoesAsync(ambiente);
        var lista = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes", seguidor.Token));
        var id = lista.GetProperty("itens")[0].GetProperty("id").GetGuid();

        var marcar = await ambiente.PatchAsync($"/api/v1/notificacoes/{id}/lida", seguidor.Token);
        var lidas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes?lidas=true", seguidor.Token));
        var naoLidas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes?lidas=false", seguidor.Token));

        Assert.Equal(HttpStatusCode.NoContent, marcar.StatusCode);
        Assert.Equal(1, lidas.GetProperty("total").GetInt32());
        Assert.Equal(2, naoLidas.GetProperty("total").GetInt32());
        Assert.Equal(2, naoLidas.GetProperty("naoLidas").GetInt32());
        Assert.Equal(2, lidas.GetProperty("naoLidas").GetInt32());
    }

    [FactBanco]
    public async Task Notificacao_de_outro_usuario_retorna_404_e_nao_e_alterada()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var seguidor = await SeguidorComNotificacoesAsync(ambiente, 1);
        var intruso = await ambiente.NovaSessaoAsync();
        var id = (await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes", seguidor.Token)))
            .GetProperty("itens")[0].GetProperty("id").GetGuid();

        var resposta = await ambiente.PatchAsync($"/api/v1/notificacoes/{id}/lida", intruso.Token);
        var inexistente = await ambiente.PatchAsync($"/api/v1/notificacoes/{Guid.NewGuid()}/lida", seguidor.Token);
        var depois = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes", seguidor.Token));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("NOTIFICACAO_NAO_ENCONTRADA", await AmbienteApi.CodigoErroAsync(resposta));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(1, depois.GetProperty("naoLidas").GetInt32());
    }

    [FactBanco]
    public async Task Marcar_todas_como_lidas_nao_mexe_nas_de_outros_usuarios()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var a = await ambiente.NovaSessaoAsync();
        var b = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        foreach (var seguidor in new[] { a, b })
        {
            await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: seguidor.Token);
        }

        await ambiente.PostAsync($"/api/v1/eventos/{evento}/cancelar", new { motivo = "Sem local disponível" }, organizador.Token);

        var resposta = await ambiente.PatchAsync("/api/v1/notificacoes/lidas", a.Token);
        var deA = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes", a.Token));
        var deB = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes", b.Token));

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(0, deA.GetProperty("naoLidas").GetInt32());
        Assert.Equal(1, deB.GetProperty("naoLidas").GetInt32());
    }

    [FactBanco]
    public async Task Paginacao_e_validada_e_funciona()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var seguidor = await SeguidorComNotificacoesAsync(ambiente);

        var pagina2 = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/notificacoes?pagina=2&tamanhoPagina=2", seguidor.Token));
        var invalida = await ambiente.GetAsync("/api/v1/notificacoes?tamanhoPagina=51", seguidor.Token);

        Assert.Equal(1, pagina2.GetProperty("itens").GetArrayLength());
        Assert.Equal(3, pagina2.GetProperty("total").GetInt32());
        Assert.Equal(2, pagina2.GetProperty("pagina").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
    }
}
