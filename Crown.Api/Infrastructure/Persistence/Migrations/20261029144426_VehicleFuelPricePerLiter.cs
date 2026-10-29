using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VehicleFuelPricePerLiter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PricePerLiter",
                table: "VehicleFuelRefuels",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Старые записи: цена/л = стоимость / литры.
            migrationBuilder.Sql(
                """
                UPDATE "VehicleFuelRefuels"
                SET "PricePerLiter" = ROUND(("Cost" / "Liters")::numeric, 2)
                WHERE "Liters" > 0 AND "PricePerLiter" = 0;
                """);

            // Галочку «Активна» убрали — вернуть «призраков» в парк.
            migrationBuilder.Sql(
                """
                UPDATE "Vehicles" SET "IsActive" = TRUE WHERE "IsActive" = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricePerLiter",
                table: "VehicleFuelRefuels");
        }
    }
}
