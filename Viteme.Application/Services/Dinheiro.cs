using System.Globalization;

namespace Viteme.Application.Services;

internal static class Dinheiro
{
    /// <summary>Formata em reais (<c>R$ 1.250,00</c>) sem depender da cultura do servidor.</summary>
    public static string Formatar(decimal valor)
    {
        var texto = valor.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return "R$ " + texto.Replace(',', '#').Replace('.', ',').Replace('#', '.');
    }
}
