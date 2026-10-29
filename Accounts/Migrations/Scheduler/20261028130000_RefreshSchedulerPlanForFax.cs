using System.Text.RegularExpressions;
using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations.Scheduler;

[DbContext(typeof(SchedulerDbContext))]
[Migration("20261028130000_RefreshSchedulerPlanForFax")]
public sealed class RefreshSchedulerPlanForFax : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string file = "20261028_SchedulerPlan.sql";
        var assembly = typeof(RefreshSchedulerPlanForFax).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith('.' + file, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing scheduler plan migration SQL.");
        using var reader = new StreamReader(stream);

        foreach (var batch in Regex.Split(
                     reader.ReadToEnd(),
                     @"^\s*GO\s*$",
                     RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                migrationBuilder.Sql(batch);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "The scheduler plan is data preserving and shared by active jobs; restore a verified database backup to downgrade it.");
}
