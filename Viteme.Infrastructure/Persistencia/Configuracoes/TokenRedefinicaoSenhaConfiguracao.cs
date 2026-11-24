using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class TokenRedefinicaoSenhaConfiguracao : IEntityTypeConfiguration<TokenRedefinicaoSenha>
{
    public void Configure(EntityTypeBuilder<TokenRedefinicaoSenha> b)
    {
        b.ToTable("token_redefinicao_senha");

        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.TokenHash).HasMaxLength(255).IsRequired();

        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasOne<Usuario>().WithMany().HasForeignKey(t => t.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
