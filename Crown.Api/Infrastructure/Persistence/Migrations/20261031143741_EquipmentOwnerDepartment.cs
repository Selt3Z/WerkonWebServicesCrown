using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentOwnerDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerLabel",
                table: "EquipmentAssets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "EquipmentAssets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssets_OwnerUserId",
                table: "EquipmentAssets",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentAssets_AspNetUsers_OwnerUserId",
                table: "EquipmentAssets",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentAssets_AspNetUsers_OwnerUserId",
                table: "EquipmentAssets");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentAssets_OwnerUserId",
                table: "EquipmentAssets");

            migrationBuilder.DropColumn(
                name: "OwnerLabel",
                table: "EquipmentAssets");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "EquipmentAssets");
        }
    }
}
