using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026170000_AddPayrollShiftEligibility")]
public sealed class AddPayrollShiftEligibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("PayrollShiftLookupValueId","PersonHrProfiles",type:"int",nullable:true);
        migrationBuilder.AddColumn<DateOnly>("PayrollShiftEffectiveFrom","PersonHrProfiles",type:"date",nullable:true);
        migrationBuilder.CreateIndex("IX_PersonHrProfiles_PayrollShiftLookupValueId","PersonHrProfiles","PayrollShiftLookupValueId");
        migrationBuilder.AddForeignKey("FK_PersonHrProfiles_AppLookupValues_PayrollShiftLookupValueId","PersonHrProfiles",
            "PayrollShiftLookupValueId","AppLookupValues",principalColumn:"LookupValueId");
        using var stream=typeof(AddPayrollShiftEligibility).Assembly.GetManifestResourceStream(
            "Accounts.Migrations.Sql.20261026170000_GenerateMonthly.sql")!;
        using var reader=new StreamReader(stream);
        foreach(var batch in Regex.Split(reader.ReadToEnd(),@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
            if(!string.IsNullOrWhiteSpace(batch))migrationBuilder.Sql(batch);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Export payroll shift eligibility and restore reviewed procedures before rollback.");
}
