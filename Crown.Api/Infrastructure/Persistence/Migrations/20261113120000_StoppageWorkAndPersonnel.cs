using System;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261113120000_StoppageWorkAndPersonnel")]
    public class StoppageWorkAndPersonnel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoppageWorkItems",
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
                    table.PrimaryKey("PK_StoppageWorkItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonnelPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonnelPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonnelPeople_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Существующие остановки остаются «вручную» (defaultValue: true).
            migrationBuilder.AddColumn<Guid>(
                name: "WorkItemId",
                table: "BrigadeStoppages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWorkManual",
                table: "BrigadeStoppages",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MasterPersonId",
                table: "BrigadeStoppages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoppageWorkItems_IsActive",
                table: "StoppageWorkItems",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_StoppageWorkItems_SortOrder",
                table: "StoppageWorkItems",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelPeople_FullName",
                table: "PersonnelPeople",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelPeople_IsActive",
                table: "PersonnelPeople",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelPeople_OwnerUserId",
                table: "PersonnelPeople",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_WorkItemId",
                table: "BrigadeStoppages",
                column: "WorkItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_MasterPersonId",
                table: "BrigadeStoppages",
                column: "MasterPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_StoppageWorkItems_WorkItemId",
                table: "BrigadeStoppages",
                column: "WorkItemId",
                principalTable: "StoppageWorkItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BrigadeStoppages_PersonnelPeople_MasterPersonId",
                table: "BrigadeStoppages",
                column: "MasterPersonId",
                principalTable: "PersonnelPeople",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_StoppageWorkItems_WorkItemId",
                table: "BrigadeStoppages");

            migrationBuilder.DropForeignKey(
                name: "FK_BrigadeStoppages_PersonnelPeople_MasterPersonId",
                table: "BrigadeStoppages");

            migrationBuilder.DropIndex(
                name: "IX_BrigadeStoppages_WorkItemId",
                table: "BrigadeStoppages");

            migrationBuilder.DropIndex(
                name: "IX_BrigadeStoppages_MasterPersonId",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "WorkItemId",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "IsWorkManual",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "MasterPersonId",
                table: "BrigadeStoppages");

            migrationBuilder.DropTable(
                name: "StoppageWorkItems");

            migrationBuilder.DropTable(
                name: "PersonnelPeople");
        }
    }
}
