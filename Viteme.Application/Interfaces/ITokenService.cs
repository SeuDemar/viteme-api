using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public record TokenAcesso(string Token, DateTime ExpiraEm);

public interface ITokenService
{
    /// <summary>JWT de acesso (15 min). Os claims não incluem papéis, que dependem do evento.</summary>
    TokenAcesso GerarTokenAcesso(Usuario usuario, DateTime agora);
}
