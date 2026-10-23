using System.Net;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E28: check-in pelo código da inscrição. Casos CT49–CT51 e regra RN20.</summary>
public class CheckinTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private sealed record Cenario(AmbienteApi Ambiente, Sessao Organizador, Sessao Colaborador, Sessao Participante, Guid Evento, string Codigo);

    /// <summary>Evento que começa em 3 horas, com um colaborador e um participante confirmado.</summary>
    private async Task<Cenario> MontarAsync(AmbienteApi ambiente, decimal preco = 0)
    {
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(
            organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromHours(3), duracao: TimeSpan.FromHours(4), preco: preco), publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/colaboradores", new { email = emailColab }, organizador.Token);
        await ambiente.InscreverComSucessoAsync(participante, evento);

        var minhas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas", participante.Token));
        return new Cenario(ambiente, organizador, colaborador, participante, evento, minhas[0].GetProperty("codigoCheckin").GetString()!);
    }

    [FactBanco]
    public async Task CT49_checkin_valido_dentro_da_janela_registra_a_presenca()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);

        ambiente.Relogio.Avancar(TimeSpan.FromHours(1)); // 2 h antes do início
        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo.ToLowerInvariant() }, c.Colaborador.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);
        var inscritos = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{c.Evento}/inscricoes", c.Organizador.Token));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("nome").GetString()));
        Assert.True(inscritos.GetProperty("itens")[0].GetProperty("presencaConfirmada").GetBoolean());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, inscritos.GetProperty("itens")[0].GetProperty("checkinEm").ValueKind);
    }

    [FactBanco]
    public async Task Organizador_tambem_pode_fazer_checkin()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);

        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));
        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Organizador.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [FactBanco]
    public async Task CT50_mesmo_codigo_duas_vezes_retorna_409()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));
        await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Colaborador.Token);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Colaborador.Token);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CHECKIN_JA_REALIZADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Dois_checkins_simultaneos_do_mesmo_codigo_so_um_vale()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));

        var respostas = await Task.WhenAll(
            ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Colaborador.Token),
            ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Organizador.Token));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [FactBanco]
    public async Task CT51_participante_comum_nao_faz_checkin()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));

        var doParticipante = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Participante.Token);
        var anonimo = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo });

        Assert.Equal(HttpStatusCode.Forbidden, doParticipante.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [FactBanco]
    public async Task Checkin_fora_da_janela_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);

        var cedoDemais = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Colaborador.Token);
        ambiente.Relogio.Avancar(TimeSpan.FromHours(3) + TimeSpan.FromHours(4) + TimeSpan.FromMinutes(1)); // depois do fim
        var tardeDemais = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = c.Codigo }, c.Colaborador.Token);

        foreach (var resposta in new[] { cedoDemais, tardeDemais })
        {
            Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
            Assert.Equal("FORA_DO_HORARIO_DE_CHECKIN", await AmbienteApi.CodigoErroAsync(resposta));
        }
    }

    [FactBanco]
    public async Task Checkin_exige_inscricao_confirmada()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente, preco: 40m);
        var pendente = await ambiente.NovaSessaoAsync();
        await ambiente.InscreverEmEventoPagoAsync(pendente, c.Evento);
        var codigo = (await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas", pendente.Token)))[0]
            .GetProperty("codigoCheckin").GetString();

        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));
        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = codigo }, c.Colaborador.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("INSCRICAO_NAO_CONFIRMADA", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Codigo_inexistente_ou_de_outro_evento_retorna_404_e_formato_invalido_retorna_400()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await MontarAsync(ambiente);
        var outroEvento = await ambiente.CriarEventoAsync(c.Organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromHours(3)), publicar: true);
        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));

        var inexistente = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = "AAAAAAAAAAAA" }, c.Colaborador.Token);
        var deOutroEvento = await ambiente.PostAsync($"/api/v1/eventos/{outroEvento}/checkin", new { codigoCheckin = c.Codigo }, c.Organizador.Token);
        var formato = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/checkin", new { codigoCheckin = "curto" }, c.Colaborador.Token);

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("INSCRICAO_NAO_ENCONTRADA", await AmbienteApi.CodigoErroAsync(inexistente));
        Assert.Equal(HttpStatusCode.NotFound, deOutroEvento.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, formato.StatusCode);
    }

    [FactBanco]
    public async Task Inscricao_cancelada_nao_faz_checkin()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromHours(30)), publicar: true);
        var inscricao = await ambiente.InscreverComSucessoAsync(participante, evento);
        var codigo = (await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas", participante.Token)))[0]
            .GetProperty("codigoCheckin").GetString();
        var cancelada = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar", token: participante.Token);

        ambiente.Relogio.Avancar(TimeSpan.FromHours(28)); // 2 h antes do início
        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{evento}/checkin", new { codigoCheckin = codigo }, organizador.Token);

        Assert.Equal(HttpStatusCode.OK, cancelada.StatusCode);
        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("INSCRICAO_NAO_CONFIRMADA", await AmbienteApi.CodigoErroAsync(resposta));
    }
}
