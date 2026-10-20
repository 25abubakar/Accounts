using Accounts.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Accounts.Tests;

public sealed class RoznamchaDateFilterPlanTests
{
    [Fact]
    public void Hierarchy_lookups_are_materialized_once_and_scoped_to_the_requested_tenant()
    {
        var operations = new MaterializeRoznamchaAccountLookups().UpOperations;
        Assert.Equal(3, operations.Count);
        foreach (var operation in operations)
        {
            var sql = Assert.IsType<SqlOperation>(operation).Sql;
            Assert.Contains("WITH RECOMPILE\nAS", sql);
            Assert.Contains("FROM dbo.vw_AccountsChartAccountHierarchy WHERE TenantId=@TenantId", sql);
            Assert.Contains("FROM dbo.vw_RoznamchaToAccountCategory WHERE TenantId=@TenantId", sql);
            Assert.Contains("LEFT JOIN @RozAccountHierarchy fromAcct", sql);
            Assert.Contains("LEFT JOIN @RozToAccountCategory toMap", sql);
            Assert.DoesNotContain("LEFT JOIN dbo.vw_AccountsChartAccountHierarchy", sql);
            Assert.Contains("e.InternalTransferGroupId", sql);
            Assert.Contains("StatusColorCode", sql);
        }
    }

    [Fact]
    public void Migration_recompiles_all_three_reports_without_changing_financial_data()
    {
        var migration = new RecompileRoznamchaDateFilterPlans();
        var operations = migration.UpOperations;
        Assert.Equal(3, operations.Count);
        foreach (var operation in operations)
        {
            var sql = Assert.IsType<SqlOperation>(operation).Sql;
            Assert.Contains("WITH RECOMPILE\nAS", sql);
            Assert.Contains("e.TenantId=@TenantId", sql);
            Assert.Contains("e.TransDate>=@DateFrom", sql);
            Assert.Contains("e.TransDate<=@DateTo", sql);
            Assert.Contains("e.InternalTransferGroupId", sql);
            Assert.Contains("StatusColorCode", sql);
            Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        }
        Assert.All(migration.DownOperations, operation =>
            Assert.DoesNotContain("WITH RECOMPILE", Assert.IsType<SqlOperation>(operation).Sql));
    }
}
