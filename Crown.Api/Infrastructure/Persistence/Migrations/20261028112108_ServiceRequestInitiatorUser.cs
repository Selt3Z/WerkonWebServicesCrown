using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceRequestInitiatorUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InitiatorUserId",
                table: "ServiceRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_InitiatorUserId",
                table: "ServiceRequests",
                column: "InitiatorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceRequests_AspNetUsers_InitiatorUserId",
                table: "ServiceRequests",
                column: "InitiatorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Старые заявки: привязать, если InitiatorName совпал с DisplayName/логином.
            migrationBuilder.Sql(
                """
                UPDATE "ServiceRequests" AS sr
                SET "InitiatorUserId" = u."Id"
                FROM "AspNetUsers" AS u
                WHERE sr."InitiatorUserId" IS NULL
                  AND (
                    u."DisplayName" = sr."InitiatorName"
                    OR u."UserName" = sr."InitiatorName"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceRequests_AspNetUsers_InitiatorUserId",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_InitiatorUserId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "InitiatorUserId",
                table: "ServiceRequests");
        }
    }
}
