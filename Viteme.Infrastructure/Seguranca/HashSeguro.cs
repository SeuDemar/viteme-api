using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Seguranca;

/// <summary>HMAC-SHA256 com a chave secreta da aplicação. Um vazamento do banco sozinho não permite reverter códigos de 6 dígitos.</summary>
public class HashSeguro(IOptions<JwtOptions> opcoes) : IHashSeguro
{
    private readonly byte[] _chave = SHA256.HashData(Encoding.UTF8.GetBytes("hash-seguro:" + ObterChave(opcoes.Value)));

    public string Calcular(string valor) =>
        Convert.ToHexString(HMACSHA256.HashData(_chave, Encoding.UTF8.GetBytes(valor)));

    public bool Confere(string valor, string hashEsperado) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Calcular(valor)),
            Encoding.ASCII.GetBytes(hashEsperado));

    private static string ObterChave(JwtOptions jwt)
    {
        jwt.ChaveDeAssinatura();
        return jwt.Chave;
    }
}
