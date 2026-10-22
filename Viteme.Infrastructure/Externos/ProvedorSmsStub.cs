using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Externos;

public class SmsStubOptions
{
    public const string Secao = "Sms";

    /// <summary>Em desenvolvimento permite ver o código no log; em produção deve ficar falso.</summary>
    public bool LogarConteudo { get; set; }
}

/// <summary>
/// Stub do provedor de SMS: não envia nada. Registra no log (com o telefone mascarado) para a demonstração.
/// Troque por um adaptador real quando o fornecedor for escolhido.
/// </summary>
public class ProvedorSmsStub(ILogger<ProvedorSmsStub> logger, IOptions<SmsStubOptions> opcoes) : IProvedorSms
{
    public Task EnviarAsync(string telefone, string mensagem, CancellationToken cancellationToken = default)
    {
        var mascarado = telefone.Length > 4 ? new string('*', telefone.Length - 4) + telefone[^4..] : "****";

        if (opcoes.Value.LogarConteudo)
        {
            logger.LogInformation("[SMS stub] para {Telefone}: {Mensagem}", mascarado, mensagem);
        }
        else
        {
            logger.LogInformation("[SMS stub] mensagem simulada para {Telefone}", mascarado);
        }

        return Task.CompletedTask;
    }
}
