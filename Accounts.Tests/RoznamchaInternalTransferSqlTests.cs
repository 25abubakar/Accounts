using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Accounts.Tests;

/// <summary>Opt-in tests use an isolated LocalDB database: schema only, never live financial rows.</summary>
public sealed class RoznamchaInternalTransferSqlTests(InternalTransferSqlFixture fixture) : IClassFixture<InternalTransferSqlFixture>
{
    [InternalTransferSqlFact]
    public async Task Actual_SQL_pair_is_separated_in_payment_and_receipt_grids_and_reloads_after_edit_delete()
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var service = RoznamchaInternalTransferTests.Service(db);
        var payment = await service.CreateAsync(1, RoznamchaInternalTransferTests.Request(1000));
        var reader = new AccountsRoznamchaService(db);
        var date = new DateOnly(2026, 10, 19);
        var payments = await reader.ListPaymentRozAsync(1, date, date);
        var receipts = await reader.ListReceiptRozAsync(1, date, date);
        Assert.Single(payments); Assert.Single(receipts);
        Assert.Equal(-1000, payments[0].Debit); Assert.Equal(1000, receipts[0].Debit);
        Assert.Equal("Payment", payments[0].TypeName); Assert.Equal("Receipt", receipts[0].TypeName);
        Assert.Equal(payments[0].InternalTransferGroupId, receipts[0].InternalTransferGroupId);
        Assert.Equal("Cash", payments[0].AccountName); Assert.Equal("Bank", receipts[0].AccountName);
        var unified = await reader.ListRoznamchaAsync(1, date, date, null);
        Assert.Equal(2, unified.Count);
        Assert.All(unified, row => Assert.Equal(payment.InternalTransferGroupId, row.InternalTransferGroupId));

        // A configured billing category must not hide the receiving side of a transfer.
        db.AccountsModuleSettings.Add(new AccountsModuleSettings { TenantId = 1, BillingCategoryId = 1 });
        await db.SaveChangesAsync();
        Assert.Single(await reader.ListReceiptRozAsync(1, date, date));
        await service.UpdateAsync(payment.Id, 1, RoznamchaInternalTransferTests.Request(2500, 2));
        await using (var reload = fixture.Context())
        {
            var rows = await reload.RoznamchaEntries.AsNoTracking().ToListAsync();
            Assert.Equal(-2500, rows.Single(x => x.RoznamchaTypeId == 1).UsdAmount);
            Assert.Equal(2500, rows.Single(x => x.RoznamchaTypeId == 2).UsdAmount);
        }
        await service.SoftDeleteAsync(receipts[0].Id, "Cancel pair");
        Assert.Empty(await reader.ListPaymentRozAsync(1, date, date));
        Assert.Empty(await reader.ListReceiptRozAsync(1, date, date));
        await using var final = fixture.Context();
        Assert.All(await final.RoznamchaEntries.ToListAsync(), x => Assert.True(x.IsDeleted));
    }

    [InternalTransferSqlTheory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task SQL_transaction_rolls_back_both_sides_if_a_later_audit_save_fails(string operation)
    {
        await fixture.ResetAsync();
        var fail = new FailAuditSave();
        await using var db = fixture.Context(fail);
        var service = RoznamchaInternalTransferTests.Service(db);
        long id = 0;
        if (operation != "create") id = (await service.CreateAsync(1, RoznamchaInternalTransferTests.Request(100))).Id;
        fail.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == "create") await service.CreateAsync(1, RoznamchaInternalTransferTests.Request(100));
            else if (operation == "update") await service.UpdateAsync(id, 1, RoznamchaInternalTransferTests.Request(200));
            else await service.SoftDeleteAsync(id);
        });
        await using var check = fixture.Context();
        var rows = await check.RoznamchaEntries.AsNoTracking().ToListAsync();
        if (operation == "create")
        {
            Assert.Empty(rows); Assert.Empty(await check.RoznamchaEntryProcessLogs.ToListAsync());
        }
        else
        {
            Assert.Equal(2, rows.Count); Assert.All(rows, x => Assert.False(x.IsDeleted));
            Assert.Equal(-100, rows.Single(x => x.RoznamchaTypeId == 1).Amount);
            Assert.Equal(100, rows.Single(x => x.RoznamchaTypeId == 2).Amount);
            Assert.Equal(2, await check.RoznamchaEntryProcessLogs.CountAsync());
        }
    }

    [InternalTransferSqlFact]
    public async Task A_stale_update_cannot_overwrite_a_newer_transfer_pair()
    {
        await fixture.ResetAsync();
        await using var first = fixture.Context();
        var payment = await RoznamchaInternalTransferTests.Service(first).CreateAsync(1, RoznamchaInternalTransferTests.Request(100));
        await using var stale = fixture.Context();
        await stale.RoznamchaEntries.ToListAsync();
        await RoznamchaInternalTransferTests.Service(first).UpdateAsync(payment.Id, 1, RoznamchaInternalTransferTests.Request(200));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            RoznamchaInternalTransferTests.Service(stale).UpdateAsync(payment.Id, 1, RoznamchaInternalTransferTests.Request(300)));
        await using var check = fixture.Context();
        Assert.Equal(-200, (await check.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 1)).Amount);
        Assert.Equal(200, (await check.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 2)).Amount);
    }

    private sealed class FailAuditSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<RoznamchaEntryProcessLog>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Intentional isolated SQL test failure after ledger rows were saved.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

