using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ServiceRequestExecutorUser : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExecutorName",
            table: "ServiceRequests",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<Guid>(
            name: "ExecutorUserId",
            table: "ServiceRequests",
            type: "uuid",
            nullable: true);

        // Переносим имя службы в снимок исполнителя (пользователя ещё нет).
        migrationBuilder.Sql(
            """
            UPDATE "ServiceRequests" AS sr
            SET "ExecutorName" = COALESCE(d."Name", '—')
            FROM "ServiceDepartments" AS d
            WHERE sr."ServiceDepartmentId" = d."Id"
              AND (sr."ExecutorName" IS NULL OR sr."ExecutorName" = '');
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_ServiceRequests_ServiceDepartments_ServiceDepartmentId",
            table: "ServiceRequests");

        migrationBuilder.DropIndex(
            name: "IX_ServiceRequests_ServiceDepartmentId",
            table: "ServiceRequests");

        migrationBuilder.DropColumn(
            name: "ServiceDepartmentId",
            table: "ServiceRequests");

        migrationBuilder.DropTable(
            name: "ServiceDepartments");

        migrationBuilder.CreateIndex(
            name: "IX_ServiceRequests_ExecutorUserId",
            table: "ServiceRequests",
            column: "ExecutorUserId");

        migrationBuilder.AddForeignKey(
            name: "FK_ServiceRequests_AspNetUsers_ExecutorUserId",
            table: "ServiceRequests",
            column: "ExecutorUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ServiceRequests_AspNetUsers_ExecutorUserId",
            table: "ServiceRequests");

        migrationBuilder.DropIndex(
            name: "IX_ServiceRequests_ExecutorUserId",
            table: "ServiceRequests");

        migrationBuilder.DropColumn(
            name: "ExecutorName",
            table: "ServiceRequests");

        migrationBuilder.DropColumn(
            name: "ExecutorUserId",
            table: "ServiceRequests");

        migrationBuilder.CreateTable(
            name: "ServiceDepartments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceDepartments", x => x.Id);
            });

        migrationBuilder.AddColumn<Guid>(
            name: "ServiceDepartmentId",
            table: "ServiceRequests",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.CreateIndex(
            name: "IX_ServiceRequests_ServiceDepartmentId",
            table: "ServiceRequests",
            column: "ServiceDepartmentId");

        migrationBuilder.CreateIndex(
            name: "IX_ServiceDepartments_Name",
            table: "ServiceDepartments",
            column: "Name",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_ServiceRequests_ServiceDepartments_ServiceDepartmentId",
            table: "ServiceRequests",
            column: "ServiceDepartmentId",
            principalTable: "ServiceDepartments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
