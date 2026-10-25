namespace Viteme.Application.Interfaces;

/// <summary>Provedor de SMS. Ainda sem fornecedor escolhido: a implementação atual é um stub.</summary>
public interface IProvedorSms
{
    Task EnviarAsync(string telefone, string mensagem, CancellationToken cancellationToken = default);
}
