using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Externos;
using Viteme.Infrastructure.Persistencia;
using Viteme.Infrastructure.Persistencia.Repositorios;
using Viteme.Infrastructure.Seguranca;
using Viteme.Infrastructure.Tarefas;

namespace Viteme.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.AddSingleton<IRelogio, RelogioSistema>();

        servicos.AddDbContext<VitemeDbContext>(opcoes =>
        {
            var conexao = configuracao.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(conexao))
            {
                throw new InvalidOperationException(
                    "String de conexão 'ConnectionStrings:Default' não configurada. Use user-secrets ou a variável ConnectionStrings__Default.");
            }

            VitemeDbContext.ConfigurarOpcoes(opcoes, conexao);
        });

        servicos.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<VitemeDbContext>());

        servicos.AddScoped<IUsuarioRepository, UsuarioRepository>();
        servicos.AddScoped<ICodigoVerificacaoRepository, CodigoVerificacaoRepository>();
        servicos.AddScoped<ITokenRenovacaoRepository, TokenRenovacaoRepository>();
        servicos.AddScoped<IPagamentoRepository, PagamentoRepository>();
        servicos.AddScoped<IEventoRepository, EventoRepository>();
        servicos.AddScoped<IColaboradorRepository, ColaboradorRepository>();
        servicos.AddScoped<IInscricaoRepository, InscricaoRepository>();
        servicos.AddScoped<IInteresseRepository, InteresseRepository>();
        servicos.AddScoped<INotificacaoRepository, NotificacaoRepository>();
        servicos.AddScoped<IDenunciaRepository, DenunciaRepository>();
        servicos.AddScoped<ICertificadoRepository, CertificadoRepository>();
        servicos.AddScoped<IEnvioSmsRepository, EnvioSmsRepository>();

        // Gateway de pagamento ainda não escolhido na época da spec; hoje é um stub. O adaptador do Asaas o substituirá.
        servicos.Configure<GatewayStubOptions>(configuracao.GetSection(GatewayStubOptions.Secao));
        servicos.AddSingleton<GatewayPagamentoStub>();
        servicos.AddSingleton<IGatewayPagamento>(sp => sp.GetRequiredService<GatewayPagamentoStub>());
        servicos.AddSingleton<ISimuladorGateway>(sp => sp.GetRequiredService<GatewayPagamentoStub>());

        // Canais de notificação (RN16): o site sempre; o SMS só registra o envio, que uma tarefa entrega depois.
        servicos.AddScoped<INotificationSender, NotificacaoSiteSender>();
        servicos.AddScoped<INotificationSender, NotificacaoSmsSender>();

        servicos.Configure<CertificadosOptions>(configuracao.GetSection(CertificadosOptions.Secao));
        servicos.AddSingleton<ICertificadoPdfGerador, CertificadoPdfGerador>();

        servicos.Configure<JwtOptions>(configuracao.GetSection(JwtOptions.Secao));
        servicos.AddSingleton<ISenhaHasher, SenhaHasherPbkdf2>();
        servicos.AddSingleton<ITokenService, TokenService>();
        servicos.AddSingleton<IHashSeguro, HashSeguro>();
        servicos.AddSingleton<IGeradorAleatorio, GeradorAleatorio>();

        // Provedor de SMS ainda não escolhido: stub que só registra no log.
        servicos.Configure<SmsStubOptions>(configuracao.GetSection(SmsStubOptions.Secao));
        servicos.AddSingleton<IProvedorSms, ProvedorSmsStub>();

        // Consulta de CNPJ do organizador (RN24): BrasilAPI; o stub só para desenvolvimento sem internet.
        servicos.Configure<ConsultaCnpjOptions>(configuracao.GetSection(ConsultaCnpjOptions.Secao));
        if (configuracao.GetValue<bool>($"{ConsultaCnpjOptions.Secao}:UsarStub"))
        {
            servicos.AddSingleton<IConsultaCnpj, ConsultaCnpjStub>();
        }
        else
        {
            servicos.AddSingleton<IConsultaCnpj, ConsultaCnpjBrasilApi>();
        }

        servicos.Configure<TarefasOptions>(configuracao.GetSection(TarefasOptions.Secao));
        servicos.AddHostedService<EncerrarEventosWorker>();
        servicos.AddHostedService<ReservasEEstornosWorker>();
        servicos.AddHostedService<EnvioSmsWorker>();
        servicos.AddHostedService<LembretesWorker>();

        return servicos;
    }
}
