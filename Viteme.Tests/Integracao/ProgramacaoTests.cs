using System.Net;
using Microsoft.EntityFrameworkCore;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E18–E20: programação do evento. Regras RN05, RN07 e RN13.</summary>
public class ProgramacaoTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private const string Motivo = "Ajuste na programação do evento";

    private static object Atividade(DateTime inicio, TimeSpan inicioEm, TimeSpan duracao, string titulo = "Palestra de abertura", string? motivo = null) => new
    {
        titulo,
        palestrante = "Profa. Ana",
        local = "Sala 3",
        inicio = inicio + inicioEm,
        fim = inicio + inicioEm + duracao,
        motivoAlteracao = motivo,
    };

    private static async Task<DateTime> InicioAsync(AmbienteApi ambiente, Guid id, Sessao organizador) =>
        (await ambiente.DetalheAsync(id, organizador.Token)).Corpo.GetProperty("dataInicio").GetDateTime();

    [FactBanco]
    public async Task Adicionar_atividade_em_rascunho_nao_exige_motivo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        var inicio = await InicioAsync(ambiente, id, organizador);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1)), organizador.Token);
        var detalhe = (await ambiente.DetalheAsync(id, organizador.Token)).Corpo;

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.StartsWith($"/api/v1/eventos/{id}/atividades/", resposta.Headers.Location!.OriginalString);
        Assert.Equal(1, detalhe.GetProperty("atividades").GetArrayLength());
        Assert.Equal(0, detalhe.GetProperty("ultimasAlteracoes").GetArrayLength());
    }

    [FactBanco]
    public async Task Atividade_fora_do_periodo_do_evento_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        var inicio = await InicioAsync(ambiente, id, organizador);

        var antes = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(-1), TimeSpan.FromHours(2)), organizador.Token);
        var depois = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(7), TimeSpan.FromHours(2)), organizador.Token);

        Assert.Equal((HttpStatusCode)422, antes.StatusCode);
        Assert.Equal("ATIVIDADE_FORA_DO_PERIODO", await AmbienteApi.CodigoErroAsync(antes));
        Assert.Equal((HttpStatusCode)422, depois.StatusCode);
    }

    [FactBanco]
    public async Task Atividade_valida_titulo_e_ordem_das_datas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        var inicio = await InicioAsync(ambiente, id, organizador);

        var fimAntes = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            new { titulo = "Ok título", inicio = inicio.AddHours(2), fim = inicio.AddHours(1) }, organizador.Token);
        var tituloCurto = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            new { titulo = "ab", inicio = inicio.AddHours(1), fim = inicio.AddHours(2) }, organizador.Token);

        Assert.Equal(HttpStatusCode.BadRequest, fimAntes.StatusCode);
        Assert.True((await AmbienteApi.CorpoAsync(fimAntes)).GetProperty("errors").TryGetProperty("fim", out _));
        Assert.Equal(HttpStatusCode.BadRequest, tituloCurto.StatusCode);
    }

    [FactBanco]
    public async Task Em_evento_publicado_adicionar_atividade_exige_motivo_registra_e_notifica()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inscrito = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.InscreverNoBancoAsync(id, inscrito.UsuarioId);
        var inicio = await InicioAsync(ambiente, id, organizador);

        var semMotivo = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1)), organizador.Token);
        var comMotivo = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1), motivo: Motivo), organizador.Token);
        var historico = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{id}/alteracoes"));

        Assert.Equal((HttpStatusCode)422, semMotivo.StatusCode);
        Assert.Equal("MOTIVO_OBRIGATORIO", await AmbienteApi.CodigoErroAsync(semMotivo));
        Assert.Equal(HttpStatusCode.Created, comMotivo.StatusCode);
        Assert.Equal("programacao", historico[0].GetProperty("campo").GetString());
        Assert.Equal(Motivo, historico[0].GetProperty("motivo").GetString());

        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Notificacoes.CountAsync(n => n.UsuarioId == inscrito.UsuarioId && n.EventoId == id && n.Tipo == TipoNotificacao.Alteracao));
    }

    [FactBanco]
    public async Task Editar_atividade_atualiza_os_dados_e_registra_valor_anterior_e_novo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var inicio = await InicioAsync(ambiente, id, organizador);
        var criada = await AmbienteApi.CorpoAsync(await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1), motivo: Motivo), organizador.Token));
        var atividadeId = criada.GetProperty("id").GetGuid();
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(5));

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}/atividades/{atividadeId}",
            Atividade(inicio, TimeSpan.FromHours(3), TimeSpan.FromHours(2), "Palestra remarcada", Motivo), organizador.Token);
        var historico = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{id}/alteracoes"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Palestra remarcada", (await AmbienteApi.CorpoAsync(resposta)).GetProperty("titulo").GetString());
        Assert.Equal(2, historico.GetArrayLength());
        Assert.Contains("Palestra de abertura", historico[0].GetProperty("valorAnterior").GetString());
        Assert.Contains("Palestra remarcada", historico[0].GetProperty("valorNovo").GetString());
    }

    [FactBanco]
    public async Task Remover_atividade_em_evento_publicado_exige_motivo_na_consulta()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var inicio = await InicioAsync(ambiente, id, organizador);
        var criada = await AmbienteApi.CorpoAsync(await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1), motivo: Motivo), organizador.Token));
        var atividadeId = criada.GetProperty("id").GetGuid();

        var semMotivo = await ambiente.DeleteAsync($"/api/v1/eventos/{id}/atividades/{atividadeId}", organizador.Token);
        var comMotivo = await ambiente.DeleteAsync(
            $"/api/v1/eventos/{id}/atividades/{atividadeId}?motivo={Uri.EscapeDataString("Atividade cancelada pelo palestrante")}", organizador.Token);
        var detalhe = (await ambiente.DetalheAsync(id)).Corpo;

        Assert.Equal((HttpStatusCode)422, semMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, comMotivo.StatusCode);
        Assert.Equal(0, detalhe.GetProperty("atividades").GetArrayLength());
    }

    [FactBanco]
    public async Task Remover_atividade_de_rascunho_nao_exige_motivo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        var inicio = await InicioAsync(ambiente, id, organizador);
        var criada = await AmbienteApi.CorpoAsync(await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1)), organizador.Token));

        var resposta = await ambiente.DeleteAsync($"/api/v1/eventos/{id}/atividades/{criada.GetProperty("id").GetGuid()}", organizador.Token);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Atividade_inexistente_ou_de_outro_evento_retorna_404()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador);
        var outro = await ambiente.CriarEventoAsync(organizador);
        var inicio = await InicioAsync(ambiente, outro, organizador);
        var doOutro = await AmbienteApi.CorpoAsync(await ambiente.PostAsync($"/api/v1/eventos/{outro}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1)), organizador.Token));
        var corpo = Atividade(await InicioAsync(ambiente, evento, organizador), TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        var inexistente = await ambiente.PutAsync($"/api/v1/eventos/{evento}/atividades/{Guid.NewGuid()}", corpo, organizador.Token);
        var cruzada = await ambiente.DeleteAsync($"/api/v1/eventos/{evento}/atividades/{doOutro.GetProperty("id").GetGuid()}", organizador.Token);

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("ATIVIDADE_NAO_ENCONTRADA", await AmbienteApi.CodigoErroAsync(inexistente));
        Assert.Equal(HttpStatusCode.NotFound, cruzada.StatusCode);
    }

    [FactBanco]
    public async Task Somente_o_organizador_altera_a_programacao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var participante = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{id}/colaboradores", new { email = emailColab }, organizador.Token);
        var corpo = Atividade(await InicioAsync(ambiente, id, organizador), TimeSpan.FromHours(1), TimeSpan.FromHours(1), motivo: Motivo);

        var porColaborador = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades", corpo, colaborador.Token);
        var porParticipante = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades", corpo, participante.Token);
        var anonimo = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades", corpo);

        Assert.Equal(HttpStatusCode.Forbidden, porColaborador.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, porParticipante.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [FactBanco]
    public async Task Evento_cancelado_nao_aceita_mudancas_na_programacao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var inicio = await InicioAsync(ambiente, id, organizador);
        await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = "Cancelado por falta de público" }, organizador.Token);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            Atividade(inicio, TimeSpan.FromHours(1), TimeSpan.FromHours(1), motivo: Motivo), organizador.Token);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("EVENTO_FINALIZADO", await AmbienteApi.CodigoErroAsync(resposta));
    }
}
