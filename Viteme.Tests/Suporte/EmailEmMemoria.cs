using System.Collections.Concurrent;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Tests.Suporte;

/// <summary>E-mail de redefinição de senha falso: guarda o token para os testes usarem o "link" recebido.</summary>
public class EmailEmMemoria : IEmailRedefinicaoSenha
{
    public ConcurrentQueue<(string Email, string Token)> Enviados { get; } = new();

    public bool Falhar { get; set; }

    public Task EnviarAsync(Usuario usuario, string token, TimeSpan validade, CancellationToken cancellationToken = default)
    {
        if (Falhar)
        {
            throw new InvalidOperationException("provedor de e-mail indisponível");
        }

        Enviados.Enqueue((usuario.Email, token));
        return Task.CompletedTask;
    }

    public string UltimoToken() => Enviados.Last().Token;
}
