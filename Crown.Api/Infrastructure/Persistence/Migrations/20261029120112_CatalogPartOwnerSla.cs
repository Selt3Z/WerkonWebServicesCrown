using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogPartOwnerSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Старый общий каталог запчастей → per-user услуги; старые строки без владельца сбрасываем.
            migrationBuilder.Sql("""DELETE FROM "CatalogParts";""");

            migrationBuilder.AlterColumn<decimal>(
                name: "DefaultUnitPrice",
                table: "CatalogParts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "CatalogParts",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "SlaHours",
                table: "CatalogParts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogParts_OwnerUserId",
                table: "CatalogParts",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogParts_AspNetUsers_OwnerUserId",
                table: "CatalogParts",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatalogParts_AspNetUsers_OwnerUserId",
                table: "CatalogParts");

            migrationBuilder.DropIndex(
                name: "IX_CatalogParts_OwnerUserId",
                table: "CatalogParts");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "CatalogParts");

            migrationBuilder.DropColumn(
                name: "SlaHours",
                table: "CatalogParts");

            migrationBuilder.AlterColumn<decimal>(
                name: "DefaultUnitPrice",
                table: "CatalogParts",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);
        }
    }
}
