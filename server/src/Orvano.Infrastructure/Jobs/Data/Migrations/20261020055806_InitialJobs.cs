using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orvano.Infrastructure.Jobs.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "jobs");

            migrationBuilder.CreateTable(
                name: "job",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "text", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    dedupe_key = table.Column<string>(type: "text", nullable: true),
                    trace_parent = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.id);
                    table.CheckConstraint("ck_job_attempt", "attempt >= 0 AND attempt <= max_attempts");
                    table.CheckConstraint("ck_job_dedupe_key", "char_length(dedupe_key) BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_job_finished", "(status IN ('succeeded', 'dead')) = (finished_at IS NOT NULL)");
                    table.CheckConstraint("ck_job_kind", "char_length(kind) BETWEEN 3 AND 100");
                    table.CheckConstraint("ck_job_last_error", "char_length(last_error) <= 2000");
                    table.CheckConstraint("ck_job_lease", "(status = 'running') = (locked_until IS NOT NULL AND locked_by IS NOT NULL)");
                    table.CheckConstraint("ck_job_locked_by", "char_length(locked_by) <= 100");
                    table.CheckConstraint("ck_job_max_attempts", "max_attempts BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_job_status", "\"status\" IN ('queued', 'running', 'succeeded', 'dead')");
                    table.CheckConstraint("ck_job_trace_parent", "char_length(trace_parent) <= 55");
                })
                .Annotation("Npgsql:StorageParameter:autovacuum_vacuum_scale_factor", 0.050000000000000003)
                .Annotation("Npgsql:StorageParameter:fillfactor", 70);

            migrationBuilder.CreateTable(
                name: "schedule",
                schema: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    cron = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule", x => x.id);
                    table.CheckConstraint("ck_schedule_cron", "char_length(cron) BETWEEN 9 AND 100");
                    table.CheckConstraint("ck_schedule_name", "char_length(name) BETWEEN 3 AND 100");
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_due",
                schema: "jobs",
                table: "job",
                columns: new[] { "run_at", "id" },
                filter: "status = 'queued'");

            migrationBuilder.CreateIndex(
                name: "ix_job_finished",
                schema: "jobs",
                table: "job",
                column: "finished_at",
                filter: "status IN ('succeeded', 'dead')");

            migrationBuilder.CreateIndex(
                name: "ix_job_lease",
                schema: "jobs",
                table: "job",
                columns: new[] { "locked_until", "id" },
                filter: "status = 'running'");

            migrationBuilder.CreateIndex(
                name: "ix_job_project",
                schema: "jobs",
                table: "job",
                column: "project_id",
                filter: "project_id IS NOT NULL AND status IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "ux_job_dedupe_key",
                schema: "jobs",
                table: "job",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedule_due",
                schema: "jobs",
                table: "schedule",
                column: "next_run_at",
                filter: "enabled");

            migrationBuilder.CreateIndex(
                name: "ux_schedule_name",
                schema: "jobs",
                table: "schedule",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "schedule",
                schema: "jobs");
        }
    }
}
