using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viteme.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VitemeDbContext))]
    [Migration("20261124120000_CpfUsuario")]
    public partial class CpfUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cpf",
                table: "usuario",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuario_cpf_formato",
                table: "usuario",
                sql: "cpf IS NULL OR cpf ~ '^[0-9]{11}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_usuario_cpf_formato",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "cpf",
                table: "usuario");
        }
    }
}
