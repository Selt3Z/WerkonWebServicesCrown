using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crown.Api.Infrastructure.Persistence.Migrations;

/// <summary>
/// Индексы под нагрузку поиска: AuditLog.UserId + pg_trgm для ILIKE/Contains.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261119160000_PerfSearchIndexes")]
public class PerfSearchIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_AuditLogEntries_UserId",
            table: "AuditLogEntries",
            column: "UserId");

        // GIN trigram — ускоряет ILIKE '%…%' / Contains на больших таблицах.
        migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_DocumentationFiles_FileName_trgm"
            ON "DocumentationFiles" USING gin ("FileName" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_Body_trgm"
            ON "ServiceRequests" USING gin ("Body" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_StoppageActItems_Text_trgm"
            ON "StoppageActItems" USING gin ("Text" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_StoppageWorkItems_Text_trgm"
            ON "StoppageWorkItems" USING gin ("Text" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_PersonnelPeople_FullName_trgm"
            ON "PersonnelPeople" USING gin ("FullName" gin_trgm_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_PersonnelPeople_FullName_trgm";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_StoppageWorkItems_Text_trgm";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_StoppageActItems_Text_trgm";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ServiceRequests_Body_trgm";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_DocumentationFiles_FileName_trgm";""");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogEntries_UserId",
            table: "AuditLogEntries");
    }
}
