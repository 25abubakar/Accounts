using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Accounts.Tests;

public sealed class RoznamchaInternalTransferTests
{
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(1, -1000)]
    [InlineData(2, 1000)]
    [InlineData(2, -1000)]
    public async Task Payment_internal_transfer_creates_opposite_linked_rows(int currency, int amount)
    {
        await using var db = await SeedAsync();
        var result = await Service(db).CreateAsync(1, Request(amount, currency));
        var payment = await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 1);
        var receipt = await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 2);
        Assert.Equal(1, result.EntryTypeId);
        Assert.NotNull(payment.InternalTransferGroupId);
        Assert.Equal(payment.InternalTransferGroupId, receipt.InternalTransferGroupId);
        Assert.NotEqual(payment.Id, receipt.Id);
        Assert.NotEqual(payment.Ref, receipt.Ref);
        Assert.Equal(1, payment.FromAccountId); Assert.Equal(2, payment.ToAccountId);
        Assert.Equal(2, receipt.FromAccountId); Assert.Equal(1, receipt.ToAccountId);
        Assert.Equal(1, payment.CategoryId); Assert.Equal(2, receipt.CategoryId);
        Assert.Equal(-1000m, currency == 2 ? payment.UsdAmount : payment.Amount);
        Assert.Equal(1000m, currency == 2 ? receipt.UsdAmount : receipt.Amount);
        Assert.Equal(0m, currency == 2 ? payment.Amount : payment.UsdAmount);
        Assert.Equal(payment.TransDate, receipt.TransDate);
        Assert.Equal(1, payment.SNo); Assert.Equal(2, receipt.SNo);
        Assert.Equal(2, await db.RoznamchaEntryProcessLogs.CountAsync());
    }

    [Fact]
    public async Task Receipt_internal_transfer_can_create_the_payment_side()
    {
        await using var db = await SeedAsync();
        var result = await Service(db).CreateAsync(2, Request(-250));
        Assert.Equal(2, result.EntryTypeId);
        Assert.Equal(250, result.Amount);
        Assert.Equal(-250, (await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 1)).Amount);
    }

    [Fact]
    public async Task Ordinary_payment_remains_single_and_keeps_existing_amount_rules()
    {
        await using var db = await SeedAsync();
        var request = Request(250); request.TransactionTypeId = 1;
        var result = await Service(db).CreateAsync(1, request);
        Assert.Null(result.InternalTransferGroupId);
        Assert.Equal(250, result.Amount);
        Assert.Single(await db.RoznamchaEntries.ToListAsync());
    }

    [Fact]
    public async Task Editing_payment_updates_receipt_without_new_ids_or_references()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var created = await service.CreateAsync(1, Request(100));
        var receipt = await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 2);
        var receiptId = receipt.Id; var receiptRef = receipt.Ref;
        var edit = Request(450); edit.ToAccountId = 3;
        edit.Description = "Updated transfer"; edit.TransactionDate = new DateOnly(2026, 10, 20);
        await service.UpdateAsync(created.Id, 1, edit);
        db.ChangeTracker.Clear();
        receipt = await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 2);
        Assert.Equal(receiptId, receipt.Id); Assert.Equal(receiptRef, receipt.Ref);
        Assert.Equal(450, receipt.Amount); Assert.Equal(3, receipt.FromAccountId); Assert.Equal(1, receipt.ToAccountId);
        Assert.Equal(edit.TransactionDate, receipt.TransDate); Assert.Equal(edit.Description, receipt.Descriptions);
        Assert.Equal(-450, (await db.RoznamchaEntries.SingleAsync(x => x.Id == created.Id)).Amount);
        Assert.Equal(2, await db.RoznamchaEntries.CountAsync());
    }

    [Fact]
    public async Task Editing_receipt_updates_payment_with_opposite_sign_and_accounts()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        await service.CreateAsync(1, Request(100));
        var receipt = await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 2);
        var edit = Request(-350, 2); edit.FromAccountId = 3; edit.ToAccountId = 1; edit.CategoryId = 2;
        await service.UpdateAsync(receipt.Id, 2, edit);
        db.ChangeTracker.Clear();
        var payment = await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == 1);
        Assert.Equal(-350, payment.UsdAmount); Assert.Equal(0, payment.Amount);
        Assert.Equal(1, payment.FromAccountId); Assert.Equal(3, payment.ToAccountId);
        Assert.Equal(350, (await db.RoznamchaEntries.SingleAsync(x => x.Id == receipt.Id)).UsdAmount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Deleting_either_side_soft_deletes_both(int type)
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        await service.CreateAsync(1, Request(100));
        var row = await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == type);
        await service.SoftDeleteAsync(row.Id, "Cancel transfer");
        Assert.All(await db.RoznamchaEntries.ToListAsync(), e => Assert.True(e.IsDeleted));
        Assert.Equal(4, await db.RoznamchaEntryProcessLogs.CountAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_lock_on_either_side_prevents_edit_and_delete(int lockedType)
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var payment = await service.CreateAsync(1, Request(100));
        (await db.RoznamchaEntries.SingleAsync(x => x.RoznamchaTypeId == lockedType)).IsLocked = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(payment.Id, 1, Request(200)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SoftDeleteAsync(payment.Id));
        db.ChangeTracker.Clear();
        Assert.All(await db.RoznamchaEntries.ToListAsync(), e => Assert.False(e.IsDeleted));
        Assert.Equal(-100, (await db.RoznamchaEntries.SingleAsync(x => x.Id == payment.Id)).Amount);
    }

    [Fact]
    public async Task Missing_receipt_type_fails_before_any_row_is_written()
    {
        await using var db = await SeedAsync();
        db.AccountsRoznamchaTypes.Remove(await db.AccountsRoznamchaTypes.SingleAsync(x => x.Code == "RECEIPT"));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(1, Request(100)));
        Assert.Empty(await db.RoznamchaEntries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Linked_transfer_cannot_be_changed_to_an_unpaired_transaction_type()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var payment = await service.CreateAsync(1, Request(100));
        var edit = Request(200); edit.TransactionTypeId = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(payment.Id, 1, edit));
        Assert.Equal(2, await db.RoznamchaEntries.CountAsync());
    }

    [Fact]
    public async Task Legacy_excel_internal_transfer_does_not_backfill_a_receipt()
    {
        await using var db = await SeedAsync();
        var csv = "Ref,AcctName,ToAcctName,Debit,TransDate,Trans Type\r\nLEGACY-1,Cash,Bank,-100,2026-10-01,Internal Transfer\r\n";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var result = await Service(db).ImportPaymentsAsync(new FormFile(stream, 0, stream.Length, "excelFile", "legacy.csv"));
        Assert.Empty(result.Errors); Assert.Equal(1, result.ImportedRows);
        var row = await db.RoznamchaEntries.SingleAsync();
        Assert.Null(row.InternalTransferGroupId); Assert.Equal(-100, row.Amount); Assert.Equal(1, row.RoznamchaTypeId);
    }

    [Fact]
    public async Task Transfer_rejects_an_account_from_another_tenant()
    {
        await using var db = await SeedAsync();
        db.AccountsChartAccounts.Add(new AccountsChartAccount { Id = 99, TenantId = 2, AccountName = "Private", AccountNumber = "PRIVATE", IsActive = true });
        await db.SaveChangesAsync();
        var request = Request(100); request.ToAccountId = 99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(1, request));
        Assert.Empty(await db.RoznamchaEntries.ToListAsync());
    }

    internal static async Task<ApplicationDbContext> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        var db = new ApplicationDbContext(options, new TestTenant());
        await db.Database.EnsureCreatedAsync();
        db.AccountsCategories.AddRange(new AccountsCategory { Id = 1, TenantId = 1, Name = "Cash", Code = "CASH" }, new AccountsCategory { Id = 2, TenantId = 1, Name = "Banks", Code = "BANK" });
        db.AccountsChartAccounts.AddRange(Enumerable.Range(1, 3).Select(id => new AccountsChartAccount { Id = id, TenantId = 1, AccountName = id == 1 ? "Cash" : id == 2 ? "Bank" : "Other Bank", AccountNumber = $"A-{id}", CategoryId = id == 1 ? 1 : 2 }));
        db.AccountsRoznamchaTypes.AddRange(new AccountsRoznamchaType { Id = 1, Code = "PAYMENT", Name = "Payment" }, new AccountsRoznamchaType { Id = 2, Code = "RECEIPT", Name = "Receipt" });
        db.AccountsTransTypes.AddRange(new AccountsTransType { Id = 1, Code = "PAYMENT", Name = "Payment" }, new AccountsTransType { Id = 8, Code = "INTERNAL_TRANSFER", Name = "Internal Transfer" });
        db.AccountsCurrencies.AddRange(new AccountsCurrency { Id = 1, Code = "RS", Name = "Rupee" }, new AccountsCurrency { Id = 2, Code = "USD", Name = "US Dollar" });
        await db.SaveChangesAsync();
        return db;
    }

    internal static SaveRoznamchaEntryRequest Request(decimal amount, int currency = 1) => new()
    {
        FromAccountId = 1, ToAccountId = 2, CategoryId = 1, TransactionTypeId = 8,
        Amount = amount, CurrencyId = currency, TransactionDate = new DateOnly(2026, 10, 19)
    };

    internal static RoznamchaService Service(ApplicationDbContext db) => new(db, new CurrentUser(), new References(), new Files());
    internal sealed class TestTenant : ITenantService { public int? TenantId => 1; public int RequiredTenantId => 1; public bool IsSuperAdmin => false; public bool IsTenantAdmin => true; }
    private sealed class CurrentUser : ICurrentUserService { public int TenantId => 1; public string? UserId => "transfer-test"; }
    private sealed class References : IReferenceGeneratorService
    {
        private int sequence;
        public Task<string> PaymentReferenceAsync(int tenantId, CancellationToken ct = default) => Task.FromResult($"LTDB-{++sequence}");
        public Task<string> ReceiptReferenceAsync(int tenantId, CancellationToken ct = default) => Task.FromResult($"LTCR-{++sequence}");
        public Task<string> AccountReferenceAsync(int tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> MainAccountCodeAsync(int tenantId, string categoryCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> SubAccountCodeAsync(int tenantId, int parentId, string parentCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> AccountNumberAsync(int tenantId, string accountCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> DocumentReferenceAsync(int tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Files : IFileStorageService
    {
        public Task<StoredFileDto> SaveAccountsFileAsync(int tenantId, IFormFile file, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAccountsFileAsync(string storedPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(Stream Stream, string ContentType, string FileName)?> OpenAccountsFileAsync(string storedPath, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
