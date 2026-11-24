using System.Net;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E43/E44: pedido e conclusão da redefinição de senha por e-mail.</summary>
public class RedefinicaoSenhaTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private const string NovaSenha = "NovaSenha456";

    [FactBanco]
    public async Task Pedido_de_conta_existente_envia_o_link_e_responde_202()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();

        var resposta = await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        var enviado = Assert.Single(ambiente.Email.Enviados);
        Assert.Equal(email, enviado.Email);
    }

    [FactBanco]
    public async Task Pedido_de_conta_inexistente_responde_202_igual_e_nao_envia_nada()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var resposta = await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email = AmbienteApi.EmailNovo() });

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.Empty(ambiente.Email.Enviados);
    }

    [FactBanco]
    public async Task Falha_no_envio_do_email_nao_aparece_para_quem_pediu()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        ambiente.Email.Falhar = true;

        var resposta = await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Email_em_formato_invalido_retorna_400()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var resposta = await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email = "isso-nao-e-email" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Pedidos_seguidos_enviam_so_um_email_dentro_do_intervalo_minimo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();

        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        Assert.Single(ambiente.Email.Enviados);

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(2));
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        Assert.Equal(2, ambiente.Email.Enviados.Count);
    }

    [FactBanco]
    public async Task Redefinir_troca_a_senha_e_o_login_passa_a_usar_a_nova()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });

        var resposta = await ambiente.PostAsync("/api/v1/auth/redefinir-senha",
            new { token = ambiente.Email.UltimoToken(), novaSenha = NovaSenha });

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        var antiga = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = AmbienteApi.Senha });
        Assert.Equal(HttpStatusCode.Unauthorized, antiga.StatusCode);
        var nova = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = NovaSenha });
        Assert.Equal(HttpStatusCode.OK, nova.StatusCode);
    }

    [FactBanco]
    public async Task O_link_vale_so_uma_vez()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        var token = ambiente.Email.UltimoToken();

        (await ambiente.PostAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = NovaSenha })).EnsureSuccessStatusCode();
        var segunda = await ambiente.PostAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = "OutraSenha789" });

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
        Assert.Equal("LINK_REDEFINICAO_INVALIDO", await AmbienteApi.CodigoErroAsync(segunda));
    }

    [FactBanco]
    public async Task Link_expirado_e_rejeitado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(61));
        var resposta = await ambiente.PostAsync("/api/v1/auth/redefinir-senha",
            new { token = ambiente.Email.UltimoToken(), novaSenha = NovaSenha });

        Assert.Equal("LINK_REDEFINICAO_INVALIDO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Token_desconhecido_e_rejeitado()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var resposta = await ambiente.PostAsync("/api/v1/auth/redefinir-senha", new { token = "token-que-nao-existe", novaSenha = NovaSenha });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("LINK_REDEFINICAO_INVALIDO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Senha_fraca_retorna_400_e_nao_gasta_o_link()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        var token = ambiente.Email.UltimoToken();

        var fraca = await ambiente.PostAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = "abcdefgh" });
        Assert.Equal(HttpStatusCode.BadRequest, fraca.StatusCode);

        var valida = await ambiente.PostAsync("/api/v1/auth/redefinir-senha", new { token, novaSenha = NovaSenha });
        Assert.Equal(HttpStatusCode.NoContent, valida.StatusCode);
    }

    [FactBanco]
    public async Task Um_novo_pedido_invalida_o_link_anterior()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        var primeiro = ambiente.Email.UltimoToken();

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(2));
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        var segundo = ambiente.Email.UltimoToken();

        var antigo = await ambiente.PostAsync("/api/v1/auth/redefinir-senha", new { token = primeiro, novaSenha = NovaSenha });
        Assert.Equal(HttpStatusCode.BadRequest, antigo.StatusCode);
        var novo = await ambiente.PostAsync("/api/v1/auth/redefinir-senha", new { token = segundo, novaSenha = NovaSenha });
        Assert.Equal(HttpStatusCode.NoContent, novo.StatusCode);
    }

    [FactBanco]
    public async Task Redefinir_revoga_as_sessoes_abertas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();
        var email = sessao.Corpo.GetProperty("usuario").GetProperty("email").GetString()!;
        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });

        (await ambiente.PostAsync("/api/v1/auth/redefinir-senha",
            new { token = ambiente.Email.UltimoToken(), novaSenha = NovaSenha })).EnsureSuccessStatusCode();

        var renovar = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: sessao.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
    }

    [FactBanco]
    public async Task Redefinir_encerra_o_bloqueio_por_tentativas_de_login()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();
        for (var i = 0; i < 5; i++)
        {
            await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = "ErradaErrada1" });
        }

        Assert.Equal(HttpStatusCode.Locked,
            (await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = AmbienteApi.Senha })).StatusCode);

        await ambiente.PostAsync("/api/v1/auth/esqueci-senha", new { email });
        (await ambiente.PostAsync("/api/v1/auth/redefinir-senha",
            new { token = ambiente.Email.UltimoToken(), novaSenha = NovaSenha })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK,
            (await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = NovaSenha })).StatusCode);
    }
}
