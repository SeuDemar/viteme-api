using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;

namespace Viteme.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection servicos)
    {
        servicos.AddScoped<AuthService>();
        servicos.AddScoped<UsuarioService>();
        return servicos;
    }
}
