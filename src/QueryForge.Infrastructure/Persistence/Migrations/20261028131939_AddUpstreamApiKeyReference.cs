using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUpstreamApiKeyReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHeaderName",
                table: "ApiDefinitions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpstreamSecretName",
                table: "ApiDefinitions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiKeyHeaderName",
                table: "ApiDefinitions");

            migrationBuilder.DropColumn(
                name: "UpstreamSecretName",
                table: "ApiDefinitions");
        }
    }
}
