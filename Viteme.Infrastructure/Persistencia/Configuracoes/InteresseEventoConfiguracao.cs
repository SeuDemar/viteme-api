using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class InteresseEventoConfiguracao : IEntityTypeConfiguration<InteresseEvento>
{
    public void Configure(EntityTypeBuilder<InteresseEvento> b)
    {
        b.ToTable("interesse_evento");

        b.HasKey(i => new { i.UsuarioId, i.EventoId });

        b.HasOne<Usuario>().WithMany().HasForeignKey(i => i.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Evento>().WithMany().HasForeignKey(i => i.EventoId).OnDelete(DeleteBehavior.Cascade);
    }
}
