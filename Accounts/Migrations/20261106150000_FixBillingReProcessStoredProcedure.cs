using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261106150000_FixBillingReProcessStoredProcedure")]
public sealed class FixBillingReProcessStoredProcedure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_ReProcess_List.sql");
        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_PatientMaster_List.sql");
    }

    private static void ApplyEmbeddedProcedure(MigrationBuilder migrationBuilder, string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded SQL resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$",
                     System.Text.RegularExpressions.RegexOptions.Multiline |
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) migrationBuilder.Sql(batch);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

