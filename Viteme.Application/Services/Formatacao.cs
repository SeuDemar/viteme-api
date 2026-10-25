using System.Globalization;

namespace Viteme.Application.Services;

internal static class Formatacao
{
    /// <summary>Horário de Brasília (UTC−3, sem horário de verão desde 2019) para textos mostrados ao usuário.</summary>
    public static string HorarioBrasilia(DateTime utc) =>
        utc.AddHours(-3).ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.InvariantCulture);
}
