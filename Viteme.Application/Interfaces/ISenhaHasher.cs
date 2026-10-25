namespace Viteme.Application.Interfaces;

/// <summary>Hash adaptativo de senhas (PBKDF2 do <c>PasswordHasher</c> do ASP.NET Core).</summary>
public interface ISenhaHasher
{
    string Gerar(string senha);

    bool Verificar(string hash, string senha);

    /// <summary>Gasta o mesmo tempo de uma verificação real, para o login não revelar se o e-mail existe.</summary>
    void SimularVerificacao(string senha);
}
