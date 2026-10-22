using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Viteme.Api.Middlewares;
using Viteme.Infrastructure.Seguranca;

namespace Viteme.Api.Autenticacao;

public static class AutenticacaoExtensions
{
    /// <summary>JWT HS256 com validação de assinatura, emissor, audiência, expiração (tolerância de 30 s) e algoritmo.</summary>
    public static IServiceCollection AddAutenticacaoJwt(this IServiceCollection servicos)
    {
        servicos.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        servicos.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((opcoes, jwt) =>
            {
                // Mantém "sub" como está (sem converter para NameIdentifier).
                opcoes.MapInboundClaims = false;

                opcoes.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Emissor,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audiencia,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.Value.ChaveDeAssinatura(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };

                opcoes.Events = new JwtBearerEvents
                {
                    OnChallenge = contexto =>
                    {
                        contexto.HandleResponse();
                        return RespostaProblema.EscreverAsync(contexto.HttpContext, StatusCodes.Status401Unauthorized,
                            "Não autenticado", "Token ausente, inválido ou expirado.", "NAO_AUTENTICADO");
                    },
                };
            });

        return servicos;
    }
}
