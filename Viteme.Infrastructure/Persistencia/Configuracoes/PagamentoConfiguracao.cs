using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Configuracoes;

public class PagamentoConfiguracao : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> b)
    {
        b.ToTable("pagamento", t => t.HasCheckConstraint("ck_pagamento_valor", "valor > 0"));

        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Valor).HasPrecision(10, 2);
        b.Property(p => p.Metodo).HasMaxLength(30);
        b.Property(p => p.IdTransacaoGateway).HasMaxLength(100);
        b.Property(p => p.UrlPagamento).HasMaxLength(500);
        b.Property(p => p.Versao).IsRowVersion();

        // RI07: UNIQUE aceita vários nulos, então o pagamento é gravado antes de o gateway devolver o id.
        b.HasIndex(p => p.IdTransacaoGateway).IsUnique();
        b.HasIndex(p => p.InscricaoId);
    }
}
