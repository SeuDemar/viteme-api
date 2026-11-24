namespace Viteme.Tests.Suporte;

/// <summary>
/// Teste contra o sandbox real do Asaas: só roda com <c>VITEME_TESTES_ASAAS_SANDBOX=1</c> e a chave da conta sandbox PJ em
/// <c>ASAAS_API_KEY</c>. Cria uma subconta de teste a cada execução (o sandbox aceita 20 por dia). Nunca roda na CI.
/// </summary>
public sealed class FactAsaasSandboxAttribute : FactAttribute
{
    public const string Variavel = "VITEME_TESTES_ASAAS_SANDBOX";

    public FactAsaasSandboxAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variavel) != "1" || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASAAS_API_KEY")))
        {
            Skip = $"Defina {Variavel}=1 e ASAAS_API_KEY (chave do sandbox PJ) para rodar contra o sandbox do Asaas.";
        }
    }
}
