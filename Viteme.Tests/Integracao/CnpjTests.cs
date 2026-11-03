using System.Net;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E40 e RN24: CNPJ do organizador, exigido para criar e publicar eventos. Casos CT64–CT70.</summary>
public class CnpjTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private const string Rota = "/api/v1/usuarios/me/cnpj";

    [FactBanco]
    public async Task CT64_cnpj_ativo_com_mascara_e_salvo_sem_mascara_e_libera_organizar()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();
        var cnpj = Dados.NovoCnpj();
        var mascarado = $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

        var antes = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/usuarios/me", sessao.Token));
        var resposta = await ambiente.PutAsync(Rota, new { cnpj = mascarado }, sessao.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.False(antes.GetProperty("podeOrganizar").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(cnpj, corpo.GetProperty("cnpj").GetString());
        Assert.Equal("Centro Acadêmico de Teste", corpo.GetProperty("razaoSocial").GetString());
        Assert.True(corpo.GetProperty("podeOrganizar").GetBoolean());
        Assert.Equal(cnpj, ambiente.Cnpj.Consultados.Single());
    }

    [FactBanco]
    public async Task CT65_cnpj_com_digito_verificador_errado_retorna_400_sem_consultar()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var resposta = await ambiente.PutAsync(Rota, new { cnpj = "11.222.333/0001-82" }, sessao.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True(corpo.GetProperty("errors").TryGetProperty("cnpj", out _));
        Assert.Empty(ambiente.Cnpj.Consultados);
    }

    [FactBanco]
    public async Task CT66_cnpj_de_outra_conta_retorna_409()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var primeira = await ambiente.NovaSessaoAsync();
        var segunda = await ambiente.NovaSessaoAsync();
        var cnpj = await ambiente.HabilitarOrganizadorAsync(primeira);

        var resposta = await ambiente.PutAsync(Rota, new { cnpj }, segunda.Token);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CNPJ_JA_CADASTRADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT67_cnpj_desconhecido_ou_inativo_retorna_422_e_nao_libera()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();
        var desconhecido = Dados.NovoCnpj();
        var baixado = Dados.NovoCnpj();
        ambiente.Cnpj.Respostas[desconhecido] = null;
        ambiente.Cnpj.Respostas[baixado] = new DadosCnpj("Empresa Antiga", false, "BAIXADA");

        var r1 = await ambiente.PutAsync(Rota, new { cnpj = desconhecido }, sessao.Token);
        var r2 = await ambiente.PutAsync(Rota, new { cnpj = baixado }, sessao.Token);
        var perfil = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/usuarios/me", sessao.Token));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);
        Assert.Equal("CNPJ_NAO_ENCONTRADO", await AmbienteApi.CodigoErroAsync(r1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);
        Assert.Equal("CNPJ_INATIVO", await AmbienteApi.CodigoErroAsync(r2));
        Assert.False(perfil.GetProperty("podeOrganizar").GetBoolean());
    }

    [FactBanco]
    public async Task CT68_consulta_indisponivel_retorna_502()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();
        ambiente.Cnpj.Falhar = true;

        var resposta = await ambiente.PutAsync(Rota, new { cnpj = Dados.NovoCnpj() }, sessao.Token);

        Assert.Equal(HttpStatusCode.BadGateway, resposta.StatusCode);
        Assert.Equal("CONSULTA_CNPJ_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT69_criar_evento_sem_cnpj_retorna_403()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var resposta = await ambiente.PostAsync("/api/v1/eventos", ambiente.CorpoEvento(), sessao.Token);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("CNPJ_NAO_VERIFICADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT70_rascunho_antigo_de_organizador_sem_cnpj_nao_pode_ser_publicado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();
        Evento evento;
        await using (var contexto = banco.CriarContexto(ambiente.Relogio))
        {
            evento = Dados.NovoEvento(sessao.UsuarioId, inicio: ambiente.Relogio.UtcAgora.AddDays(10));
            contexto.Add(evento);
            await contexto.SaveChangesAsync();
        }

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{evento.Id}/publicar", token: sessao.Token);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("CNPJ_NAO_VERIFICADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Reenviar_o_mesmo_cnpj_nao_consulta_de_novo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();
        var cnpj = await ambiente.HabilitarOrganizadorAsync(sessao);

        var resposta = await ambiente.PutAsync(Rota, new { cnpj }, sessao.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Single(ambiente.Cnpj.Consultados);
    }
}
