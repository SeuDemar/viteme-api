using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class NotificacaoConfiguracao : IEntityTypeConfiguration<Notificacao>
{
    public void Configure(EntityTypeBuilder<Notificacao> b)
    {
        b.ToTable("notificacao");

        b.HasKey(n => n.Id);
        b.Property(n => n.Id).ValueGeneratedNever();
        b.Property(n => n.Titulo).HasMaxLength(150).IsRequired();
        b.Property(n => n.Mensagem).HasMaxLength(1000).IsRequired();
        b.Property(n => n.Lida).HasDefaultValue(false);

        b.HasOne<Usuario>().WithMany().HasForeignKey(n => n.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Evento>().WithMany().HasForeignKey(n => n.EventoId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(n => n.EnvioSms).WithOne().HasForeignKey<EnvioSms>(e => e.NotificacaoId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(n => new { n.UsuarioId, n.Lida, n.CriadoEm }).IsDescending(false, false, true);
    }
}
