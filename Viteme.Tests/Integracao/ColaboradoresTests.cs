using System.Net;
using Microsoft.EntityFrameworkCore;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E21–E23: colaboradores do evento. Casos CT47 e CT48 e regras RN04, RN07 e RN08.</summary>
public class ColaboradoresTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private static string Rota(Guid eventoId) => $"/api/v1/eventos/{eventoId}/colaboradores";

    [FactBanco]
    public async Task CT47_adicionar_colaborador_notifica_o_usuario_e_ele_passa_a_ver_a_lista()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(email);
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var resposta = await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);
        var pelaOrganizador = await AmbienteApi.CorpoAsync(await ambiente.GetAsync(Rota(id), organizador.Token));
        var peloColaborador = await ambiente.GetAsync(Rota(id), colaborador.Token);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(colaborador.UsuarioId, corpo.GetProperty("usuarioId").GetGuid());
        Assert.Equal(email, corpo.GetProperty("email").GetString());
        Assert.Equal(1, pelaOrganizador.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, peloColaborador.StatusCode);

        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Notificacoes.CountAsync(n =>
            n.UsuarioId == colaborador.UsuarioId && n.EventoId == id && n.Tipo == TipoNotificacao.Colaboracao));
    }

    [FactBanco]
    public async Task Email_do_colaborador_nao_diferencia_maiusculas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var id = await ambiente.CriarEventoAsync(organizador);

        var resposta = await ambiente.PostAsync(Rota(id), new { email = email.ToUpperInvariant() }, organizador.Token);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [FactBanco]
    public async Task CT48_organizador_nao_pode_ser_colaborador_do_proprio_evento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        var organizador = await ambiente.LoginAsync(email);
        var id = await ambiente.CriarEventoAsync(organizador);

        var resposta = await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("ORGANIZADOR_NAO_PODE_SER_COLABORADOR", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Colaborador_repetido_retorna_409_e_email_desconhecido_retorna_404()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);

        var repetido = await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);
        var desconhecido = await ambiente.PostAsync(Rota(id), new { email = AmbienteApi.EmailNovo() }, organizador.Token);
        var invalido = await ambiente.PostAsync(Rota(id), new { email = "nao-e-email" }, organizador.Token);

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("JA_COLABORADOR", await AmbienteApi.CodigoErroAsync(repetido));
        Assert.Equal(HttpStatusCode.NotFound, desconhecido.StatusCode);
        Assert.Equal("USUARIO_NAO_ENCONTRADO", await AmbienteApi.CodigoErroAsync(desconhecido));
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    [FactBanco]
    public async Task Usuario_com_inscricao_ativa_nao_pode_ser_colaborador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var inscrito = await ambiente.LoginAsync(email);
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.InscreverNoBancoAsync(id, inscrito.UsuarioId);

        var resposta = await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("USUARIO_JA_INSCRITO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Usuario_com_inscricao_cancelada_pode_ser_colaborador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var usuario = await ambiente.LoginAsync(email);
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.InscreverNoBancoAsync(id, usuario.UsuarioId);
        await using (var contexto = banco.CriarContexto())
        {
            var inscricao = await contexto.Inscricoes.SingleAsync(i => i.EventoId == id && i.UsuarioId == usuario.UsuarioId);
            inscricao.Cancelar();
            await contexto.SaveChangesAsync();
        }

        var resposta = await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Remover_colaborador_retorna_204_e_depois_404()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(email);
        var id = await ambiente.CriarEventoAsync(organizador);
        await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);

        var removido = await ambiente.DeleteAsync($"{Rota(id)}/{colaborador.UsuarioId}", organizador.Token);
        var lista = await AmbienteApi.CorpoAsync(await ambiente.GetAsync(Rota(id), organizador.Token));
        var denovo = await ambiente.DeleteAsync($"{Rota(id)}/{colaborador.UsuarioId}", organizador.Token);
        var aposRemocao = await ambiente.GetAsync(Rota(id), colaborador.Token);

        Assert.Equal(HttpStatusCode.NoContent, removido.StatusCode);
        Assert.Equal(0, lista.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, denovo.StatusCode);
        Assert.Equal("COLABORADOR_NAO_ENCONTRADO", await AmbienteApi.CodigoErroAsync(denovo));
        Assert.Equal(HttpStatusCode.NotFound, aposRemocao.StatusCode);
    }

    [FactBanco]
    public async Task Somente_o_organizador_gerencia_e_participante_comum_nao_ve_a_lista()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var emailColab = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(emailColab);
        var participante = await ambiente.NovaSessaoAsync();
        var alvo = await ambiente.CadastrarAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync(Rota(id), new { email = emailColab }, organizador.Token);

        var colabAdiciona = await ambiente.PostAsync(Rota(id), new { email = alvo }, colaborador.Token);
        var colabRemove = await ambiente.DeleteAsync($"{Rota(id)}/{colaborador.UsuarioId}", colaborador.Token);
        var participanteLista = await ambiente.GetAsync(Rota(id), participante.Token);
        var anonimoLista = await ambiente.GetAsync(Rota(id));
        var inexistente = await ambiente.GetAsync(Rota(Guid.NewGuid()), organizador.Token);

        Assert.Equal(HttpStatusCode.Forbidden, colabAdiciona.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, colabRemove.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, participanteLista.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimoLista.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [FactBanco]
    public async Task Colaborador_ve_rascunho_do_evento_em_que_colabora()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(email);
        var id = await ambiente.CriarEventoAsync(organizador);
        var antes = await ambiente.GetAsync($"/api/v1/eventos/{id}", colaborador.Token);

        await ambiente.PostAsync(Rota(id), new { email }, organizador.Token);
        var depois = await ambiente.DetalheAsync(id, colaborador.Token);

        Assert.Equal(HttpStatusCode.NotFound, antes.StatusCode);
        Assert.True(depois.Corpo.GetProperty("souColaborador").GetBoolean());
        Assert.False(depois.Corpo.GetProperty("souOrganizador").GetBoolean());
    }
}
