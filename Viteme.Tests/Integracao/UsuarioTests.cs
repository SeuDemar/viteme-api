using System.Net;
using Microsoft.EntityFrameworkCore;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E05–E09: perfil, verificação de telefone e consentimento de SMS. Casos CT08–CT13.</summary>
public class UsuarioTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private const string Telefone = "+5544999990000";
    private const string OutroTelefone = "+5544988880000";

    private static object Codigo(string codigo) => new { codigo };

    private static async Task<bool> BoolAsync(HttpResponseMessage resposta, string campo) =>
        (await AmbienteApi.CorpoAsync(resposta)).GetProperty(campo).GetBoolean();

    [FactBanco]
    public async Task Perfil_retorna_os_dados_do_usuario_autenticado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);

        var resposta = await ambiente.GetAsync("/api/v1/usuarios/me", sessao.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(Telefone, corpo.GetProperty("telefone").GetString());
        Assert.False(corpo.GetProperty("telefoneVerificado").GetBoolean());
        Assert.False(corpo.GetProperty("aceitaSms").GetBoolean());
        Assert.False(corpo.TryGetProperty("senhaHash", out _));
    }

    [FactBanco]
    public async Task Atualizar_perfil_altera_o_nome_e_valida_os_campos()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var ok = await ambiente.PutAsync("/api/v1/usuarios/me", new { nome = "Maria Silva", telefone = (string?)null }, sessao.Token);
        var invalido = await ambiente.PutAsync("/api/v1/usuarios/me", new { nome = "Ma", telefone = "123" }, sessao.Token);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Maria Silva", (await AmbienteApi.CorpoAsync(ok)).GetProperty("nome").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    [FactBanco]
    public async Task CT08_envio_de_codigo_retorna_202_e_envia_sms_com_6_digitos_guardado_como_hash()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);

        var resposta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        var codigo = ambiente.Sms.UltimoCodigo();

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.Matches(@"^\d{6}$", codigo);
        Assert.Equal(Telefone, ambiente.Sms.Enviados.Last().Telefone);
        Assert.True((await AmbienteApi.CorpoAsync(resposta)).TryGetProperty("expiraEm", out _));

        await using var contexto = banco.CriarContexto();
        Assert.DoesNotContain(codigo, await contexto.CodigosVerificacao.Select(c => c.CodigoHash).ToListAsync());
    }

    [FactBanco]
    public async Task Codigo_sem_telefone_cadastrado_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var resposta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("TELEFONE_NAO_INFORMADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT09_novo_codigo_antes_de_60_segundos_retorna_429()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);

        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(30));
        var cedo = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(31));
        var depois = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);

        Assert.Equal((HttpStatusCode)429, cedo.StatusCode);
        Assert.Equal("AGUARDE_NOVO_CODIGO", await AmbienteApi.CodigoErroAsync(cedo));
        Assert.Equal(HttpStatusCode.Accepted, depois.StatusCode);
    }

    [FactBanco]
    public async Task Novo_codigo_invalida_o_anterior()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        var antigo = ambiente.Sms.UltimoCodigo();
        ambiente.Relogio.Avancar(TimeSpan.FromSeconds(61));
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        var novo = ambiente.Sms.UltimoCodigo();

        var comAntigo = antigo == novo
            ? null
            : await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(antigo), sessao.Token);
        var comNovo = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(novo), sessao.Token);

        if (comAntigo is not null)
        {
            Assert.Equal("CODIGO_INVALIDO", await AmbienteApi.CodigoErroAsync(comAntigo));
        }

        Assert.Equal(HttpStatusCode.OK, comNovo.StatusCode);
    }

    [FactBanco]
    public async Task Verificar_com_codigo_correto_confirma_o_telefone_e_impede_novo_envio()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);

        var resposta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(ambiente.Sms.UltimoCodigo()), sessao.Token);
        var denovo = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(await BoolAsync(resposta, "telefoneVerificado"));
        Assert.Equal("TELEFONE_JA_VERIFICADO", await AmbienteApi.CodigoErroAsync(denovo));
    }

    [FactBanco]
    public async Task Verificar_exige_exatamente_6_digitos_numericos()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);

        var resposta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo("12345"), sessao.Token);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [FactBanco]
    public async Task CT10_codigo_correto_apos_10_minutos_retorna_codigo_expirado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        var codigo = ambiente.Sms.UltimoCodigo();

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(10));
        var resposta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(codigo), sessao.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("CODIGO_EXPIRADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT11_sexta_tentativa_retorna_tentativas_excedidas_e_invalida_o_codigo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        var certo = ambiente.Sms.UltimoCodigo();
        var errado = certo == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            var tentativa = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(errado), sessao.Token);
            Assert.Equal("CODIGO_INVALIDO", await AmbienteApi.CodigoErroAsync(tentativa));
        }

        var sexta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(errado), sessao.Token);
        var comCodigoCorreto = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(certo), sessao.Token);

        Assert.Equal((HttpStatusCode)422, sexta.StatusCode);
        Assert.Equal("TENTATIVAS_EXCEDIDAS", await AmbienteApi.CodigoErroAsync(sexta));
        Assert.Equal((HttpStatusCode)422, comCodigoCorreto.StatusCode);
    }

    [FactBanco]
    public async Task CT12_aceitar_sms_sem_telefone_verificado_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);

        var resposta = await ambiente.PutAsync("/api/v1/usuarios/me/preferencias-sms", new { aceitaSms = true }, sessao.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("TELEFONE_NAO_VERIFICADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Preferencias_sms_sao_obrigatorias_e_o_aceite_pode_ser_revogado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(ambiente.Sms.UltimoCodigo()), sessao.Token);

        var semCorpo = await ambiente.PutAsync("/api/v1/usuarios/me/preferencias-sms", new { }, sessao.Token);
        var ativar = await ambiente.PutAsync("/api/v1/usuarios/me/preferencias-sms", new { aceitaSms = true }, sessao.Token);
        var revogar = await ambiente.PutAsync("/api/v1/usuarios/me/preferencias-sms", new { aceitaSms = false }, sessao.Token);

        Assert.Equal(HttpStatusCode.BadRequest, semCorpo.StatusCode);
        Assert.True(await BoolAsync(ativar, "aceitaSms"));
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, (await AmbienteApi.CorpoAsync(ativar)).GetProperty("aceiteSmsEm").ValueKind);
        Assert.False(await BoolAsync(revogar, "aceitaSms"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await AmbienteApi.CorpoAsync(revogar)).GetProperty("aceiteSmsEm").ValueKind);
    }

    [FactBanco]
    public async Task CT13_trocar_o_telefone_zera_verificacao_e_aceite_de_sms()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(ambiente.Sms.UltimoCodigo()), sessao.Token);
        await ambiente.PutAsync("/api/v1/usuarios/me/preferencias-sms", new { aceitaSms = true }, sessao.Token);

        var resposta = await ambiente.PutAsync("/api/v1/usuarios/me", new { nome = "Maria Souza", telefone = OutroTelefone }, sessao.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False(await BoolAsync(resposta, "telefoneVerificado"));
        Assert.False(await BoolAsync(resposta, "aceitaSms"));
    }

    [FactBanco]
    public async Task Trocar_o_telefone_invalida_codigos_pendentes_do_telefone_antigo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);
        var codigoAntigo = ambiente.Sms.UltimoCodigo();

        await ambiente.PutAsync("/api/v1/usuarios/me", new { nome = "Maria Souza", telefone = OutroTelefone }, sessao.Token);
        var resposta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo(codigoAntigo), sessao.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("CODIGO_INVALIDO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Falha_no_provedor_de_sms_retorna_502()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync(Telefone);
        ambiente.Sms.Falhar = true;

        var resposta = await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: sessao.Token);

        Assert.Equal(HttpStatusCode.BadGateway, resposta.StatusCode);
        Assert.Equal("SMS_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Endpoints_de_conta_exigem_autenticacao()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var respostas = new[]
        {
            await ambiente.GetAsync("/api/v1/usuarios/me"),
            await ambiente.PutAsync("/api/v1/usuarios/me", new { nome = "Maria Souza" }),
            await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo"),
            await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", Codigo("123456")),
            await ambiente.PutAsync("/api/v1/usuarios/me/preferencias-sms", new { aceitaSms = true }),
        };

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }
}
