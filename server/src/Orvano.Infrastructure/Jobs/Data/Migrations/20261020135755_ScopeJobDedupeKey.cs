using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orvano.Infrastructure.Jobs.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeJobDedupeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_job_dedupe_key",
                schema: "jobs",
                table: "job");

            migrationBuilder.CreateIndex(
                name: "ux_job_dedupe_key",
                schema: "jobs",
                table: "job",
                columns: new[] { "kind", "project_id", "dedupe_key" },
                unique: true,
                filter: "dedupe_key IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_job_dedupe_key",
                schema: "jobs",
                table: "job");

            migrationBuilder.CreateIndex(
                name: "ux_job_dedupe_key",
                schema: "jobs",
                table: "job",
                column: "dedupe_key",
                unique: true);
        }
    }
}
