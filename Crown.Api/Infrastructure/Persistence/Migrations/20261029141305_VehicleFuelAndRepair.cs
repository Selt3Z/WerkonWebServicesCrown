using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VehicleFuelAndRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInRepair",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Vin",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VehicleFuelRefuels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefueledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Liters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    FuelType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleFuelRefuels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleFuelRefuels_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_IsInRepair",
                table: "Vehicles",
                column: "IsInRepair");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleFuelRefuels_RefueledAt",
                table: "VehicleFuelRefuels",
                column: "RefueledAt");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleFuelRefuels_VehicleId",
                table: "VehicleFuelRefuels",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleFuelRefuels");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_IsInRepair",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "IsInRepair",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Vin",
                table: "Vehicles");
        }
    }
}
