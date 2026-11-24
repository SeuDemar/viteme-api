using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Viteme.Application.Validacoes;

/// <summary>CPF com dígitos verificadores válidos, com ou sem máscara (pontos e hífen).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed partial class CpfAttribute : ValidationAttribute
{
    public CpfAttribute() => ErrorMessage = "Informe um CPF válido.";

    public override bool IsValid(object? value) => value is null || (value is string cpf && EhValido(cpf));

    /// <summary>Remove pontos, hífen e espaços.</summary>
    public static string Normalizar(string cpf) => Mascara().Replace(cpf, string.Empty);

    public static bool EhValido(string cpf)
    {
        var valor = Normalizar(cpf);
        if (!Formato().IsMatch(valor) || valor.Distinct().Count() == 1)
        {
            return false;
        }

        return valor[9] - '0' == Digito(valor, 9) && valor[10] - '0' == Digito(valor, 10);
    }

    // Pesos decrescentes de (n + 1) até 2 sobre os n primeiros dígitos; resto < 2 vira 0.
    private static int Digito(string valor, int quantidade)
    {
        var soma = 0;
        for (var i = 0; i < quantidade; i++)
        {
            soma += (valor[i] - '0') * (quantidade + 1 - i);
        }

        var resto = soma * 10 % 11;
        return resto == 10 ? 0 : resto;
    }

    [GeneratedRegex(@"^\d{11}$")]
    private static partial Regex Formato();

    [GeneratedRegex(@"[\s.-]")]
    private static partial Regex Mascara();
}
