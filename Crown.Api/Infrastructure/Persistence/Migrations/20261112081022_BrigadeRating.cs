using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrigadeRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanEnterBrigadeRating",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewBrigadeRating",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Brigades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MasterUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brigades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Brigades_AspNetUsers_MasterUserId",
                        column: x => x.MasterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BrigadeStoppages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrigadeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MasterUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MasterName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StoppedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoppedByName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StoppedAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    RestartedAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Hours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    WorkDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrigadeStoppages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrigadeStoppages_AspNetUsers_MasterUserId",
                        column: x => x.MasterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BrigadeStoppages_AspNetUsers_StoppedByUserId",
                        column: x => x.StoppedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BrigadeStoppages_Brigades_BrigadeId",
                        column: x => x.BrigadeId,
                        principalTable: "Brigades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoppageRemarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrigadeStoppageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoppageRemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoppageRemarks_BrigadeStoppages_BrigadeStoppageId",
                        column: x => x.BrigadeStoppageId,
                        principalTable: "BrigadeStoppages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Brigades_Code",
                table: "Brigades",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brigades_IsActive",
                table: "Brigades",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Brigades_MasterUserId",
                table: "Brigades",
                column: "MasterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_BrigadeId",
                table: "BrigadeStoppages",
                column: "BrigadeId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_MasterUserId",
                table: "BrigadeStoppages",
                column: "MasterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_ReportDate",
                table: "BrigadeStoppages",
                column: "ReportDate");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeStoppages_StoppedByUserId",
                table: "BrigadeStoppages",
                column: "StoppedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoppageRemarks_BrigadeStoppageId",
                table: "StoppageRemarks",
                column: "BrigadeStoppageId");

            migrationBuilder.CreateIndex(
                name: "IX_StoppageRemarks_IsResolved",
                table: "StoppageRemarks",
                column: "IsResolved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoppageRemarks");

            migrationBuilder.DropTable(
                name: "BrigadeStoppages");

            migrationBuilder.DropTable(
                name: "Brigades");

            migrationBuilder.DropColumn(
                name: "CanEnterBrigadeRating",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CanViewBrigadeRating",
                table: "AspNetUsers");
        }
    }
}
