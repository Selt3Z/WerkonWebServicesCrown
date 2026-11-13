using System;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261113130000_StoppedByPersonId")]
    public class StoppedByPersonId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StoppedByPersonId",
                table: "BrigadeStoppages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_StoppedByPersonId",
                table: "BrigadeStoppages",
                column: "StoppedByPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_PersonnelPeople_StoppedByPersonId",
                table: "BrigadeStoppages",
                column: "StoppedByPersonId",
                principalTable: "PersonnelPeople",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_PersonnelPeople_StoppedByPersonId",
                table: "BrigadeStoppages");

            migrationBuilder.DropIndex(
                name: "IX_BrigadeStoppages_StoppedByPersonId",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "StoppedByPersonId",
                table: "BrigadeStoppages");
        }
    }
}
