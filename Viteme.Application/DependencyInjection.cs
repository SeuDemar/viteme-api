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
        servicos.AddScoped<EstornoService>();
        servicos.AddScoped<PagamentoService>();
        servicos.AddScoped<InscricaoService>();
        servicos.AddScoped<ExpiracaoReservasService>();
        servicos.AddScoped<CancelamentoEventoService>();
        servicos.AddScoped<InteresseService>();
        servicos.AddScoped<CaixaNotificacoesService>();
        servicos.AddScoped<DenunciaService>();
        servicos.AddScoped<CertificadoService>();
        servicos.AddScoped<EnvioSmsService>();
        servicos.AddScoped<LembretesService>();
        return servicos;
    }
}
