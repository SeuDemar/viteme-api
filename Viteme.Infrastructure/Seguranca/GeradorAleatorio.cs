using System.Security.Cryptography;
using System.Text;
using System.Buffers.Text;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Seguranca;

public class GeradorAleatorio : IGeradorAleatorio
{
    public string GerarCodigoNumerico(int digitos)
    {
        var codigo = new StringBuilder(digitos);
        for (var i = 0; i < digitos; i++)
        {
            codigo.Append(RandomNumberGenerator.GetInt32(0, 10));
        }

        return codigo.ToString();
    }

    public string GerarTokenOpaco(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
}
