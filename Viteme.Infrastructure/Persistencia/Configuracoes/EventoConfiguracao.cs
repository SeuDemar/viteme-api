using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class EventoConfiguracao : IEntityTypeConfiguration<Evento>
{
    public void Configure(EntityTypeBuilder<Evento> b)
    {
        b.ToTable("evento", t =>
        {
            t.HasCheckConstraint("ck_evento_data_fim", "data_fim > data_inicio");
            t.HasCheckConstraint("ck_evento_inscricao_periodo", "inscricao_fim > inscricao_inicio");
            t.HasCheckConstraint("ck_evento_inscricao_fim_ate_inicio", "inscricao_fim <= data_inicio");
            t.HasCheckConstraint("ck_evento_preco", "preco >= 0");
            t.HasCheckConstraint("ck_evento_capacidade", "capacidade IS NULL OR capacidade > 0");
            t.HasCheckConstraint("ck_evento_carga_horaria", "carga_horaria IS NULL OR carga_horaria > 0");
        });

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Titulo).HasMaxLength(150).IsRequired();
        b.Property(e => e.Descricao).HasColumnType("text").IsRequired();
        b.Property(e => e.Categoria).HasMaxLength(60).IsRequired();
        b.Property(e => e.Local).HasMaxLength(200).IsRequired();
        b.Property(e => e.ImagemUrl).HasMaxLength(500);
        b.Property(e => e.Preco).HasPrecision(10, 2).HasDefaultValue(0m);
        b.Property(e => e.Status).HasDefaultValueSql("'RASCUNHO'::status_evento");

        // RI11: concorrência otimista pela coluna de sistema xmin, exposta como ETag.
        b.Property(e => e.Versao).IsRowVersion();

        b.Ignore(e => e.Gratuito);
        b.Ignore(e => e.Finalizado);

        b.HasOne<Usuario>().WithMany().HasForeignKey(e => e.OrganizadorId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(e => e.Atividades).WithOne().HasForeignKey(a => a.EventoId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(e => e.Colaboradores).WithOne().HasForeignKey(c => c.EventoId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(e => new { e.Status, e.DataInicio });
        b.HasIndex(e => e.OrganizadorId);
    }
}
