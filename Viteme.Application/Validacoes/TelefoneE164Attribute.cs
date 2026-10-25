using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Viteme.Application.Validacoes;

/// <summary>Telefone opcional no formato E.164 (ex.: <c>+5544999990000</c>).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed partial class TelefoneE164Attribute : ValidationAttribute
{
    public TelefoneE164Attribute() => ErrorMessage = "Informe o telefone no formato internacional, ex.: +5544999990000.";

    public override bool IsValid(object? value) =>
        value is null || (value is string telefone && (telefone.Length == 0 || Padrao().IsMatch(telefone)));

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex Padrao();
}
