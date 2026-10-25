using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class AlteracaoEventoConfiguracao : IEntityTypeConfiguration<AlteracaoEvento>
{
    public void Configure(EntityTypeBuilder<AlteracaoEvento> b)
    {
        b.ToTable("alteracao_evento");

        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Campo).HasMaxLength(50).IsRequired();
        b.Property(a => a.ValorAnterior).HasColumnType("text");
        b.Property(a => a.ValorNovo).HasColumnType("text");
        b.Property(a => a.Motivo).HasMaxLength(300).IsRequired();

        b.HasOne<Evento>().WithMany().HasForeignKey(a => a.EventoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Usuario>().WithMany().HasForeignKey(a => a.AutorId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(a => new { a.EventoId, a.CriadoEm }).IsDescending(false, true);
    }
}
