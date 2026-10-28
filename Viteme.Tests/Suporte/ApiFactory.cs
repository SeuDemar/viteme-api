using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viteme.Application.Interfaces;

namespace Viteme.Tests.Suporte;

/// <summary>
/// Aplicação em memória para os testes. Sempre recebe uma chave JWT de teste (a aplicação exige uma).
/// Para testes com banco, use <see cref="ComBanco"/>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ChaveJwt = "chave-de-teste-com-mais-de-32-caracteres-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Chave", ChaveJwt);
        builder.UseSetting("RateLimiting:Habilitar", "false");

        // Tarefas em segundo plano ficam desligadas: os testes as executam de forma explícita.
        builder.UseSetting("Tarefas:Habilitar", "false");
    }

    /// <summary>Aplicação ligada ao banco temporário, com relógio e SMS controláveis.</summary>
    public WebApplicationFactory<Program> ComBanco(
        string conexao, RelogioFixo relogio, SmsEmMemoria sms, GatewayEmMemoria gateway, CnpjEmMemoria cnpj,
        bool limitacao = false, bool tokenRealista = false) =>
        WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", conexao);
            b.UseSetting("RateLimiting:Habilitar", limitacao ? "true" : "false");

            // O relógio dos testes salta horas e dias; por padrão o token de acesso dura o suficiente para o teste não
            // precisar renová-lo. Os testes de expiração pedem a validade real (15 min).
            b.UseSetting("Jwt:MinutosAcesso", tokenRealista ? "15" : "525600");
            b.ConfigureServices(servicos =>
            {
                servicos.RemoveAll<IRelogio>();
                servicos.AddSingleton<IRelogio>(relogio);
                servicos.RemoveAll<IProvedorSms>();
                servicos.AddSingleton<IProvedorSms>(sms);
                servicos.RemoveAll<IGatewayPagamento>();
                servicos.AddSingleton<IGatewayPagamento>(gateway);
                servicos.RemoveAll<ISimuladorGateway>();
                servicos.AddSingleton<ISimuladorGateway>(gateway);
                servicos.RemoveAll<IConsultaCnpj>();
                servicos.AddSingleton<IConsultaCnpj>(cnpj);
            });
        });
}
