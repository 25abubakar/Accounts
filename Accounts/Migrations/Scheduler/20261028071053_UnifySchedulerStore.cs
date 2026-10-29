using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

#nullable disable
namespace Accounts.Migrations.Scheduler;

public partial class UnifySchedulerStore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach(var file in new[]{"20261028_UnifiedSchedulerSchema.sql","20261028_SchedulerCore.sql","20261028_SchedulerApi.sql","20261028_SchedulerEngine.sql","20261028_SchedulerPlan.sql"})
        {
            var assembly=typeof(UnifySchedulerStore).Assembly;
            var resource=assembly.GetManifestResourceNames().Single(name=>name.EndsWith("."+file,StringComparison.Ordinal));
            using var stream=assembly.GetManifestResourceStream(resource)??throw new InvalidOperationException("Missing scheduler migration SQL.");
            using var reader=new StreamReader(stream);
            foreach(var batch in Regex.Split(reader.ReadToEnd(),@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
                if(!string.IsNullOrWhiteSpace(batch))migrationBuilder.Sql(batch);
        }
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException(
        "This data-preserving consolidation retires the legacy scheduler/Quartz schema. Stop API and worker, restore the verified pre-migration SQL backup and matching application build to roll back. Automatic destructive downgrade is intentionally blocked.");
}
