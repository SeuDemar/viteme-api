using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viteme.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CnpjUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cnpj",
                table: "usuario",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cnpj_verificado_em",
                table: "usuario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "razao_social",
                table: "usuario",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_usuario_cnpj",
                table: "usuario",
                column: "cnpj",
                unique: true,
                filter: "cnpj IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuario_cnpj_formato",
                table: "usuario",
                sql: "cnpj IS NULL OR cnpj ~ '^[A-Z0-9]{12}[0-9]{2}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_usuario_cnpj",
                table: "usuario");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuario_cnpj_formato",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "cnpj",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "cnpj_verificado_em",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "razao_social",
                table: "usuario");
        }
    }
}
