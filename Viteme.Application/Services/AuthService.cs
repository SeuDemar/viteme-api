using Viteme.Application.DTOs.Auth;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Cadastro, login, renovação e logout (E01–E04). Regras RN01–RN03.</summary>
public class AuthService(
    IUsuarioRepository usuarios,
    ITokenRenovacaoRepository tokens,
    ISenhaHasher hasher,
    ITokenService tokenService,
    IHashSeguro hashSeguro,
    IGeradorAleatorio aleatorio,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
{
    private const int BytesRefreshToken = 64;

    public async Task<CadastroResponse> CadastrarAsync(CadastroRequest requisicao, CancellationToken cancellationToken = default)
    {
        var email = requisicao.Email.Trim();
        if (await usuarios.EmailExisteAsync(email, cancellationToken))
        {
            throw EmailJaCadastrado();
        }

        var telefone = string.IsNullOrWhiteSpace(requisicao.Telefone) ? null : requisicao.Telefone.Trim();
        var usuario = new Usuario(requisicao.Nome.Trim(), email, hasher.Gerar(requisicao.Senha), telefone);
        usuarios.Adicionar(usuario);

        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ViolacaoDeUnicidadeException)
        {
            // Outra requisição cadastrou o mesmo e-mail entre a consulta e a gravação.
            throw EmailJaCadastrado();
        }

        return new CadastroResponse(usuario.Id, usuario.Nome, usuario.Email);
    }

    public async Task<LoginResultado> LoginAsync(LoginRequest requisicao, CancellationToken cancellationToken = default)
    {
        var agora = relogio.UtcAgora;
        var usuario = await usuarios.ObterPorEmailAsync(requisicao.Email.Trim(), cancellationToken);

        if (usuario is null)
        {
            hasher.SimularVerificacao(requisicao.Senha);
            throw CredenciaisInvalidas();
        }

        // RN03: conta bloqueada responde 423 mesmo com a senha correta.
        if (usuario.EstaBloqueado(agora))
        {
            throw new RegraDeNegocioException("CONTA_BLOQUEADA", "Conta bloqueada",
                "Conta temporariamente bloqueada por excesso de tentativas. Tente novamente em alguns minutos.", 423);
        }

        if (!hasher.Verificar(usuario.SenhaHash, requisicao.Senha))
        {
            usuario.RegistrarFalhaLogin(agora);
            await unitOfWork.SalvarAsync(cancellationToken);
            throw CredenciaisInvalidas();
        }

        usuario.RegistrarLoginComSucesso();
        var resultado = await EmitirTokensAsync(usuario, agora, cancellationToken);
        return new LoginResultado(resultado.TokenAcesso, resultado.ExpiraEm, Resumir(usuario), resultado.RefreshToken, resultado.RefreshExpiraEm);
    }

    /// <summary>Renova o acesso com rotação do refresh token. O reuso de um token já revogado revoga todos os do usuário.</summary>
    public async Task<RenovarResultado> RenovarAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw TokenInvalido();
        }

        var agora = relogio.UtcAgora;
        var atual = await tokens.ObterPorHashAsync(hashSeguro.Calcular(refreshToken), cancellationToken);
        if (atual is null)
        {
            throw TokenInvalido();
        }

        if (atual.Revogado)
        {
            await tokens.RevogarTodosDoUsuarioAsync(atual.UsuarioId, agora, cancellationToken);
            await unitOfWork.SalvarAsync(cancellationToken);
            throw TokenInvalido();
        }

        if (!atual.EstaAtivo(agora))
        {
            throw TokenInvalido();
        }

        var usuario = await usuarios.ObterPorIdAsync(atual.UsuarioId, cancellationToken) ?? throw TokenInvalido();

        // Duas renovações simultâneas com o mesmo token: só a primeira a revogar emite o novo par.
        if (!await tokens.TentarRevogarAsync(atual.Id, agora, cancellationToken))
        {
            throw TokenInvalido();
        }

        return await EmitirTokensAsync(usuario, agora, cancellationToken);
    }

    /// <summary>Idempotente: revoga o token do cookie, se existir, e nunca falha.</summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var token = await tokens.ObterPorHashAsync(hashSeguro.Calcular(refreshToken), cancellationToken);
        if (token is null || token.Revogado)
        {
            return;
        }

        token.Revogar(relogio.UtcAgora);
        await unitOfWork.SalvarAsync(cancellationToken);
    }

    private async Task<RenovarResultado> EmitirTokensAsync(Usuario usuario, DateTime agora, CancellationToken cancellationToken)
    {
        var acesso = tokenService.GerarTokenAcesso(usuario, agora);

        var valorRefresh = aleatorio.GerarTokenOpaco(BytesRefreshToken);
        var refresh = new TokenRenovacao(usuario.Id, hashSeguro.Calcular(valorRefresh), agora);
        tokens.Adicionar(refresh);

        await unitOfWork.SalvarAsync(cancellationToken);
        return new RenovarResultado(acesso.Token, acesso.ExpiraEm, valorRefresh, refresh.ExpiraEm);
    }

    internal static UsuarioResumo Resumir(Usuario u) => new(u.Id, u.Nome, u.Email, u.TelefoneVerificado, u.AceitaSms);

    private static RegraDeNegocioException EmailJaCadastrado() =>
        new("EMAIL_JA_CADASTRADO", "E-mail já cadastrado", "Já existe uma conta com este e-mail.", 409);

    // Mesma mensagem para e-mail inexistente e senha incorreta.
    private static RegraDeNegocioException CredenciaisInvalidas() =>
        new("CREDENCIAIS_INVALIDAS", "Credenciais inválidas", "E-mail ou senha incorretos.", 401);

    private static RegraDeNegocioException TokenInvalido() =>
        new("TOKEN_INVALIDO", "Token inválido", "Sessão inválida ou expirada. Faça login novamente.", 401);
}
