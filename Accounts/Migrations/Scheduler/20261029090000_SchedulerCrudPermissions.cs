using System.Text.RegularExpressions;
using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations.Scheduler;

[DbContext(typeof(SchedulerDbContext))]
[Migration("20261029090000_SchedulerCrudPermissions")]
public sealed class SchedulerCrudPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string file = "20261029_SchedulerCrudPermissions.sql";
        var assembly = typeof(SchedulerCrudPermissions).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith('.' + file, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing scheduler CRUD permission migration SQL.");
        using var reader = new StreamReader(stream);
        foreach (var batch in Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) migrationBuilder.Sql(batch);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Scheduler soft-delete and delegated CRUD authorization are active security controls; restore a verified backup to downgrade.");
}
