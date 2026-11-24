using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viteme.Infrastructure.Persistencia.Migrations
{
    /// <summary>Fase 2/3 do Asaas: subconta do organizador (conta_recebimento) e garantia do pagamento.</summary>
    [DbContext(typeof(VitemeDbContext))]
    [Migration("20261124150000_ContaRecebimento")]
    public partial class ContaRecebimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "garantia_id",
                table: "pagamento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "garantia_liberada_em",
                table: "pagamento",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conta_recebimento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_gateway_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    wallet_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    chave_api_cifrada = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    url_onboarding = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    aprovada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    garantia_habilitada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conta_recebimento", x => x.id);
                    table.CheckConstraint("ck_conta_recebimento_situacao", "situacao IN ('PENDENTE', 'APROVADA', 'REJEITADA')");
                    table.ForeignKey(
                        name: "fk_conta_recebimento_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conta_recebimento_conta_gateway_id",
                table: "conta_recebimento",
                column: "conta_gateway_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conta_recebimento_usuario_id",
                table: "conta_recebimento",
                column: "usuario_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conta_recebimento");

            migrationBuilder.DropColumn(
                name: "garantia_id",
                table: "pagamento");

            migrationBuilder.DropColumn(
                name: "garantia_liberada_em",
                table: "pagamento");
        }
    }
}
