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

    // Sem 0/O e 1/I para o código ser ditado ou digitado sem confusão.
    private const string AlfabetoCodigo = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string GerarCodigoAlfanumerico(int tamanho) =>
        string.Create(tamanho, AlfabetoCodigo, static (destino, alfabeto) =>
        {
            for (var i = 0; i < destino.Length; i++)
            {
                destino[i] = alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
            }
        });

    public string GerarTokenOpaco(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
}
