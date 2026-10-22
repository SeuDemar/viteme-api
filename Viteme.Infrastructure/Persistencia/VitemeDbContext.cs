using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia;

public class VitemeDbContext(DbContextOptions<VitemeDbContext> options, IRelogio relogio)
    : DbContext(options), IUnitOfWork
{
    private static readonly NomeEnumPostgres TradutorEnum = new();

    public DbSet<Usuario> Usuarios => Set<Usuario>();

    public DbSet<CodigoVerificacao> CodigosVerificacao => Set<CodigoVerificacao>();

    public DbSet<TokenRenovacao> TokensRenovacao => Set<TokenRenovacao>();

    public DbSet<Evento> Eventos => Set<Evento>();

    public DbSet<AtividadeProgramacao> Atividades => Set<AtividadeProgramacao>();

    public DbSet<ColaboradorEvento> Colaboradores => Set<ColaboradorEvento>();

    public DbSet<Inscricao> Inscricoes => Set<Inscricao>();

    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

    public DbSet<AlteracaoEvento> Alteracoes => Set<AlteracaoEvento>();

    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();

    public DbSet<EnvioSms> EnviosSms => Set<EnvioSms>();

    public DbSet<InteresseEvento> Interesses => Set<InteresseEvento>();

    public DbSet<Denuncia> Denuncias => Set<Denuncia>();

    public DbSet<Certificado> Certificados => Set<Certificado>();

    /// <summary>Configura Npgsql, os enums nativos e a convenção snake_case (usado pela API e pelo design-time).</summary>
    public static DbContextOptionsBuilder ConfigurarOpcoes(DbContextOptionsBuilder opcoes, string stringConexao) =>
        opcoes
            .UseNpgsql(stringConexao, npgsql =>
            {
                npgsql.MapEnum<StatusEvento>("status_evento", nameTranslator: TradutorEnum);
                npgsql.MapEnum<StatusInscricao>("status_inscricao", nameTranslator: TradutorEnum);
                npgsql.MapEnum<StatusPagamento>("status_pagamento", nameTranslator: TradutorEnum);
                npgsql.MapEnum<TipoNotificacao>("tipo_notificacao", nameTranslator: TradutorEnum);
                npgsql.MapEnum<StatusSms>("status_sms", nameTranslator: TradutorEnum);
                npgsql.MapEnum<MotivoDenuncia>("motivo_denuncia", nameTranslator: TradutorEnum);
            })
            .UseSnakeCaseNamingConvention();

    public Task<int> SalvarAsync(CancellationToken cancellationToken = default) => SaveChangesAsync(cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreencherDatasAutomaticas();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreencherDatasAutomaticas();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<StatusEvento>(name: "status_evento", nameTranslator: TradutorEnum);
        modelBuilder.HasPostgresEnum<StatusInscricao>(name: "status_inscricao", nameTranslator: TradutorEnum);
        modelBuilder.HasPostgresEnum<StatusPagamento>(name: "status_pagamento", nameTranslator: TradutorEnum);
        modelBuilder.HasPostgresEnum<TipoNotificacao>(name: "tipo_notificacao", nameTranslator: TradutorEnum);
        modelBuilder.HasPostgresEnum<StatusSms>(name: "status_sms", nameTranslator: TradutorEnum);
        modelBuilder.HasPostgresEnum<MotivoDenuncia>(name: "motivo_denuncia", nameTranslator: TradutorEnum);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VitemeDbContext).Assembly);
    }

    /// <summary>Convenção do projeto: <c>criado_em</c> e <c>atualizado_em</c> preenchidas pela aplicação, em UTC.</summary>
    private void PreencherDatasAutomaticas()
    {
        var agora = relogio.UtcAgora;

        foreach (var entrada in ChangeTracker.Entries())
        {
            if (entrada.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entrada.State == EntityState.Added && entrada.Metadata.FindProperty("CriadoEm") is not null)
            {
                entrada.Property("CriadoEm").CurrentValue = agora;
            }

            if (entrada.Metadata.FindProperty("AtualizadoEm") is not null)
            {
                entrada.Property("AtualizadoEm").CurrentValue = agora;
            }
        }
    }
}
