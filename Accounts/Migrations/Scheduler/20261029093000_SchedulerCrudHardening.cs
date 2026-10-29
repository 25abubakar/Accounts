using System.Text.RegularExpressions;
using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations.Scheduler;

[DbContext(typeof(SchedulerDbContext))]
[Migration("20261029093000_SchedulerCrudHardening")]
public sealed class SchedulerCrudHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var file in new[]
                 {
                     "20261029_SchedulerCrudPermissions.sql",
                     "20261028_SchedulerApi.sql",
                     "20261028_SchedulerPlan.sql"
                 })
        {
            var assembly = typeof(SchedulerCrudHardening).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith('.' + file, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing scheduler hardening SQL resource {file}.");
            using var reader = new StreamReader(stream);
            foreach (var batch in Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) migrationBuilder.Sql(batch);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Scheduler active-job guards are security controls; restore a verified backup to downgrade.");
}
