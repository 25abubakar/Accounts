using Accounts.Data;
using Accounts.Services.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Accounts.Tests;

/// <summary>Read-only contract checks against an explicitly selected SQL Server database.</summary>
public sealed class PaymentRozSqlServerTests
{
    [PaymentRozSqlServerFact]
    public async Task Payment_list_includes_and_serializes_configured_status_colors_for_each_imported_row()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION")).Options;
        await using var db = new ApplicationDbContext(options);
        var tenantId = int.Parse(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_TENANT") ?? "2");
        var rows = await new AccountsRoznamchaService(db).ListPaymentRozAsync(
            tenantId, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1));
        var configured = await RoznamchaStatusCatalog.Query(db, "Payment").ToDictionaryAsync(x => x.Id);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        foreach (var row in rows.Where(x => x.StatusId.HasValue))
        {
            var status = configured[row.StatusId!.Value];
            Assert.Equal(status.Name, row.StatusName);
            Assert.Equal(status.ColorCode, row.StatusColorCode);
            Assert.Equal(status.FontColor, row.StatusFontColor);
            Assert.NotNull(row.ProcessStatusId);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, jsonOptions));
            Assert.Equal(status.ColorCode, json.RootElement.GetProperty("statusColorCode").GetString());
            Assert.Equal(status.FontColor, json.RootElement.GetProperty("statusFontColor").GetString());
            Assert.Equal(row.ProcessStatusId, json.RootElement.GetProperty("processStatusId").GetInt32());
        }
    }

    [PaymentRozSqlServerFact]
    public async Task Payment_status_lookup_reads_configured_id_and_process_color_from_SQL_Server()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION")).Options;
        await using var db = new ApplicationDbContext(options);
        var settled = await RoznamchaStatusCatalog.Query(db, "Payment").SingleAsync(x => x.Code == "SETTLED");
        Assert.Equal(20, settled.Id);
        Assert.Equal("Settled", settled.Name);
        Assert.Equal("#2BFA06", settled.ColorCode);
        Assert.Null(settled.FontColor);
    }

    [PaymentRozSqlServerFact]
    public async Task Date_filter_includes_both_boundaries_and_does_not_leak_other_tenants()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION")).Options;
        await using var db = new ApplicationDbContext(options);
        var tenantId = int.Parse(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_TENANT") ?? "2");
        var service = new AccountsRoznamchaService(db);
        var allRows = await service.ListPaymentRozAsync(tenantId, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1));
        var boundaryDates = allRows.Count == 0 ? Array.Empty<DateOnly>() : new[] { allRows.Min(x => x.TransDate), allRows.Max(x => x.TransDate) }.Distinct().ToArray();
        foreach (var date in boundaryDates)
        {
            var dailyRows = await service.ListPaymentRozAsync(tenantId, date, date);
            Assert.Equal(allRows.Where(x => x.TransDate == date).Select(x => x.Id).Order(), dailyRows.Select(x => x.Id).Order());
        }
        Assert.Empty(await service.ListPaymentRozAsync(int.MaxValue, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1)));
    }

    [PaymentRozSqlServerFact]
    public async Task Payment_list_materializes_imported_rows_with_the_complete_contract()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION"))
            .Options;
        await using var db = new ApplicationDbContext(options);
        var tenantId = int.Parse(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_TENANT") ?? "2");
        var rows = await new AccountsRoznamchaService(db).ListPaymentRozAsync(
            tenantId, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1));

        Assert.Equal(rows.Count, rows.Select(x => x.Id).Distinct().Count());
        Assert.All(rows, row => Assert.InRange(row.TransDate, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1)));
        var expectedCount = Environment.GetEnvironmentVariable("ACCOUNTS_TEST_PAYMENT_COUNT");
        if (expectedCount != null) Assert.Equal(int.Parse(expectedCount), rows.Count);
    }
}

public sealed class PaymentRozSqlServerFactAttribute : FactAttribute
{
    public PaymentRozSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION")))
            Skip = "Set ACCOUNTS_TEST_SQL_CONNECTION to run read-only SQL Server contract checks.";
    }
}
