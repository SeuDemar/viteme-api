using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class ColaboradorEventoConfiguracao : IEntityTypeConfiguration<ColaboradorEvento>
{
    public void Configure(EntityTypeBuilder<ColaboradorEvento> b)
    {
        b.ToTable("colaborador_evento");

        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();

        b.HasOne<Usuario>().WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Cascade);

        // RI06
        b.HasIndex(c => new { c.EventoId, c.UsuarioId }).IsUnique();
        b.HasIndex(c => c.UsuarioId);
    }
}
