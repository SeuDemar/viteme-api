using System.ComponentModel.DataAnnotations;

namespace Viteme.Application.Validacoes;

/// <summary>Valor monetário com no máximo duas casas decimais.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DuasCasasDecimaisAttribute : ValidationAttribute
{
    public DuasCasasDecimaisAttribute() => ErrorMessage = "Informe o valor com no máximo duas casas decimais.";

    public override bool IsValid(object? value) => value is null || (value is decimal d && decimal.Round(d, 2) == d);
}
