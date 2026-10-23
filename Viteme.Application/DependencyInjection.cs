using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;

namespace Viteme.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection servicos)
    {
        servicos.AddScoped<AuthService>();
        servicos.AddScoped<UsuarioService>();
        servicos.AddScoped<NotificacaoService>();
        servicos.AddScoped<AlteracaoEventoRegistrador>();
        servicos.AddScoped<EventoService>();
        servicos.AddScoped<ProgramacaoService>();
        servicos.AddScoped<ColaboradorService>();
        servicos.AddScoped<EncerramentoEventosService>();
        return servicos;
    }
}
