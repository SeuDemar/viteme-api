using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E24–E27: inscrição, cancelamento e listagens. Casos CT23–CT30.</summary>
public class InscricoesTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private static async Task<string?> CodigoAsync(HttpResponseMessage r) => await AmbienteApi.CodigoErroAsync(r);

    private static async Task<int?> VagasRestantesAsync(AmbienteApi ambiente, Guid eventoId) =>
        (await ambiente.DetalheAsync(eventoId)).Corpo.GetProperty("vagasRestantes").GetInt32();

    [FactBanco]
    public async Task CT23_inscricao_gratuita_e_confirmada_com_codigo_de_checkin()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(capacidade: 10), publicar: true);

        var resposta = await ambiente.InscreverAsync(participante, evento);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal("CONFIRMADA", corpo.GetProperty("status").GetString());
        Assert.Matches("^[A-Z0-9]{12}$", corpo.GetProperty("codigoCheckin").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, corpo.GetProperty("pagamento").ValueKind);
        Assert.Equal(9, await VagasRestantesAsync(ambiente, evento));

        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Notificacoes.CountAsync(n =>
            n.UsuarioId == participante.UsuarioId && n.EventoId == evento && n.Tipo == TipoNotificacao.Inscricao));
    }

    [FactBanco]
    public async Task Inscricao_exige_autenticacao_e_evento_existente()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var participante = await ambiente.NovaSessaoAsync();

        var anonima = await ambiente.PostAsync($"/api/v1/eventos/{Guid.NewGuid()}/inscricoes");
        var inexistente = await ambiente.InscreverAsync(participante, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, anonima.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [FactBanco]
    public async Task CT24_inscricao_duplicada_retorna_409()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.InscreverComSucessoAsync(participante, evento);

        var resposta = await ambiente.InscreverAsync(participante, evento);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("INSCRICAO_EXISTENTE", await CodigoAsync(resposta));
    }

    [FactBanco]
    public async Task CT25_evento_lotado_retorna_422_sem_vagas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(capacidade: 2), publicar: true);
        await ambiente.InscreverComSucessoAsync(await ambiente.NovaSessaoAsync(), evento);
        await ambiente.InscreverComSucessoAsync(await ambiente.NovaSessaoAsync(), evento);

        var resposta = await ambiente.InscreverAsync(await ambiente.NovaSessaoAsync(), evento);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("SEM_VAGAS", await CodigoAsync(resposta));
    }

    [FactBanco]
    public async Task CT26_disputa_pela_ultima_vaga_so_um_consegue()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(capacidade: 1), publicar: true);
        var disputantes = new List<Sessao>();
        for (var i = 0; i < 8; i++)
        {
            disputantes.Add(await ambiente.NovaSessaoAsync());
        }

        var respostas = await Task.WhenAll(disputantes.Select(d => ambiente.InscreverAsync(d, evento)));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
        var recusadas = respostas.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        Assert.Equal(7, recusadas.Count);
        foreach (var recusada in recusadas)
        {
            Assert.Equal((HttpStatusCode)422, recusada.StatusCode);
            Assert.Equal("SEM_VAGAS", await CodigoAsync(recusada));
        }

        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Inscricoes.CountAsync(i => i.EventoId == evento));
    }

    [FactBanco]
    public async Task Disputa_pela_ultima_vaga_em_evento_pago_so_um_consegue()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(capacidade: 2, preco: 20m), publicar: true);
        var disputantes = new List<Sessao>();
        for (var i = 0; i < 6; i++)
        {
            disputantes.Add(await ambiente.NovaSessaoAsync());
        }

        var respostas = await Task.WhenAll(disputantes.Select(d => ambiente.InscreverAsync(d, evento)));

        Assert.Equal(2, respostas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, respostas.Count(r => (int)r.StatusCode == 422));
        Assert.Equal(2, ambiente.Gateway.Cobrancas.Count);
    }

    [FactBanco]
    public async Task Mesmo_usuario_disparando_duas_inscricoes_ao_mesmo_tempo_cria_apenas_uma()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var respostas = await Task.WhenAll(ambiente.InscreverAsync(participante, evento), ambiente.InscreverAsync(participante, evento));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [FactBanco]
    public async Task CT27_inscricao_apos_o_fim_do_periodo_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);

        ambiente.Relogio.Avancar(TimeSpan.FromDays(10) - TimeSpan.FromMinutes(30));
        var resposta = await ambiente.InscreverAsync(participante, evento);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("INSCRICOES_ENCERRADAS", await CodigoAsync(resposta));
    }

    [FactBanco]
    public async Task CT28_organizador_e_colaborador_nao_se_inscrevem_no_proprio_evento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/colaboradores", new { email = emailColab }, organizador.Token);

        var doOrganizador = await ambiente.InscreverAsync(organizador, evento);
        var doColaborador = await ambiente.InscreverAsync(colaborador, evento);

        foreach (var resposta in new[] { doOrganizador, doColaborador })
        {
            Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
            Assert.Equal("PAPEL_NAO_PERMITE_INSCRICAO", await CodigoAsync(resposta));
        }
    }

    [FactBanco]
    public async Task Evento_que_nao_esta_publicado_nao_aceita_inscricao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var rascunho = await ambiente.CriarEventoAsync(organizador);
        var cancelado = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{cancelado}/cancelar", new { motivo = "Cancelado por falta de público" }, organizador.Token);

        var noRascunho = await ambiente.InscreverAsync(participante, rascunho);
        var noCancelado = await ambiente.InscreverAsync(participante, cancelado);

        Assert.Equal(HttpStatusCode.NotFound, noRascunho.StatusCode);
        Assert.Equal((HttpStatusCode)422, noCancelado.StatusCode);
        Assert.Equal("EVENTO_INDISPONIVEL", await CodigoAsync(noCancelado));
    }

    [FactBanco]
    public async Task CT29_cancelamento_dentro_do_prazo_libera_a_vaga()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(capacidade: 5), publicar: true);
        var inscricao = await ambiente.InscreverComSucessoAsync(participante, evento);
        Assert.Equal(4, await VagasRestantesAsync(ambiente, evento));

        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar", token: participante.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("CANCELADA", corpo.GetProperty("status").GetString());
        Assert.False(corpo.GetProperty("estornoSolicitado").GetBoolean());
        Assert.Equal(5, await VagasRestantesAsync(ambiente, evento));
    }

    [FactBanco]
    public async Task CT30_cancelamento_a_menos_de_24_horas_do_inicio_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromHours(30)), publicar: true);
        var inscricao = await ambiente.InscreverComSucessoAsync(participante, evento);

        ambiente.Relogio.Avancar(TimeSpan.FromHours(27)); // faltam 3 horas
        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar", token: participante.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("PRAZO_CANCELAMENTO_ENCERRADO", await CodigoAsync(resposta));
    }

    [FactBanco]
    public async Task Cancelamento_um_minuto_antes_do_limite_de_24_horas_ainda_e_permitido()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromHours(30)), publicar: true);
        var inscricao = await ambiente.InscreverComSucessoAsync(participante, evento);

        ambiente.Relogio.Avancar(TimeSpan.FromHours(5) + TimeSpan.FromMinutes(59)); // faltam 24 h e 1 min
        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar", token: participante.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Cancelar_valida_dono_autenticacao_e_cancelamento_repetido()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var outro = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var inscricao = await ambiente.InscreverComSucessoAsync(participante, evento);

        var anonimo = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar");
        var alheia = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar", token: outro.Token);
        var inexistente = await ambiente.PostAsync($"/api/v1/inscricoes/{Guid.NewGuid()}/cancelar", token: participante.Token);
        var primeira = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar", token: participante.Token);
        var segunda = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricao}/cancelar", token: participante.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("INSCRICAO_JA_CANCELADA", await CodigoAsync(segunda));
    }

    [FactBanco]
    public async Task Apos_cancelar_o_usuario_pode_se_inscrever_de_novo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var primeira = await ambiente.InscreverComSucessoAsync(participante, evento);
        await ambiente.PostAsync($"/api/v1/inscricoes/{primeira}/cancelar", token: participante.Token);

        var denovo = await ambiente.InscreverAsync(participante, evento);

        Assert.Equal(HttpStatusCode.Created, denovo.StatusCode);
    }

    [FactBanco]
    public async Task Minhas_inscricoes_separam_proximas_e_passadas_e_indicam_certificado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var participante = await ambiente.NovaSessaoAsync();
        var curto = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromHours(3), duracao: TimeSpan.FromHours(2)), publicar: true);
        var longe = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromDays(20)), publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{curto}/colaboradores", new { email = emailColab }, organizador.Token);
        await ambiente.InscreverComSucessoAsync(participante, curto);
        await ambiente.InscreverComSucessoAsync(participante, longe);

        var antes = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas?situacao=proximas", participante.Token));
        Assert.Equal(2, antes.GetArrayLength());
        Assert.Equal(curto, antes[0].GetProperty("evento").GetProperty("id").GetGuid());

        ambiente.Relogio.Avancar(TimeSpan.FromHours(1));
        var codigo = antes[0].GetProperty("codigoCheckin").GetString();
        var checkin = await ambiente.PostAsync($"/api/v1/eventos/{curto}/checkin", new { codigoCheckin = codigo }, colaborador.Token);
        ambiente.Relogio.Avancar(TimeSpan.FromHours(4));
        using (var escopo = ambiente.Servicos.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<EncerramentoEventosService>().EncerrarVencidosAsync();
        }

        var proximas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas?situacao=proximas", participante.Token));
        var passadas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas?situacao=passadas", participante.Token));
        var todas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas", participante.Token));

        Assert.Equal(HttpStatusCode.OK, checkin.StatusCode);
        Assert.Equal(1, proximas.GetArrayLength());
        Assert.Equal(longe, proximas[0].GetProperty("evento").GetProperty("id").GetGuid());
        Assert.False(proximas[0].GetProperty("certificadoDisponivel").GetBoolean());
        Assert.Equal(1, passadas.GetArrayLength());
        Assert.True(passadas[0].GetProperty("presencaConfirmada").GetBoolean());
        Assert.True(passadas[0].GetProperty("certificadoDisponivel").GetBoolean());
        Assert.Equal(2, todas.GetArrayLength());
    }

    [FactBanco]
    public async Task Minhas_inscricoes_so_mostra_as_do_proprio_usuario_e_exige_autenticacao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var a = await ambiente.NovaSessaoAsync();
        var b = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.InscreverComSucessoAsync(a, evento);

        var deB = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas", b.Token));
        var anonima = await ambiente.GetAsync("/api/v1/inscricoes/minhas");
        var invalida = await ambiente.GetAsync("/api/v1/inscricoes/minhas?situacao=ontem", a.Token);

        Assert.Equal(0, deB.GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, anonima.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
    }

    [FactBanco]
    public async Task Inscritos_do_evento_sao_listados_para_organizador_e_colaborador_sem_telefone()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var participante = await ambiente.NovaSessaoAsync("+5544999990000");
        var estranho = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/colaboradores", new { email = emailColab }, organizador.Token);
        await ambiente.InscreverComSucessoAsync(participante, evento);

        var peloOrganizador = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes", organizador.Token));
        var peloColaborador = await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes", colaborador.Token);
        var peloParticipante = await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes", participante.Token);
        var peloEstranho = await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes", estranho.Token);
        var anonimo = await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes");

        Assert.Equal(1, peloOrganizador.GetProperty("total").GetInt32());
        var item = peloOrganizador.GetProperty("itens")[0];
        Assert.Equal("CONFIRMADA", item.GetProperty("status").GetString());
        Assert.False(item.TryGetProperty("telefone", out _));
        Assert.DoesNotContain("5544999990000", item.GetRawText());
        Assert.Equal(HttpStatusCode.OK, peloColaborador.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, peloParticipante.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, peloEstranho.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [FactBanco]
    public async Task Inscritos_aceitam_filtros_de_status_busca_e_paginacao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 30m), publicar: true);
        var pagante = await ambiente.NovaSessaoAsync();
        var pendente = await ambiente.NovaSessaoAsync();
        await ambiente.InscreverEmEventoPagoAsync(pagante, evento);
        await ambiente.InscreverEmEventoPagoAsync(pendente, evento);
        var primeiro = await ambiente.InscreverEmEventoPagoAsync(await ambiente.NovaSessaoAsync(), evento);
        await ambiente.WebhookDeAsync(primeiro, "APROVADO");

        var confirmadas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes?status=CONFIRMADA", organizador.Token));
        var pendentes = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes?status=PENDENTE_PAGAMENTO", organizador.Token));
        var pagina = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes?tamanhoPagina=2&pagina=2", organizador.Token));
        var busca = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes?busca=maria", organizador.Token));
        var invalido = await ambiente.GetAsync($"/api/v1/eventos/{evento}/inscricoes?status=XYZ", organizador.Token);

        Assert.Equal(1, confirmadas.GetProperty("total").GetInt32());
        Assert.Equal(2, pendentes.GetProperty("total").GetInt32());
        Assert.Equal(3, pagina.GetProperty("total").GetInt32());
        Assert.Equal(1, pagina.GetProperty("itens").GetArrayLength());
        Assert.Equal(3, busca.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }
}
