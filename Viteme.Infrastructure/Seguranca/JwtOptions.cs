using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Viteme.Infrastructure.Seguranca;

/// <summary>Configuração do JWT (seção <c>Jwt</c>). A chave vem de variável de ambiente ou user-secrets, nunca do repositório.</summary>
public class JwtOptions
{
    public const string Secao = "Jwt";

    /// <summary>HS256 exige chave com pelo menos 256 bits.</summary>
    public const int TamanhoMinimoChave = 32;

    public string Chave { get; set; } = string.Empty;

    public string Emissor { get; set; } = "viteme-api";

    public string Audiencia { get; set; } = "viteme-web";

    public int MinutosAcesso { get; set; } = 15;

    public SymmetricSecurityKey ChaveDeAssinatura()
    {
        var bytes = Encoding.UTF8.GetBytes(Chave ?? string.Empty);
        if (bytes.Length < TamanhoMinimoChave)
        {
            throw new InvalidOperationException(
                $"'Jwt:Chave' deve ter pelo menos {TamanhoMinimoChave} caracteres. Defina por user-secrets ou pela variável Jwt__Chave.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}
