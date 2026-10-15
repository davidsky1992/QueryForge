using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueryForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AuthenticationType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DocumentationUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiDefinitions_Name",
                table: "ApiDefinitions",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ApiDefinitions_Slug",
                table: "ApiDefinitions",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiDefinitions_Status",
                table: "ApiDefinitions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiDefinitions");
        }
    }
}
