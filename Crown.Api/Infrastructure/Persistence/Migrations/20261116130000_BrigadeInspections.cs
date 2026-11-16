using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrigadeInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrigadeInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Workshop = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BrigadeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrigadeNumberSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MasterPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    MasterName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Well = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CheckerPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CheckerPosition = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FixDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FixDateIsManual = table.Column<bool>(type: "boolean", nullable: false),
                    FixDateManualText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    StopPoint = table.Column<bool>(type: "boolean", nullable: false),
                    HasExplanation = table.Column<bool>(type: "boolean", nullable: false),
                    DepartmentUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FixResponsibleText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FixReportText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FixResponsiblePersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    FixResponsibleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrigadeInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrigadeInspections_AspNetUsers_BrigadeUserId",
                        column: x => x.BrigadeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrigadeInspections_AspNetUsers_DepartmentUserId",
                        column: x => x.DepartmentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BrigadeInspections_PersonnelPeople_CheckerPersonId",
                        column: x => x.CheckerPersonId,
                        principalTable: "PersonnelPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BrigadeInspections_PersonnelPeople_FixResponsiblePersonId",
                        column: x => x.FixResponsiblePersonId,
                        principalTable: "PersonnelPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BrigadeInspections_PersonnelPeople_MasterPersonId",
                        column: x => x.MasterPersonId,
                        principalTable: "PersonnelPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BrigadeInspectionRemarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrigadeInspectionRemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrigadeInspectionRemarks_BrigadeInspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "BrigadeInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrigadeInspectionRemarks_StoppageActItems_ActItemId",
                        column: x => x.ActItemId,
                        principalTable: "StoppageActItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspectionRemarks_ActItemId",
                table: "BrigadeInspectionRemarks",
                column: "ActItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspectionRemarks_InspectionId",
                table: "BrigadeInspectionRemarks",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspections_BrigadeUserId",
                table: "BrigadeInspections",
                column: "BrigadeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspections_CheckerPersonId",
                table: "BrigadeInspections",
                column: "CheckerPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspections_DepartmentUserId",
                table: "BrigadeInspections",
                column: "DepartmentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspections_FixResponsiblePersonId",
                table: "BrigadeInspections",
                column: "FixResponsiblePersonId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspections_Kind_SequenceNo",
                table: "BrigadeInspections",
                columns: new[] { "Kind", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspections_MasterPersonId",
                table: "BrigadeInspections",
                column: "MasterPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_BrigadeInspections_ReportDate",
                table: "BrigadeInspections",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrigadeInspectionRemarks");

            migrationBuilder.DropTable(
                name: "BrigadeInspections");
        }
    }
}
