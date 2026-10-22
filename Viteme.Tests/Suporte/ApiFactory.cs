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
    }

    /// <summary>Aplicação ligada ao banco temporário, com relógio e SMS controláveis.</summary>
    public WebApplicationFactory<Program> ComBanco(
        string conexao, RelogioFixo relogio, SmsEmMemoria sms, bool limitacao = false) =>
        WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", conexao);
            b.UseSetting("RateLimiting:Habilitar", limitacao ? "true" : "false");
            b.ConfigureServices(servicos =>
            {
                servicos.RemoveAll<IRelogio>();
                servicos.AddSingleton<IRelogio>(relogio);
                servicos.RemoveAll<IProvedorSms>();
                servicos.AddSingleton<IProvedorSms>(sms);
            });
        });
}
