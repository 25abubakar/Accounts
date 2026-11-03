using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261102153000_EnforceRoznamchaUpdateQueueScope")]
public sealed class EnforceRoznamchaUpdateQueueScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var assembly = typeof(EnforceRoznamchaUpdateQueueScope).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Accounts.Sql.StoredProcedures.usp_Accounts_RoznamchaUpdate_List.sql")
            ?? throw new InvalidOperationException("Missing Roznamcha Update procedure resource.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd()
            .Replace("\r\nGO\r\n", "\r\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\nGO\n", "\n", StringComparison.OrdinalIgnoreCase);
        migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The previous procedure also read the same physical table. Retaining
        // the safely narrowed queue is preferable to exposing unrelated rows
        // during a rollback.
    }
}
