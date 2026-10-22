using Viteme.Domain.Entities;

namespace Viteme.Tests.Suporte;

/// <summary>Fábricas de entidades válidas para os testes.</summary>
public static class Dados
{
    public static readonly DateTime Agora = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public static Usuario NovoUsuario(string? email = null, string? telefone = null) =>
        new("Maria Souza", email ?? $"{Guid.NewGuid():N}@exemplo.com", "hash", telefone);

    public static Evento NovoEvento(Guid organizadorId, decimal preco = 0, int? capacidade = 10, DateTime? inicio = null)
    {
        var dataInicio = inicio ?? Agora.AddDays(10);
        return new Evento(
            organizadorId, "Semana Acadêmica", "Palestras e oficinas.", "semana acadêmica", "Auditório",
            null, dataInicio, dataInicio.AddHours(8), Agora.AddDays(-1), dataInicio.AddHours(-1), capacidade, preco, 8);
    }
}
