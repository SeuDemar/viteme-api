using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viteme.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VitemeDbContext))]
    [Migration("20261123120000_RedefinicaoSenha")]
    public partial class RedefinicaoSenha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "token_redefinicao_senha",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_redefinicao_senha", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_redefinicao_senha_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_token_redefinicao_senha_token_hash",
                table: "token_redefinicao_senha",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_redefinicao_senha_usuario_id",
                table: "token_redefinicao_senha",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "token_redefinicao_senha");
        }
    }
}
