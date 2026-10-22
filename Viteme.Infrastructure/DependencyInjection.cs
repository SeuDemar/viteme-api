using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Persistencia;

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

        return servicos;
    }
}
