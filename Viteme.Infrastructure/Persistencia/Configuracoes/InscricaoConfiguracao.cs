using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class InscricaoConfiguracao : IEntityTypeConfiguration<Inscricao>
{
    public void Configure(EntityTypeBuilder<Inscricao> b)
    {
        b.ToTable("inscricao");

        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.CodigoCheckin).HasMaxLength(12).IsRequired();
        b.Property(i => i.PresencaConfirmada).HasDefaultValue(false);
        b.Property(i => i.Versao).IsRowVersion();
        b.Ignore(i => i.Ativa);

        b.HasOne<Evento>().WithMany().HasForeignKey(i => i.EventoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Usuario>().WithMany().HasForeignKey(i => i.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(i => i.Pagamentos).WithOne().HasForeignKey(p => p.InscricaoId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(i => i.CodigoCheckin).IsUnique();

        // RI05: no máximo uma inscrição ativa por usuário em cada evento (reinscrição após cancelar ou expirar).
        b.HasIndex(i => new { i.EventoId, i.UsuarioId })
            .IsUnique()
            .HasDatabaseName("ux_inscricao_ativa_por_usuario_evento")
            .HasFilter("status IN ('PENDENTE_PAGAMENTO', 'CONFIRMADA')");

        b.HasIndex(i => new { i.EventoId, i.Status });
        b.HasIndex(i => i.UsuarioId);

        // Tarefa de expiração de reservas.
        b.HasIndex(i => i.ReservadaAte)
            .HasDatabaseName("ix_inscricao_reservada_ate_pendente")
            .HasFilter("status = 'PENDENTE_PAGAMENTO'");
    }
}
