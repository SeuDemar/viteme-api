using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class DenunciaConfiguracao : IEntityTypeConfiguration<Denuncia>
{
    public void Configure(EntityTypeBuilder<Denuncia> b)
    {
        b.ToTable("denuncia", t => t.HasCheckConstraint("ck_denuncia_descricao_outro", "motivo <> 'OUTRO' OR descricao IS NOT NULL"));

        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.Descricao).HasMaxLength(500);

        b.HasOne<Evento>().WithMany().HasForeignKey(d => d.EventoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Usuario>().WithMany().HasForeignKey(d => d.UsuarioId).OnDelete(DeleteBehavior.Restrict);

        // RI06
        b.HasIndex(d => new { d.EventoId, d.UsuarioId }).IsUnique();
        b.HasIndex(d => new { d.EventoId, d.CriadoEm });
    }
}
