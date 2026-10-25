using System.Text;
using Npgsql;

namespace Viteme.Infrastructure.Persistencia;

/// <summary>Traduz membros de enum do C# (<c>PendentePagamento</c>) para os rótulos do PostgreSQL (<c>PENDENTE_PAGAMENTO</c>).</summary>
public class NomeEnumPostgres : INpgsqlNameTranslator
{
    public string TranslateTypeName(string clrName) => clrName;

    public string TranslateMemberName(string clrName)
    {
        var resultado = new StringBuilder(clrName.Length + 4);
        for (var i = 0; i < clrName.Length; i++)
        {
            if (i > 0 && char.IsUpper(clrName[i]))
            {
                resultado.Append('_');
            }

            resultado.Append(char.ToUpperInvariant(clrName[i]));
        }

        return resultado.ToString();
    }
}