public sealed class InternalTransferSqlFactAttribute : FactAttribute
{
    public InternalTransferSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ACCOUNTS_TEST_TRANSFER_SQL") != "1")
            Skip = "Set ACCOUNTS_TEST_TRANSFER_SQL=1 and ACCOUNTS_TEST_SQL_CONNECTION for isolated LocalDB tests.";
    }
}

public sealed class InternalTransferSqlTheoryAttribute : TheoryAttribute
{
    public InternalTransferSqlTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("ACCOUNTS_TEST_TRANSFER_SQL") != "1")
            Skip = "Set ACCOUNTS_TEST_TRANSFER_SQL=1 and ACCOUNTS_TEST_SQL_CONNECTION for isolated LocalDB tests.";
    }
}

public sealed class InternalTransferSqlFixture : IAsyncLifetime
{
    private readonly string name = "AccountsTransferTests_" + Guid.NewGuid().ToString("N");
    private string? connectionString;
    private string? masterString;
    private bool created;
    private static string Q(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("ACCOUNTS_TEST_TRANSFER_SQL") != "1") return;
        var source = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION"));
        if (!source.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Isolated transfer tests are restricted to LocalDB, never a production server.");
        var sourceDatabase = source.InitialCatalog;
        var master = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = "master" };
        masterString = master.ConnectionString;
        await using var server = new SqlConnection(masterString);
        await server.OpenAsync();
        await ExecuteAsync(server, $"CREATE DATABASE {Q(name)}");
        created = true;
        connectionString = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = name }.ConnectionString;
        try
        {
            await using var target = new SqlConnection(connectionString);
            await target.OpenAsync();
            await ExecuteAsync(target, "CREATE SCHEMA PlatformSettings;");
            string[] tables = ["dbo.RoznamchaEntries", "dbo.AccountsCategories", "dbo.AccountsChartAccounts", "dbo.AccountsRoznamchaTypes", "dbo.AccountsTransTypes", "dbo.AccountsTransModes", "dbo.AccountsCurrencies", "dbo.AccountsTaxTypes", "dbo.AccountsProjects", "dbo.AccountsModuleSettings", "dbo.PaymentRoznamcha", "dbo.RoznamchaLibraryDocuments", "dbo.LibraryDocuments", "dbo.RoznamchaEntryProcessLogs", "PlatformSettings.Processes", "PlatformSettings.Statuses", "PlatformSettings.ProcessStatuses", "PlatformSettings.Colors"];
            foreach (var table in tables)
            {
                var parts = table.Split('.'); var qualified = Q(parts[0]) + "." + Q(parts[1]);
                // TOP(0) copies column/identity shape only. No user data is copied.
                await ExecuteAsync(target, $"SELECT TOP(0) * INTO {qualified} FROM {Q(sourceDatabase)}.{qualified};");
                await using var defaults = new SqlCommand($"SELECT c.name,d.definition FROM {Q(sourceDatabase)}.sys.default_constraints d JOIN {Q(sourceDatabase)}.sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id JOIN {Q(sourceDatabase)}.sys.tables t ON t.object_id=c.object_id JOIN {Q(sourceDatabase)}.sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@Schema AND t.name=@Table AND c.name<>N'Id'", server);
                defaults.Parameters.AddWithValue("@Schema", parts[0]); defaults.Parameters.AddWithValue("@Table", parts[1]);
                var definitions = new List<(string Column, string Definition)>();
                await using (var reader = await defaults.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) definitions.Add((reader.GetString(0), reader.GetString(1)));
                foreach (var item in definitions) await ExecuteAsync(target, $"ALTER TABLE {qualified} ADD DEFAULT {item.Definition} FOR {Q(item.Column)};");
            }
            await ExecuteAsync(target, "CREATE UNIQUE INDEX IX_Test_TransferSide ON dbo.RoznamchaEntries(TenantId,InternalTransferGroupId,RoznamchaTypeId) WHERE InternalTransferGroupId IS NOT NULL;");
            foreach (var module in new[] { "dbo.vw_AccountsChartAccountHierarchy", "dbo.vw_RoznamchaToAccountCategory", "dbo.usp_Accounts_PaymentRoz_List", "dbo.usp_Accounts_ReceiptRoz_List", "dbo.usp_Accounts_Roznamcha_List" })
            {
                await using var command = new SqlCommand($"SELECT definition FROM {Q(sourceDatabase)}.sys.sql_modules WHERE object_id=OBJECT_ID(@Name)", server);
                command.Parameters.AddWithValue("@Name", sourceDatabase + "." + module);
                var definition = (string?)await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Source module missing: " + module);
                await ExecuteAsync(target, definition);
            }
            await SeedTableAsync(target, "dbo.AccountsCategories", "Id,TenantId,Name,Code", "(1,1,N'Cash',N'CASH'),(2,1,N'Banks',N'BANK')");
            await SeedTableAsync(target, "dbo.AccountsChartAccounts", "Id,TenantId,CategoryId,AccountName,AccountNumber", "(1,1,1,N'Cash',N'A-1'),(2,1,2,N'Bank',N'A-2'),(3,1,2,N'Other Bank',N'A-3')");
            await SeedTableAsync(target, "dbo.AccountsRoznamchaTypes", "Id,Code,Name", "(1,N'PAYMENT',N'Payment'),(2,N'RECEIPT',N'Receipt')");
            await SeedTableAsync(target, "dbo.AccountsTransTypes", "Id,Code,Name", "(1,N'PAYMENT',N'Payment'),(8,N'INTERNAL_TRANSFER',N'Internal Transfer')");
            await SeedTableAsync(target, "dbo.AccountsCurrencies", "Id,Code,Name", "(1,N'RS',N'Rupee'),(2,N'USD',N'US Dollar')");
        }
        catch { await DisposeAsync(); throw; }
    }

    public ApplicationDbContext Context(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString!).AddInterceptors(interceptors).Options;
        return new ApplicationDbContext(options, new RoznamchaInternalTransferTests.TestTenant());
    }
    public async Task ResetAsync()
    {
        if (!created || !name.StartsWith("AccountsTransferTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Only isolated test database can be reset.");
        await using var target = new SqlConnection(connectionString); await target.OpenAsync();
        await ExecuteAsync(target, "DELETE dbo.RoznamchaEntryProcessLogs; DELETE dbo.RoznamchaLibraryDocuments; DELETE dbo.RoznamchaEntries; DELETE dbo.AccountsModuleSettings;");
    }
    private static Task SeedTableAsync(SqlConnection connection, string table, string columns, string values) =>
        ExecuteAsync(connection, $"IF COLUMNPROPERTY(OBJECT_ID(N'{table}'),'Id','IsIdentity')=1 SET IDENTITY_INSERT {table} ON; INSERT {table}({columns}) VALUES {values}; IF COLUMNPROPERTY(OBJECT_ID(N'{table}'),'Id','IsIdentity')=1 SET IDENTITY_INSERT {table} OFF;");
    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 }; await command.ExecuteNonQueryAsync();
    }
    public async Task DisposeAsync()
    {
        if (!created) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^AccountsTransferTests_[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test database name.");
        SqlConnection.ClearPool(new SqlConnection(connectionString));
        await using var server = new SqlConnection(masterString); await server.OpenAsync();
        await ExecuteAsync(server, $"ALTER DATABASE {Q(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Q(name)};");
        created = false;
    }
}
