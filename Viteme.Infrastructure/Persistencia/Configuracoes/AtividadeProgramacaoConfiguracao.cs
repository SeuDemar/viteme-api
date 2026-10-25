using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class AtividadeProgramacaoConfiguracao : IEntityTypeConfiguration<AtividadeProgramacao>
{
    public void Configure(EntityTypeBuilder<AtividadeProgramacao> b)
    {
        b.ToTable("atividade_programacao", t => t.HasCheckConstraint("ck_atividade_programacao_fim", "fim > inicio"));

        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Titulo).HasMaxLength(150).IsRequired();
        b.Property(a => a.Palestrante).HasMaxLength(120);
        b.Property(a => a.Local).HasMaxLength(200);
    }
}
