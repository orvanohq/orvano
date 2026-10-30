using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orvano.Modules.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "instance",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    setup_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    setup_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    setup_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance", x => x.id);
                    table.CheckConstraint("ck_instance_id", "id = 1");
                });

            // Spec 0016: the install's one row, setup open and no token yet. now() is right here: a migration has no
            // TimeProvider, and the row is made once per install.
            migrationBuilder.Sql(
                "INSERT INTO platform.instance (id, created_at, updated_at) VALUES (1, now(), now()) ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance",
                schema: "platform");
        }
    }
}
