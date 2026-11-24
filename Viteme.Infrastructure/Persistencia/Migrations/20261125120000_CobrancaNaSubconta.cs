using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viteme.Infrastructure.Persistencia.Migrations
{
    /// <summary>Fase 3 do Asaas: a cobrança com garantia nasce na subconta; o pagamento guarda qual conta cobrou.</summary>
    [DbContext(typeof(VitemeDbContext))]
    [Migration("20261125120000_CobrancaNaSubconta")]
    public partial class CobrancaNaSubconta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "conta_recebimento_id",
                table: "pagamento",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_conta_recebimento_id",
                table: "pagamento",
                column: "conta_recebimento_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pagamento_conta_recebimento_conta_recebimento_id",
                table: "pagamento",
                column: "conta_recebimento_id",
                principalTable: "conta_recebimento",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pagamento_conta_recebimento_conta_recebimento_id",
                table: "pagamento");

            migrationBuilder.DropIndex(
                name: "ix_pagamento_conta_recebimento_id",
                table: "pagamento");

            migrationBuilder.DropColumn(
                name: "conta_recebimento_id",
                table: "pagamento");
        }
    }
}
