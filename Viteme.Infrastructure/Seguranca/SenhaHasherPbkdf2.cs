using Microsoft.AspNetCore.Identity;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Seguranca;

/// <summary>PBKDF2 com sal aleatório e várias iterações, pelo <see cref="PasswordHasher{TUser}"/> do ASP.NET Core.</summary>
public class SenhaHasherPbkdf2 : ISenhaHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Contexto = new();
    private static readonly string HashFalso = Hasher.HashPassword(Contexto, Guid.NewGuid().ToString("N"));

    public string Gerar(string senha) => Hasher.HashPassword(Contexto, senha);

    public bool Verificar(string hash, string senha) =>
        Hasher.VerifyHashedPassword(Contexto, hash, senha) is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;

    public void SimularVerificacao(string senha) => Hasher.VerifyHashedPassword(Contexto, HashFalso, senha);
}
