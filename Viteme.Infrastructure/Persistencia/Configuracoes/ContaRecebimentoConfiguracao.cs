using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class ContaRecebimentoConfiguracao : IEntityTypeConfiguration<ContaRecebimento>
{
    public void Configure(EntityTypeBuilder<ContaRecebimento> b)
    {
        // Situação em texto (PENDENTE | APROVADA | REJEITADA) com CHECK, e não enum nativo do PostgreSQL: a tabela entra
        // desligada (Fase 2) e os valores podem mudar quando a conta PJ do sandbox responder a Q6.
        b.ToTable("conta_recebimento", t => t.HasCheckConstraint(
            "ck_conta_recebimento_situacao", "situacao IN ('PENDENTE', 'APROVADA', 'REJEITADA')"));

        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.ContaGatewayId).HasMaxLength(100).IsRequired();
        b.Property(c => c.WalletId).HasMaxLength(100).IsRequired();
        b.Property(c => c.ChaveApiCifrada).HasMaxLength(1000).IsRequired();
        b.Property(c => c.UrlOnboarding).HasMaxLength(500);
        b.Property(c => c.Situacao)
            .HasConversion(
                s => s.ToString().ToUpperInvariant(),
                s => Enum.Parse<SituacaoContaRecebimento>(s, true))
            .HasMaxLength(20)
            .IsRequired();

        b.HasIndex(c => c.UsuarioId).IsUnique();
        b.HasIndex(c => c.ContaGatewayId).IsUnique();
        b.HasOne<Usuario>().WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
