using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequestByCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestedEquipmentCategory",
                table: "ServiceRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedSpecialVehicleType",
                table: "ServiceRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedEquipmentCategory",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RequestedSpecialVehicleType",
                table: "ServiceRequests");
        }
    }
}
