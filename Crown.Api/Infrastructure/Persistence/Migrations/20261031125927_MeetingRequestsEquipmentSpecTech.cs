using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MeetingRequestsEquipmentSpecTech : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Роль Initiator → Department (NormalizedName как Identity).
            migrationBuilder.Sql(
                """
                UPDATE "AspNetRoles"
                SET "Name" = 'Department',
                    "NormalizedName" = 'DEPARTMENT'
                WHERE "Name" = 'Initiator' OR "NormalizedName" = 'INITIATOR';
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "EquipmentAssetId",
                table: "ServiceRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsUrgent",
                table: "ServiceRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ServiceRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SpecArrivedAt",
                table: "ServiceRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecOperation",
                table: "ServiceRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SpecPlannedAt",
                table: "ServiceRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SpecReleasedAt",
                table: "ServiceRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpecialVehicleUnitId",
                table: "ServiceRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EquipmentAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InventoryNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CurrentLocationUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentLocationLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Availability = table.Column<int>(type: "integer", nullable: false),
                    ReservedByRequestId = table.Column<long>(type: "bigint", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentAssets_AspNetUsers_CurrentLocationUserId",
                        column: x => x.CurrentLocationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SpecialVehicleUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlateOrNote = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Availability = table.Column<int>(type: "integer", nullable: false),
                    ReservedByRequestId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialVehicleUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromLocationUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromLocationLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToLocationUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ServiceRequestId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ByUserDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentMovements_EquipmentAssets_EquipmentAssetId",
                        column: x => x.EquipmentAssetId,
                        principalTable: "EquipmentAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_EquipmentAssetId",
                table: "ServiceRequests",
                column: "EquipmentAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_IsUrgent",
                table: "ServiceRequests",
                column: "IsUrgent");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Kind",
                table: "ServiceRequests",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_SpecialVehicleUnitId",
                table: "ServiceRequests",
                column: "SpecialVehicleUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssets_Availability",
                table: "EquipmentAssets",
                column: "Availability");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssets_CurrentLocationUserId",
                table: "EquipmentAssets",
                column: "CurrentLocationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssets_InventoryNumber",
                table: "EquipmentAssets",
                column: "InventoryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssets_IsArchived",
                table: "EquipmentAssets",
                column: "IsArchived");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMovements_EquipmentAssetId",
                table: "EquipmentMovements",
                column: "EquipmentAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMovements_OccurredAt",
                table: "EquipmentMovements",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMovements_ServiceRequestId",
                table: "EquipmentMovements",
                column: "ServiceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SpecialVehicleUnits_Availability",
                table: "SpecialVehicleUnits",
                column: "Availability");

            migrationBuilder.CreateIndex(
                name: "IX_SpecialVehicleUnits_IsActive",
                table: "SpecialVehicleUnits",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SpecialVehicleUnits_Name",
                table: "SpecialVehicleUnits",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceRequests_EquipmentAssets_EquipmentAssetId",
                table: "ServiceRequests",
                column: "EquipmentAssetId",
                principalTable: "EquipmentAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceRequests_SpecialVehicleUnits_SpecialVehicleUnitId",
                table: "ServiceRequests",
                column: "SpecialVehicleUnitId",
                principalTable: "SpecialVehicleUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceRequests_EquipmentAssets_EquipmentAssetId",
                table: "ServiceRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceRequests_SpecialVehicleUnits_SpecialVehicleUnitId",
                table: "ServiceRequests");

            migrationBuilder.DropTable(
                name: "EquipmentMovements");

            migrationBuilder.DropTable(
                name: "SpecialVehicleUnits");

            migrationBuilder.DropTable(
                name: "EquipmentAssets");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_EquipmentAssetId",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_IsUrgent",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_Kind",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_SpecialVehicleUnitId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "EquipmentAssetId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "IsUrgent",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "SpecArrivedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "SpecOperation",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "SpecPlannedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "SpecReleasedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "SpecialVehicleUnitId",
                table: "ServiceRequests");

            migrationBuilder.Sql(
                """
                UPDATE "AspNetRoles"
                SET "Name" = 'Initiator',
                    "NormalizedName" = 'INITIATOR'
                WHERE "Name" = 'Department' OR "NormalizedName" = 'DEPARTMENT';
                """);
        }
    }
}
