using System.Net;
using Microsoft.EntityFrameworkCore;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E01–E04: cadastro, login, renovação e logout. Casos CT01–CT07 do plano de testes.</summary>
public class AuthTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    [FactBanco]
    public async Task CT01_cadastro_valido_cria_conta_e_guarda_senha_como_hash()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = AmbienteApi.EmailNovo();

        var resposta = await ambiente.PostAsync("/api/v1/auth/cadastro",
            new { nome = "Maria Souza", email, senha = "Senha123", telefone = "+5544999990000" });
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(email, corpo.GetProperty("email").GetString());
        Assert.False(corpo.TryGetProperty("senhaHash", out _));

        await using var contexto = banco.CriarContexto();
        var usuario = await contexto.Usuarios.SingleAsync(u => u.Email == email);
        Assert.NotEqual("Senha123", usuario.SenhaHash);
        Assert.DoesNotContain("Senha123", usuario.SenhaHash);
        Assert.False(usuario.TelefoneVerificado);
    }

    [FactBanco]
    public async Task CT02_email_ja_cadastrado_com_maiusculas_diferentes_retorna_409()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();

        var resposta = await ambiente.PostAsync("/api/v1/auth/cadastro",
            new { nome = "Outra Pessoa", email = email.ToUpperInvariant(), senha = "Senha123" });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("EMAIL_JA_CADASTRADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT03_senha_fraca_retorna_400_com_erro_no_campo_senha()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var resposta = await ambiente.PostAsync("/api/v1/auth/cadastro",
            new { nome = "Maria Souza", email = AmbienteApi.EmailNovo(), senha = "abcdefgh" });
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.True(corpo.GetProperty("errors").TryGetProperty("senha", out _));
    }

    [FactBanco]
    public async Task Cadastro_valida_nome_email_e_telefone()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var resposta = await ambiente.PostAsync("/api/v1/auth/cadastro",
            new { nome = "Ma", email = "nao-e-email", senha = "Senha123", telefone = "44999990000" });
        var erros = (await AmbienteApi.CorpoAsync(resposta)).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True(erros.TryGetProperty("nome", out _));
        Assert.True(erros.TryGetProperty("email", out _));
        Assert.True(erros.TryGetProperty("telefone", out _));
    }

    [FactBanco]
    public async Task CT04_login_valido_retorna_token_e_cookie_httponly_de_renovacao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();

        var resposta = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = AmbienteApi.Senha });
        var corpo = await AmbienteApi.CorpoAsync(resposta);
        var cookie = resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("refresh_token=")).ToLowerInvariant();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("tokenAcesso").GetString()));
        Assert.Equal(email, corpo.GetProperty("usuario").GetProperty("email").GetString());
        Assert.False(corpo.TryGetProperty("refreshToken", out _));
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/v1/auth", cookie);
    }

    [FactBanco]
    public async Task Token_de_acesso_permite_chamar_endpoint_protegido()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var resposta = await ambiente.GetAsync("/api/v1/usuarios/me", sessao.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Endpoint_protegido_sem_token_ou_com_token_invalido_retorna_401_problem_details()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var semToken = await ambiente.GetAsync("/api/v1/usuarios/me");
        var invalido = await ambiente.GetAsync("/api/v1/usuarios/me", "token.invalido.aqui");

        foreach (var resposta in new[] { semToken, invalido })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
            Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
            Assert.Equal("NAO_AUTENTICADO", await AmbienteApi.CodigoErroAsync(resposta));
        }
    }

    [FactBanco]
    public async Task CT05_senha_incorreta_retorna_401_com_a_mesma_mensagem_de_email_inexistente()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();

        var senhaErrada = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = "Errada123" });
        var emailInexistente = await ambiente.PostAsync("/api/v1/auth/login", new { email = AmbienteApi.EmailNovo(), senha = "Errada123" });
        var a = await AmbienteApi.CorpoAsync(senhaErrada);
        var b = await AmbienteApi.CorpoAsync(emailInexistente);

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, emailInexistente.StatusCode);
        Assert.Equal("CREDENCIAIS_INVALIDAS", a.GetProperty("codigo").GetString());
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
    }

    [FactBanco]
    public async Task CT06_cinco_logins_invalidos_bloqueiam_a_conta_por_15_minutos()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();

        for (var i = 0; i < 5; i++)
        {
            var falha = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = "Errada123" });
            Assert.Equal(HttpStatusCode.Unauthorized, falha.StatusCode);
        }

        var bloqueada = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = AmbienteApi.Senha });
        Assert.Equal(HttpStatusCode.Locked, bloqueada.StatusCode);
        Assert.Equal("CONTA_BLOQUEADA", await AmbienteApi.CodigoErroAsync(bloqueada));

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(14));
        var aindaBloqueada = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = AmbienteApi.Senha });
        Assert.Equal(HttpStatusCode.Locked, aindaBloqueada.StatusCode);

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));
        var liberada = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = AmbienteApi.Senha });
        Assert.Equal(HttpStatusCode.OK, liberada.StatusCode);
    }

    [FactBanco]
    public async Task Login_com_sucesso_zera_a_contagem_de_falhas()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var email = await ambiente.CadastrarAsync();

        for (var i = 0; i < 4; i++)
        {
            await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = "Errada123" });
        }

        await ambiente.LoginAsync(email);
        var outraFalha = await ambiente.PostAsync("/api/v1/auth/login", new { email, senha = "Errada123" });

        Assert.Equal(HttpStatusCode.Unauthorized, outraFalha.StatusCode);
    }

    [FactBanco]
    public async Task Rate_limiting_limita_logins_por_minuto_por_ip()
    {
        using var ambiente = new AmbienteApi(banco, api, limitacao: true);
        var corpo = new { email = AmbienteApi.EmailNovo(), senha = "Errada123" };

        for (var i = 0; i < 5; i++)
        {
            var permitida = await ambiente.PostAsync("/api/v1/auth/login", corpo);
            Assert.Equal(HttpStatusCode.Unauthorized, permitida.StatusCode);
        }

        var limitada = await ambiente.PostAsync("/api/v1/auth/login", corpo);

        Assert.Equal((HttpStatusCode)429, limitada.StatusCode);
        Assert.Equal("LIMITE_EXCEDIDO", await AmbienteApi.CodigoErroAsync(limitada));
    }

    [FactBanco]
    public async Task Renovar_emite_novo_token_e_gira_o_refresh_token()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var resposta = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: sessao.Refresh);
        var novoRefresh = AmbienteApi.ExtrairRefresh(resposta);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("tokenAcesso").GetString()));
        Assert.NotNull(novoRefresh);
        Assert.NotEqual(sessao.Refresh, novoRefresh);
    }

    [FactBanco]
    public async Task CT07_reutilizar_refresh_token_retorna_401_e_revoga_todos_os_tokens_do_usuario()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var primeira = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: sessao.Refresh);
        var refreshNovo = AmbienteApi.ExtrairRefresh(primeira)!;
        var reuso = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: sessao.Refresh);
        var comTokenNovo = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: refreshNovo);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        Assert.Equal("TOKEN_INVALIDO", await AmbienteApi.CodigoErroAsync(reuso));
        Assert.Equal(HttpStatusCode.Unauthorized, comTokenNovo.StatusCode);
    }

    [FactBanco]
    public async Task Renovar_sem_cookie_ou_com_token_desconhecido_retorna_401()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var semCookie = await ambiente.PostAsync("/api/v1/auth/renovar");
        var desconhecido = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: "token-que-nao-existe");

        Assert.Equal(HttpStatusCode.Unauthorized, semCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, desconhecido.StatusCode);
    }

    [FactBanco]
    public async Task Refresh_token_expira_em_7_dias()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        ambiente.Relogio.Avancar(TimeSpan.FromDays(7));
        var resposta = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: sessao.Refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [FactBanco]
    public async Task Logout_revoga_o_refresh_token_remove_o_cookie_e_e_idempotente()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var primeira = await ambiente.PostAsync("/api/v1/auth/logout", refresh: sessao.Refresh);
        var segunda = await ambiente.PostAsync("/api/v1/auth/logout", refresh: sessao.Refresh);
        var semCookie = await ambiente.PostAsync("/api/v1/auth/logout");
        var renovar = await ambiente.PostAsync("/api/v1/auth/renovar", refresh: sessao.Refresh);

        Assert.Equal(HttpStatusCode.NoContent, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, segunda.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, semCookie.StatusCode);
        Assert.Contains(primeira.Headers.GetValues("Set-Cookie"), c => c.StartsWith("refresh_token=;"));
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
    }

    [FactBanco]
    public async Task Refresh_token_e_guardado_apenas_como_hash()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        await using var contexto = banco.CriarContexto();
        var hashes = await contexto.TokensRenovacao.Select(t => t.TokenHash).ToListAsync();

        Assert.DoesNotContain(sessao.Refresh, hashes);
    }
}
