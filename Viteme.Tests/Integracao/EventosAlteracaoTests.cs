using System.Net;
using Microsoft.EntityFrameworkCore;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;

namespace Viteme.Tests.Integracao;

/// <summary>E13, E15 e E17: alteração, histórico, notificação e cancelamento de eventos. Casos CT39–CT41 e RN13/RN14.</summary>
public class EventosAlteracaoTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private const string Motivo = "O auditório principal está em reforma";

    private async Task<int> NotificacoesAsync(Guid usuarioId, Guid eventoId, TipoNotificacao tipo)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Notificacoes.CountAsync(n => n.UsuarioId == usuarioId && n.EventoId == eventoId && n.Tipo == tipo);
    }

    [FactBanco]
    public async Task CT39_alterar_o_local_registra_a_alteracao_e_notifica_inscritos_e_interessados()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inscrito = await ambiente.NovaSessaoAsync();
        var interessado = await ambiente.NovaSessaoAsync();
        var alheio = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Alteração {marca}", local: "Auditório"), publicar: true);
        await ambiente.InscreverNoBancoAsync(id, inscrito.UsuarioId);
        await ambiente.InteresseNoBancoAsync(id, interessado.UsuarioId);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("local", "Sala 12")) .Append(new("motivoAlteracao", Motivo)).ToDictionary(), organizador.Token, etag);
        var historico = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{id}/alteracoes"));
        var mural = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos?busca={marca}"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Sala 12", (await AmbienteApi.CorpoAsync(resposta)).GetProperty("local").GetString());

        Assert.Equal(1, historico.GetArrayLength());
        Assert.Equal("local", historico[0].GetProperty("campo").GetString());
        Assert.Equal("Auditório", historico[0].GetProperty("valorAnterior").GetString());
        Assert.Equal("Sala 12", historico[0].GetProperty("valorNovo").GetString());
        Assert.Equal(Motivo, historico[0].GetProperty("motivo").GetString());

        Assert.Equal(1, await NotificacoesAsync(inscrito.UsuarioId, id, TipoNotificacao.Alteracao));
        Assert.Equal(1, await NotificacoesAsync(interessado.UsuarioId, id, TipoNotificacao.Alteracao));
        Assert.Equal(0, await NotificacoesAsync(alheio.UsuarioId, id, TipoNotificacao.Alteracao));
        Assert.Equal(0, await NotificacoesAsync(organizador.UsuarioId, id, TipoNotificacao.Alteracao));

        Assert.True(mural.GetProperty("itens")[0].GetProperty("alteradoRecentemente").GetBoolean());
    }

    [FactBanco]
    public async Task Alteracao_so_e_destacada_no_mural_por_72_horas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Selo {marca}"), publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);
        await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("local", "Nova sala")).Append(new("motivoAlteracao", Motivo)).ToDictionary(), organizador.Token, etag);

        ambiente.Relogio.Avancar(TimeSpan.FromHours(71));
        var dentro = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos?busca={marca}"));
        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));
        var fora = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos?busca={marca}"));

        Assert.True(dentro.GetProperty("itens")[0].GetProperty("alteradoRecentemente").GetBoolean());
        Assert.False(fora.GetProperty("itens")[0].GetProperty("alteradoRecentemente").GetBoolean());
    }

    [FactBanco]
    public async Task CT40_alterar_a_data_sem_motivo_retorna_422_e_com_motivo_curto_retorna_400()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);
        var novaData = detalhe.GetProperty("dataInicio").GetDateTime().AddDays(1);
        var novoFim = detalhe.GetProperty("dataFim").GetDateTime().AddDays(1);

        var semMotivo = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("dataInicio", novaData), ("dataFim", novoFim)), organizador.Token, etag);
        var motivoCurto = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("dataInicio", novaData), ("dataFim", novoFim))
                .Append(new("motivoAlteracao", "curto")).ToDictionary(), organizador.Token, etag);

        Assert.Equal((HttpStatusCode)422, semMotivo.StatusCode);
        Assert.Equal("MOTIVO_OBRIGATORIO", await AmbienteApi.CodigoErroAsync(semMotivo));
        Assert.Equal(HttpStatusCode.BadRequest, motivoCurto.StatusCode);
    }

    [FactBanco]
    public async Task Alterar_campos_que_nao_exigem_motivo_nao_gera_historico()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("descricao", "Descrição nova"), ("categoria", "workshop")), organizador.Token, etag);
        var historico = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{id}/alteracoes"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0, historico.GetArrayLength());
    }

    [FactBanco]
    public async Task Editar_rascunho_nao_exige_motivo_nem_gera_historico()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("titulo", "Título totalmente novo"), ("local", "Outro lugar")), organizador.Token, etag);
        var historico = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{id}/alteracoes", organizador.Token));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0, historico.GetArrayLength());
    }

    [FactBanco]
    public async Task CT41_alterar_o_preco_com_inscricao_pendente_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m), publicar: true);
        await ambiente.InscreverNoBancoAsync(id, participante.UsuarioId, StatusInscricao.PendentePagamento);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("preco", 80m)), organizador.Token, etag);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("PRECO_BLOQUEADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Preco_pode_mudar_quando_nao_ha_inscricoes_ativas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m), publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("preco", 80m)), organizador.Token, etag);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(80m, (await AmbienteApi.CorpoAsync(resposta)).GetProperty("preco").GetDecimal());
    }

    [FactBanco]
    public async Task Capacidade_nao_pode_ficar_abaixo_das_vagas_ocupadas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(capacidade: 5), publicar: true);
        for (var i = 0; i < 3; i++)
        {
            await ambiente.InscreverNoBancoAsync(id, (await ambiente.NovaSessaoAsync()).UsuarioId);
        }

        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);
        var abaixo = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("capacidade", 2)), organizador.Token, etag);
        var igual = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("capacidade", 3)), organizador.Token, etag);

        Assert.Equal((HttpStatusCode)422, abaixo.StatusCode);
        Assert.Equal("CAPACIDADE_ABAIXO_DAS_VAGAS_OCUPADAS", await AmbienteApi.CodigoErroAsync(abaixo));
        Assert.Equal(HttpStatusCode.OK, igual.StatusCode);
        Assert.Equal(0, (await AmbienteApi.CorpoAsync(igual)).GetProperty("vagasRestantes").GetInt32());
    }

    [FactBanco]
    public async Task Evento_cancelado_nao_pode_ser_editado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);
        await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = Motivo }, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("descricao", "Tentativa")), organizador.Token, etag);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("EVENTO_FINALIZADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Mudar_o_periodo_deixando_atividade_fora_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        var (detalhe, _) = await ambiente.DetalheAsync(id, organizador.Token);
        var inicio = detalhe.GetProperty("dataInicio").GetDateTime();
        await ambiente.PostAsync($"/api/v1/eventos/{id}/atividades",
            new { titulo = "Abertura", inicio = inicio.AddHours(1), fim = inicio.AddHours(2) }, organizador.Token);
        var (atual, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(atual, ("dataInicio", inicio.AddHours(5)), ("dataFim", inicio.AddHours(9))), organizador.Token, etag);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("ATIVIDADE_FORA_DO_PERIODO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Cancelar_evento_cancela_inscricoes_e_notifica_os_envolvidos()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inscrito = await ambiente.NovaSessaoAsync();
        var interessado = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.InscreverNoBancoAsync(id, inscrito.UsuarioId);
        await ambiente.InteresseNoBancoAsync(id, interessado.UsuarioId);
        await ambiente.PostAsync($"/api/v1/eventos/{id}/colaboradores", new { email = emailColab }, organizador.Token);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = Motivo }, organizador.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("CANCELADO", (await AmbienteApi.CorpoAsync(resposta)).GetProperty("status").GetString());

        await using var contexto = banco.CriarContexto();
        Assert.All(await contexto.Inscricoes.Where(i => i.EventoId == id).ToListAsync(), i => Assert.Equal(StatusInscricao.Cancelada, i.Status));
        Assert.Equal(1, await NotificacoesAsync(inscrito.UsuarioId, id, TipoNotificacao.Cancelamento));
        Assert.Equal(1, await NotificacoesAsync(interessado.UsuarioId, id, TipoNotificacao.Cancelamento));
        Assert.Equal(1, await NotificacoesAsync(colaborador.UsuarioId, id, TipoNotificacao.Cancelamento));
        Assert.Equal(0, await NotificacoesAsync(organizador.UsuarioId, id, TipoNotificacao.Cancelamento));
    }

    [FactBanco]
    public async Task Cancelar_valida_motivo_papel_e_estado_do_evento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var estranho = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var motivoCurto = await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = "curto" }, organizador.Token);
        var semPapel = await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = Motivo }, estranho.Token);
        var primeira = await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = Motivo }, organizador.Token);
        var segunda = await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = Motivo }, organizador.Token);

        Assert.Equal(HttpStatusCode.BadRequest, motivoCurto.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, semPapel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("EVENTO_JA_CANCELADO", await AmbienteApi.CodigoErroAsync(segunda));
    }

    [FactBanco]
    public async Task Evento_encerrado_nao_pode_ser_cancelado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        ambiente.Relogio.Avancar(TimeSpan.FromDays(11));
        using (var escopo = ambiente.Servicos.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<EncerramentoEventosService>().EncerrarVencidosAsync();
        }

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{id}/cancelar", new { motivo = Motivo }, organizador.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("EVENTO_ENCERRADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Historico_lista_da_alteracao_mais_recente_para_a_mais_antiga()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);
        var primeira = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("local", "Sala A")).Append(new("motivoAlteracao", "Primeira troca de sala")).ToDictionary(), organizador.Token, etag);
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(5));
        var segunda = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("local", "Sala B")).Append(new("motivoAlteracao", "Segunda troca de sala")).ToDictionary(), organizador.Token, primeira.Headers.ETag!.ToString());

        var historico = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos/{id}/alteracoes"));
        var detalheFinal = (await ambiente.DetalheAsync(id)).Corpo;

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal(2, historico.GetArrayLength());
        Assert.Equal("Sala B", historico[0].GetProperty("valorNovo").GetString());
        Assert.Equal("Sala A", historico[1].GetProperty("valorNovo").GetString());
        Assert.Equal(2, detalheFinal.GetProperty("ultimasAlteracoes").GetArrayLength());
    }
}
