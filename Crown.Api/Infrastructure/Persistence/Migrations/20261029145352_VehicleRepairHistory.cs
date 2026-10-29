using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VehicleRepairHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleRepairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleRepairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleRepairs_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRepairs_StartedAt",
                table: "VehicleRepairs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleRepairs_VehicleId",
                table: "VehicleRepairs",
                column: "VehicleId");

            // Уже стоящие «в ремонте» — открытый период с текущего момента.
            migrationBuilder.Sql(
                """
                INSERT INTO "VehicleRepairs" ("Id", "VehicleId", "StartedAt", "EndedAt", "Note", "CreatedByUserId", "CreatedAt")
                SELECT gen_random_uuid(), v."Id", NOW(), NULL, NULL, NULL, NOW()
                FROM "Vehicles" v
                WHERE v."IsInRepair" = TRUE
                  AND NOT EXISTS (
                      SELECT 1 FROM "VehicleRepairs" r
                      WHERE r."VehicleId" = v."Id" AND r."EndedAt" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleRepairs");
        }
    }
}
