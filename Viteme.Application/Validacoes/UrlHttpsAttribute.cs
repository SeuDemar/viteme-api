using System.ComponentModel.DataAnnotations;

namespace Viteme.Application.Validacoes;

/// <summary>URL opcional, absoluta e com https.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class UrlHttpsAttribute : ValidationAttribute
{
    public UrlHttpsAttribute() => ErrorMessage = "Informe uma URL válida iniciando com https.";

    public override bool IsValid(object? value) =>
        value is null
        || (value is string url && (url.Length == 0
            || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)));
}
