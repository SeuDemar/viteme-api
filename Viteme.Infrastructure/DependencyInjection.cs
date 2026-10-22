using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Externos;
using Viteme.Infrastructure.Persistencia;
using Viteme.Infrastructure.Persistencia.Repositorios;
using Viteme.Infrastructure.Seguranca;

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

        servicos.Configure<JwtOptions>(configuracao.GetSection(JwtOptions.Secao));
        servicos.AddSingleton<ISenhaHasher, SenhaHasherPbkdf2>();
        servicos.AddSingleton<ITokenService, TokenService>();
        servicos.AddSingleton<IHashSeguro, HashSeguro>();
        servicos.AddSingleton<IGeradorAleatorio, GeradorAleatorio>();

        // Provedor de SMS ainda não escolhido: stub que só registra no log.
        servicos.Configure<SmsStubOptions>(configuracao.GetSection(SmsStubOptions.Secao));
        servicos.AddSingleton<IProvedorSms, ProvedorSmsStub>();

        return servicos;
    }
}
