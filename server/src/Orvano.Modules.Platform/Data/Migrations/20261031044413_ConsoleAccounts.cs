using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orvano.Modules.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsoleAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operator",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    email_normalized = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: true),
                    last_sign_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator", x => x.id);
                    table.CheckConstraint("ck_operator_email_length", "char_length(email) <= 254");
                    table.CheckConstraint("ck_operator_locale_length", "char_length(locale) <= 35");
                    table.CheckConstraint("ck_operator_name_length", "char_length(name) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_operator_status", "\"status\" IN ('active', 'disabled')");
                });

            migrationBuilder.CreateTable(
                name: "organization",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization", x => x.id);
                    table.CheckConstraint("ck_organization_name_length", "char_length(name) BETWEEN 1 AND 128");
                });

            migrationBuilder.CreateTable(
                name: "operator_session",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    csrf_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_reauth_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_session", x => x.id);
                    table.CheckConstraint("ck_operator_session_csrf_token_length", "octet_length(csrf_token) = 32");
                    table.CheckConstraint("ck_operator_session_token_hash_length", "octet_length(token_hash) = 32");
                    table.CheckConstraint("ck_operator_session_user_agent_length", "char_length(user_agent) <= 512");
                    table.ForeignKey(
                        name: "fk_operator_session_operator_operator_id",
                        column: x => x.operator_id,
                        principalSchema: "platform",
                        principalTable: "operator",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership", x => x.id);
                    table.CheckConstraint("ck_membership_role", "role IN ('owner')");
                    table.ForeignKey(
                        name: "fk_membership_operator_operator_id",
                        column: x => x.operator_id,
                        principalSchema: "platform",
                        principalTable: "operator",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_membership_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    deletion_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.id);
                    table.CheckConstraint("ck_project_deletion_requested_at", "(status = 'deleting') = (deletion_requested_at IS NOT NULL)");
                    table.CheckConstraint("ck_project_name_length", "char_length(name) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_project_status", "\"status\" IN ('creating', 'active', 'deleting')");
                    table.ForeignKey(
                        name: "fk_project_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "platform",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_key",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    prefix = table.Column<string>(type: "text", nullable: false),
                    last4 = table.Column<string>(type: "text", nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    created_by_operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_key", x => x.id);
                    table.CheckConstraint("ck_api_key_hash_length", "octet_length(hash) = 32");
                    table.CheckConstraint("ck_api_key_last4_length", "char_length(last4) = 4");
                    table.CheckConstraint("ck_api_key_name_length", "char_length(name) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_api_key_prefix_length", "char_length(prefix) = 15");
                    table.CheckConstraint("ck_api_key_scopes_present", "cardinality(scopes) >= 1");
                    table.CheckConstraint("ck_api_key_scopes_publishable", "type <> 'publishable' OR scopes <@ ARRAY['client.auth', 'client.auth.signup', 'client.databases', 'client.functions', 'client.realtime', 'client.storage']::text[]");
                    table.CheckConstraint("ck_api_key_scopes_secret", "type <> 'secret' OR scopes <@ ARRAY['databases.read', 'databases.write', 'functions.execute', 'functions.read', 'functions.write', 'messaging.read', 'messaging.write', 'realtime.subscribe', 'sites.read', 'sites.write', 'storage.read', 'storage.write', 'users.read', 'users.write']::text[]");
                    table.CheckConstraint("ck_api_key_type", "\"type\" IN ('publishable', 'secret')");
                    table.ForeignKey(
                        name: "fk_api_key_operator_created_by_operator_id",
                        column: x => x.created_by_operator_id,
                        principalSchema: "platform",
                        principalTable: "operator",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_api_key_project_project_id",
                        column: x => x.project_id,
                        principalSchema: "platform",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_settings",
                schema: "platform",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allowed_origins = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_settings", x => x.project_id);
                    table.CheckConstraint("ck_project_settings_allowed_origins_count", "cardinality(allowed_origins) <= 100");
                    table.ForeignKey(
                        name: "fk_project_settings_project_project_id",
                        column: x => x.project_id,
                        principalSchema: "platform",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_key_created_by_operator_id",
                schema: "platform",
                table: "api_key",
                column: "created_by_operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_key_hash",
                schema: "platform",
                table: "api_key",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_key_project_id_id",
                schema: "platform",
                table: "api_key",
                columns: new[] { "project_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_membership_operator_id",
                schema: "platform",
                table: "membership",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_organization_id_operator_id",
                schema: "platform",
                table: "membership",
                columns: new[] { "organization_id", "operator_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operator_email_normalized",
                schema: "platform",
                table: "operator",
                column: "email_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operator_session_operator_id",
                schema: "platform",
                table: "operator_session",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_session_token_hash",
                schema: "platform",
                table: "operator_session",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_organization_id_id",
                schema: "platform",
                table: "project",
                columns: new[] { "organization_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_key",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "membership",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "operator_session",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "project_settings",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "operator",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "project",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "organization",
                schema: "platform");
        }
    }
}
