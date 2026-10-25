using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Viteme.Domain.Enums;

#nullable disable

namespace Viteme.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:motivo_denuncia", "EVENTO_FALSO,GOLPE,CONTEUDO_IMPROPRIO,SPAM,OUTRO")
                .Annotation("Npgsql:Enum:status_evento", "RASCUNHO,PUBLICADO,OCULTO,CANCELADO,ENCERRADO")
                .Annotation("Npgsql:Enum:status_inscricao", "PENDENTE_PAGAMENTO,CONFIRMADA,CANCELADA,EXPIRADA")
                .Annotation("Npgsql:Enum:status_pagamento", "PENDENTE,APROVADO,RECUSADO,ESTORNADO,EXPIRADO")
                .Annotation("Npgsql:Enum:status_sms", "PENDENTE,ENVIADO,FALHOU")
                .Annotation("Npgsql:Enum:tipo_notificacao", "INSCRICAO,PAGAMENTO,ALTERACAO,LEMBRETE,CANCELAMENTO,COLABORACAO,DENUNCIA");

            migrationBuilder.CreateTable(
                name: "usuario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    senha_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    telefone_verificado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    aceita_sms = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    aceite_sms_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tentativas_login = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    bloqueado_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario", x => x.id);
                    table.CheckConstraint("ck_usuario_sms_exige_telefone_verificado", "NOT aceita_sms OR telefone_verificado");
                    table.CheckConstraint("ck_usuario_telefone_e164", "telefone IS NULL OR telefone ~ '^\\+[1-9][0-9]{7,14}$'");
                });

            migrationBuilder.CreateTable(
                name: "codigo_verificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tentativas = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    utilizado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigo_verificacao", x => x.id);
                    table.CheckConstraint("ck_codigo_verificacao_tentativas", "tentativas <= 5");
                    table.ForeignKey(
                        name: "fk_codigo_verificacao_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    local = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    imagem_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    data_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    inscricao_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    inscricao_fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    capacidade = table.Column<int>(type: "integer", nullable: true),
                    preco = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    carga_horaria = table.Column<short>(type: "smallint", nullable: true),
                    status = table.Column<StatusEvento>(type: "status_evento", nullable: false, defaultValueSql: "'RASCUNHO'::status_evento"),
                    reaberto_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evento", x => x.id);
                    table.CheckConstraint("ck_evento_capacidade", "capacidade IS NULL OR capacidade > 0");
                    table.CheckConstraint("ck_evento_carga_horaria", "carga_horaria IS NULL OR carga_horaria > 0");
                    table.CheckConstraint("ck_evento_data_fim", "data_fim > data_inicio");
                    table.CheckConstraint("ck_evento_inscricao_fim_ate_inicio", "inscricao_fim <= data_inicio");
                    table.CheckConstraint("ck_evento_inscricao_periodo", "inscricao_fim > inscricao_inicio");
                    table.CheckConstraint("ck_evento_preco", "preco >= 0");
                    table.ForeignKey(
                        name: "fk_evento_usuarios_organizador_id",
                        column: x => x.organizador_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "token_renovacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revogado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_renovacao", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_renovacao_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alteracao_evento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_novo = table.Column<string>(type: "text", nullable: true),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alteracao_evento", x => x.id);
                    table.ForeignKey(
                        name: "fk_alteracao_evento_eventos_evento_id",
                        column: x => x.evento_id,
                        principalTable: "evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alteracao_evento_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "atividade_programacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    palestrante = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    local = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_atividade_programacao", x => x.id);
                    table.CheckConstraint("ck_atividade_programacao_fim", "fim > inicio");
                    table.ForeignKey(
                        name: "fk_atividade_programacao_eventos_evento_id",
                        column: x => x.evento_id,
                        principalTable: "evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "colaborador_evento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adicionado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_colaborador_evento", x => x.id);
                    table.ForeignKey(
                        name: "fk_colaborador_evento_eventos_evento_id",
                        column: x => x.evento_id,
                        principalTable: "evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_colaborador_evento_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "denuncia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<MotivoDenuncia>(type: "motivo_denuncia", nullable: false),
                    descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_denuncia", x => x.id);
                    table.CheckConstraint("ck_denuncia_descricao_outro", "motivo <> 'OUTRO' OR descricao IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_denuncia_eventos_evento_id",
                        column: x => x.evento_id,
                        principalTable: "evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_denuncia_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inscricao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<StatusInscricao>(type: "status_inscricao", nullable: false),
                    codigo_checkin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    reservada_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    presenca_confirmada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    checkin_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inscricao", x => x.id);
                    table.ForeignKey(
                        name: "fk_inscricao_evento_evento_id",
                        column: x => x.evento_id,
                        principalTable: "evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inscricao_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "interesse_evento",
                columns: table => new
                {
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interesse_evento", x => new { x.usuario_id, x.evento_id });
                    table.ForeignKey(
                        name: "fk_interesse_evento_evento_evento_id",
                        column: x => x.evento_id,
                        principalTable: "evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_interesse_evento_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<TipoNotificacao>(type: "tipo_notificacao", nullable: false),
                    titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    lida = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificacao", x => x.id);
                    table.ForeignKey(
                        name: "fk_notificacao_evento_evento_id",
                        column: x => x.evento_id,
                        principalTable: "evento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_notificacao_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "certificado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inscricao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_validacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    emitido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificado", x => x.id);
                    table.ForeignKey(
                        name: "fk_certificado_inscricoes_inscricao_id",
                        column: x => x.inscricao_id,
                        principalTable: "inscricao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pagamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inscricao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<StatusPagamento>(type: "status_pagamento", nullable: false),
                    metodo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    id_transacao_gateway = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    url_pagamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pagamento", x => x.id);
                    table.CheckConstraint("ck_pagamento_valor", "valor > 0");
                    table.ForeignKey(
                        name: "fk_pagamento_inscricao_inscricao_id",
                        column: x => x.inscricao_id,
                        principalTable: "inscricao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "envio_sms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notificacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<StatusSms>(type: "status_sms", nullable: false),
                    tentativas = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    ultimo_erro = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    enviado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envio_sms", x => x.id);
                    table.CheckConstraint("ck_envio_sms_tentativas", "tentativas <= 4");
                    table.ForeignKey(
                        name: "fk_envio_sms_notificacoes_notificacao_id",
                        column: x => x.notificacao_id,
                        principalTable: "notificacao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alteracao_evento_autor_id",
                table: "alteracao_evento",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "ix_alteracao_evento_evento_id_criado_em",
                table: "alteracao_evento",
                columns: new[] { "evento_id", "criado_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_atividade_programacao_evento_id",
                table: "atividade_programacao",
                column: "evento_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificado_codigo_validacao",
                table: "certificado",
                column: "codigo_validacao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificado_inscricao_id",
                table: "certificado",
                column: "inscricao_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_codigo_verificacao_usuario_id",
                table: "codigo_verificacao",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_colaborador_evento_evento_id_usuario_id",
                table: "colaborador_evento",
                columns: new[] { "evento_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_colaborador_evento_usuario_id",
                table: "colaborador_evento",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_denuncia_evento_id_criado_em",
                table: "denuncia",
                columns: new[] { "evento_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_denuncia_evento_id_usuario_id",
                table: "denuncia",
                columns: new[] { "evento_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_denuncia_usuario_id",
                table: "denuncia",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_envio_sms_notificacao_id",
                table: "envio_sms",
                column: "notificacao_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evento_organizador_id",
                table: "evento",
                column: "organizador_id");

            migrationBuilder.CreateIndex(
                name: "ix_evento_status_data_inicio",
                table: "evento",
                columns: new[] { "status", "data_inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_inscricao_codigo_checkin",
                table: "inscricao",
                column: "codigo_checkin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inscricao_evento_id_status",
                table: "inscricao",
                columns: new[] { "evento_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_inscricao_reservada_ate_pendente",
                table: "inscricao",
                column: "reservada_ate",
                filter: "status = 'PENDENTE_PAGAMENTO'");

            migrationBuilder.CreateIndex(
                name: "ix_inscricao_usuario_id",
                table: "inscricao",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_inscricao_ativa_por_usuario_evento",
                table: "inscricao",
                columns: new[] { "evento_id", "usuario_id" },
                unique: true,
                filter: "status IN ('PENDENTE_PAGAMENTO', 'CONFIRMADA')");

            migrationBuilder.CreateIndex(
                name: "ix_interesse_evento_evento_id",
                table: "interesse_evento",
                column: "evento_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacao_evento_id",
                table: "notificacao",
                column: "evento_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacao_usuario_id_lida_criado_em",
                table: "notificacao",
                columns: new[] { "usuario_id", "lida", "criado_em" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_id_transacao_gateway",
                table: "pagamento",
                column: "id_transacao_gateway",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pagamento_inscricao_id",
                table: "pagamento",
                column: "inscricao_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_renovacao_token_hash",
                table: "token_renovacao",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_renovacao_usuario_id",
                table: "token_renovacao",
                column: "usuario_id");

            // RI02: e-mail único sem diferenciar maiúsculas (o EF não modela índices por expressão).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_usuario_email_lower ON usuario (lower(email));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_usuario_email_lower;");

            migrationBuilder.DropTable(
                name: "alteracao_evento");

            migrationBuilder.DropTable(
                name: "atividade_programacao");

            migrationBuilder.DropTable(
                name: "certificado");

            migrationBuilder.DropTable(
                name: "codigo_verificacao");

            migrationBuilder.DropTable(
                name: "colaborador_evento");

            migrationBuilder.DropTable(
                name: "denuncia");

            migrationBuilder.DropTable(
                name: "envio_sms");

            migrationBuilder.DropTable(
                name: "interesse_evento");

            migrationBuilder.DropTable(
                name: "pagamento");

            migrationBuilder.DropTable(
                name: "token_renovacao");

            migrationBuilder.DropTable(
                name: "notificacao");

            migrationBuilder.DropTable(
                name: "inscricao");

            migrationBuilder.DropTable(
                name: "evento");

            migrationBuilder.DropTable(
                name: "usuario");
        }
    }
}
