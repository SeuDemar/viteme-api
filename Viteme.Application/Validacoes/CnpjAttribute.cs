using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Viteme.Application.Validacoes;

/// <summary>
/// CNPJ com dígitos verificadores válidos, com ou sem máscara. Aceita o formato alfanumérico da Receita Federal
/// (12 caracteres de A–Z/0–9 e 2 dígitos verificadores), que convive com o numérico desde julho de 2026.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed partial class CnpjAttribute : ValidationAttribute
{
    private static readonly int[] PesosPrimeiro = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] PesosSegundo = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public CnpjAttribute() => ErrorMessage = "Informe um CNPJ válido.";

    public override bool IsValid(object? value) => value is null || (value is string cnpj && EhValido(cnpj));

    /// <summary>Remove a máscara (pontos, barra, hífen e espaços) e passa as letras para maiúsculas.</summary>
    public static string Normalizar(string cnpj) => Mascara().Replace(cnpj, string.Empty).ToUpperInvariant();

    public static bool EhValido(string cnpj)
    {
        var valor = Normalizar(cnpj);
        if (!Formato().IsMatch(valor) || valor.Distinct().Count() == 1)
        {
            return false;
        }

        return valor[12] - '0' == Digito(valor, PesosPrimeiro) && valor[13] - '0' == Digito(valor, PesosSegundo);
    }

    // Cada caractere vale o código ASCII menos 48: '0'–'9' valem 0–9 e 'A'–'Z' valem 17–42 (regra da Receita).
    private static int Digito(string valor, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++)
        {
            soma += (valor[i] - '0') * pesos[i];
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    [GeneratedRegex(@"^[A-Z0-9]{12}[0-9]{2}$")]
    private static partial Regex Formato();

    [GeneratedRegex(@"[\s./-]")]
    private static partial Regex Mascara();
}
