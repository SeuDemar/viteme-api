using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

/// <summary>Monta e envia o e-mail com o link de redefinição de senha.</summary>
public interface IEmailRedefinicaoSenha
{
    Task EnviarAsync(Usuario usuario, string token, TimeSpan validade, CancellationToken cancellationToken = default);
}
