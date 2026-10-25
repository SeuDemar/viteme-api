using System.ComponentModel.DataAnnotations;

namespace Viteme.Application.Validacoes;

/// <summary>RN02: mínimo de 8 caracteres, com pelo menos uma letra e um número.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SenhaForteAttribute : ValidationAttribute
{
    public const int Minimo = 8;
    public const int Maximo = 128;

    public SenhaForteAttribute() => ErrorMessage = "A senha deve ter de 8 a 128 caracteres, com pelo menos uma letra e um número.";

    public override bool IsValid(object? value) =>
        value is string senha
        && senha.Length is >= Minimo and <= Maximo
        && senha.Any(char.IsLetter)
        && senha.Any(char.IsDigit);
}
