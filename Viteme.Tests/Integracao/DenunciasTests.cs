using System.Net;
using Microsoft.EntityFrameworkCore;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E37: denúncias e a proteção automática da plataforma. Casos CT52–CT55 e regra RN19.</summary>
public class DenunciasTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private static object Corpo(string motivo = "GOLPE", string? descricao = null) => new { motivo, descricao };

    private static async Task<Sessao[]> NovosAsync(AmbienteApi ambiente, int quantidade)
    {
        var lista = new List<Sessao>();
        for (var i = 0; i < quantidade; i++)
        {
            lista.Add(await ambiente.NovaSessaoAsync());
        }

        return [.. lista];
    }

    private static async Task<HttpStatusCode> DenunciarTodosAsync(AmbienteApi ambiente, Guid evento, IEnumerable<Sessao> denunciantes)
    {
        var ultimo = HttpStatusCode.Created;
        foreach (var d in denunciantes)
        {
            ultimo = (await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), d.Token)).StatusCode;
        }

        return ultimo;
    }

    private async Task<StatusEvento> StatusAsync(Guid evento)
    {
        await using var contexto = banco.CriarContexto();
        return (await contexto.Eventos.AsNoTracking().SingleAsync(e => e.Id == evento)).Status;
    }

    [FactBanco]
    public async Task CT52_denuncia_valida_retorna_201_e_fica_registrada()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var denunciante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo("SPAM"), denunciante.Token);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        await using var contexto = banco.CriarContexto();
        var denuncia = await contexto.Denuncias.SingleAsync(d => d.EventoId == evento);
        Assert.Equal(MotivoDenuncia.Spam, denuncia.Motivo);
        Assert.Equal(denunciante.UsuarioId, denuncia.UsuarioId);
        Assert.Equal(StatusEvento.Publicado, await StatusAsync(evento));
    }

    [FactBanco]
    public async Task CT53_denuncia_repetida_retorna_409_e_nao_conta_duas_vezes()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var denunciante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), denunciante.Token);

        var repetida = await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), denunciante.Token);

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("DENUNCIA_EXISTENTE", await AmbienteApi.CodigoErroAsync(repetida));
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Denuncias.CountAsync(d => d.EventoId == evento));
    }

    [FactBanco]
    public async Task Organizador_e_colaborador_nao_denunciam_o_proprio_evento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/colaboradores", new { email = emailColab }, organizador.Token);

        var doOrganizador = await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), organizador.Token);
        var doColaborador = await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), colaborador.Token);

        Assert.Equal((HttpStatusCode)422, doOrganizador.StatusCode);
        Assert.Equal("NAO_PODE_DENUNCIAR_PROPRIO_EVENTO", await AmbienteApi.CodigoErroAsync(doOrganizador));
        Assert.Equal("NAO_PODE_DENUNCIAR_PROPRIO_EVENTO", await AmbienteApi.CodigoErroAsync(doColaborador));
    }

    [FactBanco]
    public async Task Motivo_outro_exige_descricao_e_motivo_invalido_retorna_400()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var denunciante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var rota = $"/api/v1/eventos/{evento}/denuncias";

        var semDescricao = await ambiente.PostAsync(rota, Corpo("OUTRO"), denunciante.Token);
        var semMotivo = await ambiente.PostAsync(rota, new { }, denunciante.Token);
        var motivoInvalido = await ambiente.PostAsync(rota, Corpo("NAO_EXISTE"), denunciante.Token);
        var longa = await ambiente.PostAsync(rota, Corpo("OUTRO", new string('x', 501)), denunciante.Token);
        var valida = await ambiente.PostAsync(rota, Corpo("OUTRO", "Local informado não existe"), denunciante.Token);

        Assert.Equal(HttpStatusCode.BadRequest, semDescricao.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, motivoInvalido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longa.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valida.StatusCode);
    }

    [FactBanco]
    public async Task Evento_inexistente_rascunho_e_sem_login()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var denunciante = await ambiente.NovaSessaoAsync();
        var rascunho = await ambiente.CriarEventoAsync(organizador);

        var inexistente = await ambiente.PostAsync($"/api/v1/eventos/{Guid.NewGuid()}/denuncias", Corpo(), denunciante.Token);
        var doRascunho = await ambiente.PostAsync($"/api/v1/eventos/{rascunho}/denuncias", Corpo(), denunciante.Token);
        var anonimo = await ambiente.PostAsync($"/api/v1/eventos/{rascunho}/denuncias", Corpo());

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, doRascunho.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [FactBanco]
    public async Task Evento_cancelado_nao_recebe_denuncia()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var denunciante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/cancelar", new { motivo = "Sem local disponível" }, organizador.Token);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), denunciante.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("EVENTO_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT54_quinta_denuncia_oculta_o_evento_e_avisa_inscritos_e_organizador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inscrito = await ambiente.NovaSessaoAsync();
        var denunciantes = await NovosAsync(ambiente, 5);
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.InscreverComSucessoAsync(inscrito, evento);

        await DenunciarTodosAsync(ambiente, evento, denunciantes.Take(4));
        Assert.Equal(StatusEvento.Publicado, await StatusAsync(evento));
        await DenunciarTodosAsync(ambiente, evento, denunciantes.Skip(4));

        Assert.Equal(StatusEvento.Oculto, await StatusAsync(evento));
        var mural = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos?busca={Uri.EscapeDataString("Evento")}"));
        Assert.DoesNotContain(mural.GetProperty("itens").EnumerateArray(), e => e.GetProperty("id").GetGuid() == evento);
        Assert.Equal(HttpStatusCode.NotFound, (await ambiente.GetAsync($"/api/v1/eventos/{evento}", denunciantes[0].Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ambiente.GetAsync($"/api/v1/eventos/{evento}", inscrito.Token)).StatusCode);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Notificacoes.CountAsync(n => n.EventoId == evento && n.UsuarioId == inscrito.UsuarioId && n.Tipo == TipoNotificacao.Denuncia));
        Assert.Equal(1, await contexto.Notificacoes.CountAsync(n => n.EventoId == evento && n.UsuarioId == organizador.UsuarioId && n.Tipo == TipoNotificacao.Denuncia));
    }

    [FactBanco]
    public async Task Denuncias_simultaneas_ocultam_o_evento_uma_unica_vez()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var denunciantes = await NovosAsync(ambiente, 8);
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var respostas = await Task.WhenAll(denunciantes.Select(d =>
            ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), d.Token)));

        // Depois da quinta o evento sai do mural e quem não está inscrito passa a receber 404.
        Assert.All(respostas, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.NotFound }));
        Assert.True(respostas.Count(r => r.StatusCode == HttpStatusCode.Created) >= 5);
        Assert.Equal(StatusEvento.Oculto, await StatusAsync(evento));
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Notificacoes.CountAsync(n => n.EventoId == evento && n.UsuarioId == organizador.UsuarioId && n.Tipo == TipoNotificacao.Denuncia));
    }

    [FactBanco]
    public async Task CT55_republicado_e_denunciado_de_novo_e_cancelado_automaticamente_com_estorno()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var pagante = await ambiente.NovaSessaoAsync();
        var primeiros = await NovosAsync(ambiente, 5);
        var segundos = await NovosAsync(ambiente, 5);
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 30, capacidade: 5), publicar: true);
        var pagamento = await ambiente.InscreverEmEventoPagoAsync(pagante, evento);
        await ambiente.WebhookDeAsync(pagamento, "APROVADO");

        await DenunciarTodosAsync(ambiente, evento, primeiros);
        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(1)); // o relógio é fixo: separa as denúncias antigas da republicação
        var republicar = await ambiente.PostAsync($"/api/v1/eventos/{evento}/publicar", token: organizador.Token);
        var depoisDeRepublicar = await StatusAsync(evento);
        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(1));
        await DenunciarTodosAsync(ambiente, evento, segundos.Take(4));
        var aindaPublicado = await StatusAsync(evento);
        await DenunciarTodosAsync(ambiente, evento, segundos.Skip(4));

        Assert.Equal(HttpStatusCode.OK, republicar.StatusCode);
        Assert.Equal(StatusEvento.Publicado, depoisDeRepublicar);
        Assert.Equal(StatusEvento.Publicado, aindaPublicado); // só contam as denúncias depois da republicação
        Assert.Equal(StatusEvento.Cancelado, await StatusAsync(evento));
        Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(pagante, pagamento.InscricaoId));
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Notificacoes.CountAsync(n => n.EventoId == evento && n.UsuarioId == pagante.UsuarioId && n.Tipo == TipoNotificacao.Cancelamento));
    }

    [FactBanco]
    public async Task Limite_de_denuncias_por_minuto_retorna_429()
    {
        using var ambiente = new AmbienteApi(banco, api, limitacao: true);
        var organizador = await ambiente.NovaSessaoAsync();
        var denunciante = await ambiente.NovaSessaoAsync();
        var codigos = new List<HttpStatusCode>();

        for (var i = 0; i < 6; i++)
        {
            var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
            codigos.Add((await ambiente.PostAsync($"/api/v1/eventos/{evento}/denuncias", Corpo(), denunciante.Token)).StatusCode);
        }

        Assert.Equal(5, codigos.Count(c => c == HttpStatusCode.Created));
        Assert.Equal(HttpStatusCode.TooManyRequests, codigos[5]);
    }
}
