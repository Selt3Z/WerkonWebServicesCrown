using System;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261112120000_BrigadeAsDepartmentUser")]
    public class BrigadeAsDepartmentUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Старые остановки завязаны на справочник Brigades — сбрасываем (данных в проде ещё нет / мало).
            migrationBuilder.Sql(@"DELETE FROM ""StoppageRemarks"";");
            migrationBuilder.Sql(@"DELETE FROM ""BrigadeStoppages"";");

            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_MasterUserId",
                table: "BrigadeStoppages");

            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_StoppedByUserId",
                table: "BrigadeStoppages");

            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_Brigades_BrigadeId",
                table: "BrigadeStoppages");

            migrationBuilder.DropTable(name: "Brigades");

            migrationBuilder.DropIndex(
                name: "IX_BrigadeStoppages_BrigadeId",
                table: "BrigadeStoppages");

            migrationBuilder.DropIndex(
                name: "IX_BrigadeStoppages_MasterUserId",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "MasterUserId",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "CanEnterBrigadeRating",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "BrigadeId",
                table: "BrigadeStoppages",
                newName: "BrigadeUserId");

            migrationBuilder.AddColumn<string>(
                name: "BrigadeName",
                table: "BrigadeStoppages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_BrigadeUserId",
                table: "BrigadeStoppages",
                column: "BrigadeUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_BrigadeUserId",
                table: "BrigadeStoppages",
                column: "BrigadeUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_StoppedByUserId",
                table: "BrigadeStoppages",
                column: "StoppedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_BrigadeUserId",
                table: "BrigadeStoppages");

            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_StoppedByUserId",
                table: "BrigadeStoppages");

            migrationBuilder.DropIndex(
                name: "IX_BrigadeStoppages_BrigadeUserId",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "BrigadeName",
                table: "BrigadeStoppages");

            migrationBuilder.RenameColumn(
                name: "BrigadeUserId",
                table: "BrigadeStoppages",
                newName: "BrigadeId");

            migrationBuilder.AddColumn<Guid>(
                name: "MasterUserId",
                table: "BrigadeStoppages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanEnterBrigadeRating",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Brigades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MasterUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brigades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Brigades_AspNetUsers_MasterUserId",
                        column: x => x.MasterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Brigades_Code",
                table: "Brigades",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brigades_IsActive",
                table: "Brigades",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Brigades_MasterUserId",
                table: "Brigades",
                column: "MasterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_BrigadeId",
                table: "BrigadeStoppages",
                column: "BrigadeId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_MasterUserId",
                table: "BrigadeStoppages",
                column: "MasterUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_MasterUserId",
                table: "BrigadeStoppages",
                column: "MasterUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_AspNetUsers_StoppedByUserId",
                table: "BrigadeStoppages",
                column: "StoppedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_Brigades_BrigadeId",
                table: "BrigadeStoppages",
                column: "BrigadeId",
                principalTable: "Brigades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
