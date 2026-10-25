using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Viteme.Infrastructure.Persistencia;

/// <summary>Usada só pelas ferramentas do EF (<c>dotnet ef</c>). Não conecta ao banco para gerar migrations.</summary>
public class VitemeDbContextFactory : IDesignTimeDbContextFactory<VitemeDbContext>
{
    public VitemeDbContext CreateDbContext(string[] args)
    {
        var conexao = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=viteme_design;Username=postgres;Password=postgres";

        var opcoes = new DbContextOptionsBuilder<VitemeDbContext>();
        VitemeDbContext.ConfigurarOpcoes(opcoes, conexao);
        return new VitemeDbContext(opcoes.Options, new RelogioSistema());
    }
}
