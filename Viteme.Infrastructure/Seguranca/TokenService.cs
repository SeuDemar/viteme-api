using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Seguranca;

public class TokenService(IOptions<JwtOptions> opcoes) : ITokenService
{
    private readonly JwtOptions _jwt = opcoes.Value;

    public TokenAcesso GerarTokenAcesso(Usuario usuario, DateTime agora)
    {
        var expiraEm = agora.AddMinutes(_jwt.MinutosAcesso);

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Emissor,
            Audience = _jwt.Audiencia,
            IssuedAt = agora,
            Expires = expiraEm,
            SigningCredentials = new SigningCredentials(_jwt.ChaveDeAssinatura(), SecurityAlgorithms.HmacSha256),

            // Sem papéis: o papel depende do evento e é verificado no banco a cada requisição.
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, usuario.Nome),
                new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
        };

        return new TokenAcesso(new JsonWebTokenHandler().CreateToken(descritor), expiraEm);
    }
}
