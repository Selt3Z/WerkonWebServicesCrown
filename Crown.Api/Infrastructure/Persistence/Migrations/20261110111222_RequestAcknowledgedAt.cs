using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequestAcknowledgedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcknowledgedAt",
                table: "ServiceRequests",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "ServiceRequests");
        }
    }
}
