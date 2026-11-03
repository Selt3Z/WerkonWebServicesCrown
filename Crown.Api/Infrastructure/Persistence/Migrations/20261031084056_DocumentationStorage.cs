using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentationStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DocumentationEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "DocumentationFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentationFolders_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentationFolders_DocumentationFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "DocumentationFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentationFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentationFiles_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentationFiles_DocumentationFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "DocumentationFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFiles_FolderId",
                table: "DocumentationFiles",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFiles_OwnerUserId",
                table: "DocumentationFiles",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_OwnerUserId",
                table: "DocumentationFolders",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_OwnerUserId_ParentFolderId_Name",
                table: "DocumentationFolders",
                columns: new[] { "OwnerUserId", "ParentFolderId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationFolders_ParentFolderId",
                table: "DocumentationFolders",
                column: "ParentFolderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentationFiles");

            migrationBuilder.DropTable(
                name: "DocumentationFolders");

            migrationBuilder.DropColumn(
                name: "DocumentationEnabled",
                table: "AspNetUsers");
        }
    }
}
