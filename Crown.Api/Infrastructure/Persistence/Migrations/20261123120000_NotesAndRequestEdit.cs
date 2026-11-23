using System;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261123120000_NotesAndRequestEdit")]
    public class NotesAndRequestEdit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BrigadeStoppages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BrigadeInspections",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEditReason",
                table: "ServiceRequests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEditedAt",
                table: "ServiceRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastEditedByUserId",
                table: "ServiceRequests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "BrigadeStoppages");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "BrigadeInspections");

            migrationBuilder.DropColumn(
                name: "LastEditReason",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "LastEditedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "LastEditedByUserId",
                table: "ServiceRequests");
        }
    }
}
