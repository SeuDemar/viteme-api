using Viteme.Domain.Entities;

namespace Viteme.Tests.Suporte;

/// <summary>Fábricas de entidades válidas para os testes.</summary>
public static class Dados
{
    public static readonly DateTime Agora = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public static Usuario NovoUsuario(string? email = null, string? telefone = null) =>
        new("Maria Souza", email ?? $"{Guid.NewGuid():N}@exemplo.com", "hash", telefone);

    /// <summary>CNPJ numérico aleatório com dígitos verificadores corretos (14 dígitos, sem máscara).</summary>
    public static string NovoCnpj()
    {
        var digitos = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        digitos.Add(Digito(digitos, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digitos.Add(Digito(digitos, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digitos);

        static int Digito(List<int> d, int[] pesos)
        {
            var resto = pesos.Select((p, i) => d[i] * p).Sum() % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }

    public static Evento NovoEvento(Guid organizadorId, decimal preco = 0, int? capacidade = 10, DateTime? inicio = null)
    {
        var dataInicio = inicio ?? Agora.AddDays(10);
        return new Evento(
            organizadorId, "Semana Acadêmica", "Palestras e oficinas.", "semana acadêmica", "Auditório",
            null, dataInicio, dataInicio.AddHours(8), Agora.AddDays(-1), dataInicio.AddHours(-1), capacidade, preco, 8);
    }
}
