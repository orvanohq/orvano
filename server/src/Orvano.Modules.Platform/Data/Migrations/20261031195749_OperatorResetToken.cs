using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orvano.Modules.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperatorResetToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operator_reset_token",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_reset_token", x => x.id);
                    table.CheckConstraint("ck_operator_reset_token_token_hash_length", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_operator_reset_token_operator_operator_id",
                        column: x => x.operator_id,
                        principalSchema: "platform",
                        principalTable: "operator",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operator_reset_token_operator_id",
                schema: "platform",
                table: "operator_reset_token",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_reset_token_token_hash",
                schema: "platform",
                table: "operator_reset_token",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operator_reset_token",
                schema: "platform");
        }
    }
}
