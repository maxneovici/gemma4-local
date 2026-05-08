using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLLMax.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantIdentityToUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assistant_description",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "assistant_name",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assistant_description",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "assistant_name",
                table: "user_profiles");
        }
    }
}
