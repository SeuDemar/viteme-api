using Microsoft.AspNetCore.Authorization;

namespace Viteme.Api.Autorizacao;

public static class AutorizacaoExtensions
{
    public static IServiceCollection AddAutorizacaoPorEvento(this IServiceCollection servicos)
    {
        servicos.AddAuthorizationBuilder()
            .AddPolicy(Politicas.OrganizadorDoEvento, politica => politica
                .RequireAuthenticatedUser()
                .AddRequirements(new PapelNoEventoRequirement(exigeOrganizador: true)))
            .AddPolicy(Politicas.OrganizadorOuColaborador, politica => politica
                .RequireAuthenticatedUser()
                .AddRequirements(new PapelNoEventoRequirement(exigeOrganizador: false)));

        servicos.AddScoped<IAuthorizationHandler, PapelNoEventoHandler>();
        servicos.AddSingleton<IAuthorizationMiddlewareResultHandler, ResultadoAutorizacaoHandler>();

        return servicos;
    }
}
