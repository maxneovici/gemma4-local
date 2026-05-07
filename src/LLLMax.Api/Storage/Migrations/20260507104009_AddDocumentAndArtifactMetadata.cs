using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLLMax.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentAndArtifactMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS background_job_artifacts (
                    id TEXT NOT NULL PRIMARY KEY,
                    job_id TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    title TEXT NOT NULL,
                    content_type TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    bytes INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS documents (
                    id TEXT NOT NULL PRIMARY KEY,
                    file_name TEXT NOT NULL,
                    stored_file_name TEXT NOT NULL,
                    path TEXT NOT NULL,
                    bytes INTEGER NOT NULL,
                    content_type TEXT NULL,
                    created_at TEXT NOT NULL
                );
                """);
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_background_job_artifacts_created_at ON background_job_artifacts (created_at);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_background_job_artifacts_job_id ON background_job_artifacts (job_id);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_documents_created_at ON documents (created_at);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_job_artifacts");

            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
