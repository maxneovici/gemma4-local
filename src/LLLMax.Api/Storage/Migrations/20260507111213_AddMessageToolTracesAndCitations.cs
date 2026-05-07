using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLLMax.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageToolTracesAndCitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE session_messages ADD COLUMN citations_json TEXT NULL;
                """, suppressTransaction: true);
            migrationBuilder.Sql("""
                ALTER TABLE session_messages ADD COLUMN tool_traces_json TEXT NULL;
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "citations_json",
                table: "session_messages");

            migrationBuilder.DropColumn(
                name: "tool_traces_json",
                table: "session_messages");
        }
    }
}
