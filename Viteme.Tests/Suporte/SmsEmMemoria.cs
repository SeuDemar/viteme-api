using System.Collections.Concurrent;
using Viteme.Application.Interfaces;

namespace Viteme.Tests.Suporte;

/// <summary>Provedor de SMS falso: guarda as mensagens para os testes lerem o código enviado.</summary>
public class SmsEmMemoria : IProvedorSms
{
    public ConcurrentQueue<(string Telefone, string Mensagem)> Enviados { get; } = new();

    public bool Falhar { get; set; }

    public Task EnviarAsync(string telefone, string mensagem, CancellationToken cancellationToken = default)
    {
        if (Falhar)
        {
            throw new InvalidOperationException("provedor indisponível");
        }

        Enviados.Enqueue((telefone, mensagem));
        return Task.CompletedTask;
    }

    /// <summary>Código de 6 dígitos da última mensagem enviada.</summary>
    public string UltimoCodigo() =>
        System.Text.RegularExpressions.Regex.Match(Enviados.Last().Mensagem, @"\d{6}").Value;
}
