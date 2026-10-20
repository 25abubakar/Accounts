using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Tests;

public sealed class PaymentWorkbookStatusTests
{
    [PaymentWorkbookFact]
    public async Task Supplied_workbook_preserves_configured_settled_status_and_duplicate_protection()
    {
        // Production masters are read only; transaction writes use an isolated in-memory database.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION")).Options;
        await using var source = new ApplicationDbContext(options);
        var tenantId = int.Parse(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_TENANT") ?? "2");
        var tenant = new TestTenant(tenantId);
        var targetOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var target = new ApplicationDbContext(targetOptions, tenant);
        await target.Database.EnsureCreatedAsync();
        async Task Copy<T>(IQueryable<T> rows) where T : class
        {
            target.AddRange(await rows.AsNoTracking().ToListAsync());
            await target.SaveChangesAsync();
            target.ChangeTracker.Clear();
        }
        await Copy(source.AccountsCategories.IgnoreQueryFilters().Where(x => x.TenantId == tenantId));
        await Copy(source.AccountsChartAccounts.IgnoreQueryFilters().Where(x => x.TenantId == tenantId));
        await Copy(source.AccountsCurrencies);
        await Copy(source.AccountsRoznamchaTypes);
        await Copy(source.AccountsTransTypes);
        await Copy(source.AccountsTransModes);
        await Copy(source.AccountsProjects.IgnoreQueryFilters().Where(x => x.TenantId == tenantId));
        await Copy(source.PlatformSettingProcesses);
        await Copy(source.PlatformSettingStatuses);
        await Copy(source.PlatformSettingColors);
        await Copy(source.PlatformSettingProcessStatuses);

        var service = new RoznamchaService(target, new TestUser(tenantId), new TestReferences(), new UnusedFiles());
        var path = Environment.GetEnvironmentVariable("ACCOUNTS_TEST_PAYMENT_WORKBOOK")!;
        await using var stream = File.OpenRead(path);
        var file = new FormFile(stream, 0, stream.Length, "excelFile", Path.GetFileName(path));
        var result = await service.ImportPaymentsAsync(file);
        Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(10, result.TotalRows);
        Assert.Equal(10, result.ImportedRows);
        var saved = await target.RoznamchaEntries.AsNoTracking().ToListAsync();
        Assert.Equal(10, saved.Count);
        Assert.All(saved, row => { Assert.Equal(20, row.EnterStatusId); Assert.True(row.IsSettled); });
        var settled = await RoznamchaStatusCatalog.Query(target, "Payment").SingleAsync(x => x.Id == 20);
        Assert.Equal("Settled", settled.Name);
        Assert.Equal("#2BFA06", settled.ColorCode);

        await using var retryStream = File.OpenRead(path);
        var retry = await service.ImportPaymentsAsync(new FormFile(retryStream, 0, retryStream.Length, "excelFile", Path.GetFileName(path)));
        Assert.Equal(0, retry.ImportedRows);
        Assert.Equal(10, retry.DuplicateRows);
        Assert.Empty(retry.Errors);
    }

    private sealed class TestTenant(int id) : ITenantService
    {
        public int? TenantId => id;
        public int RequiredTenantId => id;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
    }
    private sealed class TestUser(int id) : ICurrentUserService
    {
        public int TenantId => id;
        public string? UserId => "workbook-verification";
    }
    private sealed class TestReferences : IReferenceGeneratorService
    {
        private int sequence;
        private Task<string> Next() => Task.FromResult($"TEST-{++sequence}");
        public Task<string> AccountReferenceAsync(int id, CancellationToken ct = default) => Next();
        public Task<string> MainAccountCodeAsync(int id, string code, CancellationToken ct = default) => Next();
        public Task<string> SubAccountCodeAsync(int id, int parent, string code, CancellationToken ct = default) => Next();
        public Task<string> AccountNumberAsync(int id, string code, CancellationToken ct = default) => Next();
        public Task<string> PaymentReferenceAsync(int id, CancellationToken ct = default) => Next();
        public Task<string> ReceiptReferenceAsync(int id, CancellationToken ct = default) => Next();
        public Task<string> DocumentReferenceAsync(int id, CancellationToken ct = default) => Next();
    }
    private sealed class UnusedFiles : IFileStorageService
    {
        public Task<StoredFileDto> SaveAccountsFileAsync(int id, IFormFile file, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAccountsFileAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(Stream Stream, string ContentType, string FileName)?> OpenAccountsFileAsync(string path, CancellationToken ct = default) => Task.FromResult<(Stream, string, string)?>(null);
    }
}

public sealed class PaymentWorkbookFactAttribute : FactAttribute
{
    public PaymentWorkbookFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_SQL_CONNECTION")) ||
            !File.Exists(Environment.GetEnvironmentVariable("ACCOUNTS_TEST_PAYMENT_WORKBOOK")))
            Skip = "Set ACCOUNTS_TEST_SQL_CONNECTION and ACCOUNTS_TEST_PAYMENT_WORKBOOK for the isolated workbook import test.";
    }
}
