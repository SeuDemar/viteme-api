using Viteme.Domain.Excecoes;

namespace Viteme.Api.Configuracao;

/// <summary>ETag do evento: a versão (<c>xmin</c>) entre aspas. Chega de volta no cabeçalho <c>If-Match</c> (RI11).</summary>
public static class VersaoEvento
{
    public static string ParaEtag(uint versao) => $"\"{versao}\"";

    /// <summary>Aceita <c>"123"</c>, <c>W/"123"</c> e <c>123</c>. Ausente retorna nulo; formato inválido é 400.</summary>
    public static uint? Interpretar(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return null;
        }

        var valor = ifMatch.Trim();
        if (valor.StartsWith("W/", StringComparison.Ordinal))
        {
            valor = valor[2..];
        }

        valor = valor.Trim('"');
        return uint.TryParse(valor, out var versao)
            ? versao
            : throw new RegraDeNegocioException("VERSAO_INVALIDA", "Versão inválida",
                "O cabeçalho If-Match deve conter o ETag recebido ao consultar o evento.", 400);
    }
}
