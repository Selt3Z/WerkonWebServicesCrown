using System;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261123140000_DocumentationLibraries")]
    public class DocumentationLibraries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentationLibraries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    InboxFolderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentationLibraryMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanWrite = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationLibraryMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentationLibraryMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentationLibraryMembers_DocumentationLibraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "DocumentationLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationLibraries_InboxFolderKey",
                table: "DocumentationLibraries",
                column: "InboxFolderKey");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationLibraries_IsActive",
                table: "DocumentationLibraries",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationLibraries_SortOrder",
                table: "DocumentationLibraries",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationLibraryMembers_LibraryId_UserId",
                table: "DocumentationLibraryMembers",
                columns: new[] { "LibraryId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationLibraryMembers_UserId",
                table: "DocumentationLibraryMembers",
                column: "UserId");

            // Старые unique по OwnerUserId → потом LibraryId.
            migrationBuilder.DropIndex(
                name: "IX_DocumentationFolders_OwnerUserId_ParentFolderId_Name",
                table: "DocumentationFolders");

            migrationBuilder.DropIndex(
                name: "IX_DocumentationFiles_OwnerUserId_InboxSourceKey",
                table: "DocumentationFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentationFolders_AspNetUsers_OwnerUserId",
                table: "DocumentationFolders");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentationFiles_AspNetUsers_OwnerUserId",
                table: "DocumentationFiles");

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "DocumentationFolders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "DocumentationFiles",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryId",
                table: "DocumentationFolders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryId",
                table: "DocumentationFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentationFolders_AspNetUsers_OwnerUserId",
                table: "DocumentationFolders",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentationFiles_AspNetUsers_OwnerUserId",
                table: "DocumentationFiles",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentationFolders_DocumentationLibraries_LibraryId",
                table: "DocumentationFolders",
                column: "LibraryId",
                principalTable: "DocumentationLibraries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentationFiles_DocumentationLibraries_LibraryId",
                table: "DocumentationFiles",
                column: "LibraryId",
                principalTable: "DocumentationLibraries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_LibraryId",
                table: "DocumentationFolders",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_LibraryId_ParentFolderId_Name",
                table: "DocumentationFolders",
                columns: new[] { "LibraryId", "ParentFolderId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL AND \"LibraryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFiles_LibraryId",
                table: "DocumentationFiles",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFiles_LibraryId_InboxSourceKey",
                table: "DocumentationFiles",
                columns: new[] { "LibraryId", "InboxSourceKey" },
                unique: true,
                filter: "\"InboxSourceKey\" IS NOT NULL AND \"DeletedAt\" IS NULL AND \"LibraryId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentationFiles_LibraryId_InboxSourceKey",
                table: "DocumentationFiles");

            migrationBuilder.DropIndex(
                name: "IX_DocumentationFiles_LibraryId",
                table: "DocumentationFiles");

            migrationBuilder.DropIndex(
                name: "IX_DocumentationFolders_LibraryId_ParentFolderId_Name",
                table: "DocumentationFolders");

            migrationBuilder.DropIndex(
                name: "IX_DocumentationFolders_LibraryId",
                table: "DocumentationFolders");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentationFiles_DocumentationLibraries_LibraryId",
                table: "DocumentationFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentationFolders_DocumentationLibraries_LibraryId",
                table: "DocumentationFolders");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentationFiles_AspNetUsers_OwnerUserId",
                table: "DocumentationFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentationFolders_AspNetUsers_OwnerUserId",
                table: "DocumentationFolders");

            migrationBuilder.DropColumn(
                name: "LibraryId",
                table: "DocumentationFiles");

            migrationBuilder.DropColumn(
                name: "LibraryId",
                table: "DocumentationFolders");

            // Перед NOT NULL — заглушка для строк без владельца (редко после миграции).
            migrationBuilder.Sql(
                """
                DELETE FROM "DocumentationFiles" WHERE "OwnerUserId" IS NULL;
                DELETE FROM "DocumentationFolders" WHERE "OwnerUserId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "DocumentationFiles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "DocumentationFolders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentationFolders_AspNetUsers_OwnerUserId",
                table: "DocumentationFolders",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentationFiles_AspNetUsers_OwnerUserId",
                table: "DocumentationFiles",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_OwnerUserId_ParentFolderId_Name",
                table: "DocumentationFolders",
                columns: new[] { "OwnerUserId", "ParentFolderId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFiles_OwnerUserId_InboxSourceKey",
                table: "DocumentationFiles",
                columns: new[] { "OwnerUserId", "InboxSourceKey" },
                unique: true,
                filter: "\"InboxSourceKey\" IS NOT NULL AND \"DeletedAt\" IS NULL");

            migrationBuilder.DropTable(
                name: "DocumentationLibraryMembers");

            migrationBuilder.DropTable(
                name: "DocumentationLibraries");
        }
    }
}
