namespace Viteme.Application.Interfaces;

/// <summary>Envio de e-mail transacional. A implementação real usa o SendGrid.</summary>
public interface IEnvioEmail
{
    Task EnviarAsync(string destinatario, string nomeDestinatario, string assunto, string texto, string html,
        CancellationToken cancellationToken = default);
}
