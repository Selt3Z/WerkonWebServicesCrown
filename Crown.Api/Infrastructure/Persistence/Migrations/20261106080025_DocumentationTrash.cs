using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentationTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentationFolders_OwnerUserId_ParentFolderId_Name",
                table: "DocumentationFolders");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "DocumentationFolders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "DocumentationFolders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "DocumentationFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "DocumentationFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_DeletedAt",
                table: "DocumentationFolders",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_OwnerUserId_ParentFolderId_Name",
                table: "DocumentationFolders",
                columns: new[] { "OwnerUserId", "ParentFolderId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFiles_DeletedAt",
                table: "DocumentationFiles",
                column: "DeletedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentationFolders_DeletedAt",
                table: "DocumentationFolders");

            migrationBuilder.DropIndex(
                name: "IX_DocumentationFolders_OwnerUserId_ParentFolderId_Name",
                table: "DocumentationFolders");

            migrationBuilder.DropIndex(
                name: "IX_DocumentationFiles_DeletedAt",
                table: "DocumentationFiles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DocumentationFolders");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "DocumentationFolders");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DocumentationFiles");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "DocumentationFiles");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_OwnerUserId_ParentFolderId_Name",
                table: "DocumentationFolders",
                columns: new[] { "OwnerUserId", "ParentFolderId", "Name" });
        }
    }
}
