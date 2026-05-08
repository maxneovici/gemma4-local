using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLLMax.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ExpandUserProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "communication_style",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "constraints",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "family_and_relations",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "goals",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "interests",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "work",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "communication_style",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "constraints",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "family_and_relations",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "goals",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "interests",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "location",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "work",
                table: "user_profiles");
        }
    }
}
