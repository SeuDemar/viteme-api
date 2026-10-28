using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("usuario", t =>
        {
            t.HasCheckConstraint("ck_usuario_sms_exige_telefone_verificado", "NOT aceita_sms OR telefone_verificado");
            t.HasCheckConstraint("ck_usuario_telefone_e164", "telefone IS NULL OR telefone ~ '^\\+[1-9][0-9]{7,14}$'");
            t.HasCheckConstraint("ck_usuario_cnpj_formato", "cnpj IS NULL OR cnpj ~ '^[A-Z0-9]{12}[0-9]{2}$'");
        });

        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();
        b.Property(u => u.Nome).HasMaxLength(120).IsRequired();
        b.Property(u => u.Email).HasMaxLength(160).IsRequired();
        b.Property(u => u.SenhaHash).HasMaxLength(255).IsRequired();
        b.Property(u => u.Telefone).HasMaxLength(20);
        b.Property(u => u.TelefoneVerificado).HasDefaultValue(false);
        b.Property(u => u.AceitaSms).HasDefaultValue(false);
        b.Property(u => u.TentativasLogin).HasDefaultValue((short)0);
        b.Property(u => u.Cnpj).HasMaxLength(14);
        b.Property(u => u.RazaoSocial).HasMaxLength(200);
        b.HasIndex(u => u.Cnpj).IsUnique().HasDatabaseName("ux_usuario_cnpj").HasFilter("cnpj IS NOT NULL");
        b.Property(u => u.CriadoEm).IsRequired();
        b.Property(u => u.AtualizadoEm).IsRequired();

        // RI02: índice único em lower(email), criado por SQL na migration (o EF não modela expressões).
        b.Ignore(u => u.PodeReceberSms);
    }
}
