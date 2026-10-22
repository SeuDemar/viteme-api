using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class CertificadoConfiguracao : IEntityTypeConfiguration<Certificado>
{
    public void Configure(EntityTypeBuilder<Certificado> b)
    {
        b.ToTable("certificado");

        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.CodigoValidacao).HasMaxLength(20).IsRequired();

        b.HasOne<Inscricao>().WithOne().HasForeignKey<Certificado>(c => c.InscricaoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(c => c.CodigoValidacao).IsUnique();
    }
}
