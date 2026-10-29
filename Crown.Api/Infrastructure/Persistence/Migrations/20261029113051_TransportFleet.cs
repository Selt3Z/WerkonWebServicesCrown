using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransportFleet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParkingLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingLots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CurrentParkingId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_ParkingLots_CurrentParkingId",
                        column: x => x.CurrentParkingId,
                        principalTable: "ParkingLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTrips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BorrowerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BorrowerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PlannedStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlannedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsedHours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    LateMinutes = table.Column<int>(type: "integer", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OdometerStart = table.Column<int>(type: "integer", nullable: true),
                    OdometerEnd = table.Column<int>(type: "integer", nullable: true),
                    FuelStart = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    FuelEnd = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    ReturnParkingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTrips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleTrips_AspNetUsers_BorrowerUserId",
                        column: x => x.BorrowerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleTrips_ParkingLots_ReturnParkingId",
                        column: x => x.ReturnParkingId,
                        principalTable: "ParkingLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VehicleTrips_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParkingLots_IsActive",
                table: "ParkingLots",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingLots_Name",
                table: "ParkingLots",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_CurrentParkingId",
                table: "Vehicles",
                column: "CurrentParkingId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_IsActive",
                table: "Vehicles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_PlateNumber",
                table: "Vehicles",
                column: "PlateNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTrips_BorrowerUserId",
                table: "VehicleTrips",
                column: "BorrowerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTrips_PlannedEndAt",
                table: "VehicleTrips",
                column: "PlannedEndAt");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTrips_PlannedStartAt",
                table: "VehicleTrips",
                column: "PlannedStartAt");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTrips_ReturnParkingId",
                table: "VehicleTrips",
                column: "ReturnParkingId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTrips_Status",
                table: "VehicleTrips",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTrips_VehicleId",
                table: "VehicleTrips",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleTrips");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "ParkingLots");
        }
    }
}
