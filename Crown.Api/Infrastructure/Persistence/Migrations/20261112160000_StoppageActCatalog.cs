using System;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261112160000_StoppageActCatalog")]
    public class StoppageActCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoppageActItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoppageActItems", x => x.Id);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "ActItemId",
                table: "StoppageRemarks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "StoppageRemarks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "StoppageRemarks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoppageActItems_IsActive",
                table: "StoppageActItems",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_StoppageActItems_SortOrder",
                table: "StoppageActItems",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_StoppageRemarks_ActItemId",
                table: "StoppageRemarks",
                column: "ActItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoppageRemarks_StoppageActItems_ActItemId",
                table: "StoppageRemarks",
                column: "ActItemId",
                principalTable: "StoppageActItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoppageRemarks_StoppageActItems_ActItemId",
                table: "StoppageRemarks");

            migrationBuilder.DropTable(
                name: "StoppageActItems");

            migrationBuilder.DropIndex(
                name: "IX_StoppageRemarks_ActItemId",
                table: "StoppageRemarks");

            migrationBuilder.DropColumn(
                name: "ActItemId",
                table: "StoppageRemarks");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "StoppageRemarks");

            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "StoppageRemarks");
        }
    }
}
