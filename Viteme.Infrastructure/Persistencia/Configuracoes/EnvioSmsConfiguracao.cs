using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class EnvioSmsConfiguracao : IEntityTypeConfiguration<EnvioSms>
{
    public void Configure(EntityTypeBuilder<EnvioSms> b)
    {
        b.ToTable("envio_sms", t => t.HasCheckConstraint("ck_envio_sms_tentativas", "tentativas <= 4"));

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Telefone).HasMaxLength(20).IsRequired();
        b.Property(e => e.Tentativas).HasDefaultValue((short)0);
        b.Property(e => e.UltimoErro).HasMaxLength(300);
        b.Ignore(e => e.PodeReenviar);
    }
}
