using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class CodigoVerificacaoConfiguracao : IEntityTypeConfiguration<CodigoVerificacao>
{
    public void Configure(EntityTypeBuilder<CodigoVerificacao> b)
    {
        b.ToTable("codigo_verificacao", t => t.HasCheckConstraint("ck_codigo_verificacao_tentativas", "tentativas <= 5"));

        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.CodigoHash).HasMaxLength(255).IsRequired();
        b.Property(c => c.Tentativas).HasDefaultValue((short)0);
        b.Property(c => c.Utilizado).HasDefaultValue(false);
        b.Ignore(c => c.TentativasEsgotadas);

        b.HasOne<Usuario>().WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
