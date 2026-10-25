namespace Viteme.Tests.Suporte;

/// <summary>Teste que precisa de um PostgreSQL real; é ignorado se <c>VITEME_TESTES_DB</c> não estiver definida.</summary>
public sealed class FactBancoAttribute : FactAttribute
{
    public const string Variavel = "VITEME_TESTES_DB";

    public FactBancoAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variavel)))
        {
            Skip = $"Defina {Variavel} (ex.: Host=localhost;Username=postgres;Password=postgres) para rodar os testes de banco.";
        }
    }
}
