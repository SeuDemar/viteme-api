using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Viteme.Api.Autenticacao;
using Viteme.Api.Middlewares;

namespace Viteme.Api.Configuracao;

/// <summary>
/// Rate limiting do ASP.NET Core. Seção <c>RateLimiting</c>: <c>Habilitar</c> (padrão true),
/// <c>LoginPorMinuto</c> (5), <c>CadastroPorMinuto</c> (10), <c>CodigoSmsPorMinuto</c> (5).
/// </summary>
public static class LimitacaoExtensions
{
    public const string Login = "login";
    public const string Cadastro = "cadastro";
    public const string CodigoSms = "codigo-sms";

    public static IServiceCollection AddLimitacaoDeRequisicoes(this IServiceCollection servicos)
    {
        servicos.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opcoes.OnRejected = (contexto, _) => new ValueTask(RespostaProblema.EscreverAsync(
                contexto.HttpContext, StatusCodes.Status429TooManyRequests, "Muitas requisições",
                "Limite de requisições excedido. Tente novamente em instantes.", "LIMITE_EXCEDIDO"));

            // Por IP: login e cadastro. Por usuário autenticado: envio de código por SMS.
            opcoes.AddPolicy(Login, c => Janela(c, Login, "LoginPorMinuto", 5, PorIp(c)));
            opcoes.AddPolicy(Cadastro, c => Janela(c, Cadastro, "CadastroPorMinuto", 10, PorIp(c)));
            opcoes.AddPolicy(CodigoSms, c => Janela(c, CodigoSms, "CodigoSmsPorMinuto", 5, PorUsuario(c)));
        });

        return servicos;
    }

    private static RateLimitPartition<string> Janela(HttpContext contexto, string politica, string chave, int padrao, string particao)
    {
        var configuracao = contexto.RequestServices.GetRequiredService<IConfiguration>().GetSection("RateLimiting");
        if (!configuracao.GetValue("Habilitar", true))
        {
            return RateLimitPartition.GetNoLimiter(politica);
        }

        return RateLimitPartition.GetFixedWindowLimiter($"{politica}:{particao}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = configuracao.GetValue(chave, padrao),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }

    private static string PorIp(HttpContext contexto) => contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

    private static string PorUsuario(HttpContext contexto) =>
        contexto.User.Identity?.IsAuthenticated == true ? contexto.User.ObterUsuarioId().ToString() : PorIp(contexto);
}
