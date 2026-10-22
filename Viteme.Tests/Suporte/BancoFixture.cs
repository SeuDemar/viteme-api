using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Persistencia;

namespace Viteme.Tests.Suporte;

/// <summary>
/// Cria um banco temporário com as migrations aplicadas e o remove ao final.
/// Exige <c>VITEME_TESTES_DB</c> apontando para um PostgreSQL com permissão de criar bancos
/// (nunca o banco real do projeto).
/// </summary>
public sealed class BancoFixture : IAsyncLifetime
{
    private readonly string? _conexaoAdmin = Environment.GetEnvironmentVariable(FactBancoAttribute.Variavel);
    private readonly string _nome = $"viteme_teste_{Guid.NewGuid():N}";
    private string? _conexao;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_conexaoAdmin))
        {
            return;
        }

        await using (var admin = new NpgsqlConnection(_conexaoAdmin))
        {
            await admin.OpenAsync();
            await using var comando = new NpgsqlCommand($"CREATE DATABASE {_nome}", admin);
            await comando.ExecuteNonQueryAsync();
        }

        _conexao = new NpgsqlConnectionStringBuilder(_conexaoAdmin) { Database = _nome }.ConnectionString;

        await using var contexto = CriarContexto();
        await contexto.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_conexaoAdmin) || _conexao is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_conexaoAdmin);
        await admin.OpenAsync();
        await using var comando = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_nome} WITH (FORCE)", admin);
        await comando.ExecuteNonQueryAsync();
    }

    public VitemeDbContext CriarContexto(IRelogio? relogio = null)
    {
        var opcoes = new DbContextOptionsBuilder<VitemeDbContext>();
        VitemeDbContext.ConfigurarOpcoes(opcoes, _conexao ?? throw new InvalidOperationException("Banco de teste não inicializado."));
        return new VitemeDbContext(opcoes.Options, relogio ?? new RelogioFixo());
    }
}
