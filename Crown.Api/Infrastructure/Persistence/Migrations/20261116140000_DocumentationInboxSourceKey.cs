using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261116140000_DocumentationInboxSourceKey")]
    public class DocumentationInboxSourceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InboxSourceKey",
                table: "DocumentationFiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFiles_OwnerUserId_InboxSourceKey",
                table: "DocumentationFiles",
                columns: new[] { "OwnerUserId", "InboxSourceKey" },
                unique: true,
                filter: "\"InboxSourceKey\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentationFiles_OwnerUserId_InboxSourceKey",
                table: "DocumentationFiles");

            migrationBuilder.DropColumn(
                name: "InboxSourceKey",
                table: "DocumentationFiles");
        }
    }
}
