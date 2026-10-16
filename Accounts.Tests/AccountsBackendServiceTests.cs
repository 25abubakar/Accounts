using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Accounts.Tests;

public sealed class AccountsBackendServiceTests
{
    [Theory]
    [InlineData("Life Time", "LT")]
    [InlineData("Lal Technologies Pakistan", "LTP")]
    [InlineData("", "ORG")]
    public void Organization_prefix_uses_first_letter_of_each_word(string name, string expected) =>
        Assert.Equal(expected, ReferenceGeneratorService.BuildPrefix(name));

    [Fact]
    public void Numeric_suffix_uses_the_highest_existing_sequence() =>
        Assert.Equal(13, ReferenceGeneratorService.NextNumericSuffix(["CAT-2", "CAT-12", "CAT/not-a-number"]));

    [Fact]
    public async Task Payment_applies_tax_and_stores_normal_currency_amount()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        var service = NewRoznamcha(db, 1);

        var result = await service.CreateAsync(1, Entry(1, 2, amount: 1_000, taxRate: 10));

        Assert.Equal(1, result.EntryTypeId);
        Assert.Equal(900, result.Amount);
        Assert.Equal(100, result.TaxAmount);
        Assert.StartsWith("LTDB-", result.ReferenceNo);
    }

    [Fact]
    public async Task Payment_accepts_negative_amount_and_rate()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        var request = Entry(1, 2, amount: -733_830.37m);
        request.Rate = -7_312.31m;

        var result = await NewRoznamcha(db, 1).CreateAsync(1, request);

        Assert.Equal(-733_830.37m, result.Amount);
        Assert.Equal(-7_312.31m, result.Rate);
    }

    [Fact]
    public async Task Payment_list_requires_relational_stored_procedure_provider()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        await NewRoznamcha(db, 1).CreateAsync(1, Entry(1, 2, amount: 750));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AccountsRoznamchaService(db).ListPaymentRozAsync(
                1,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30)));
        Assert.Contains("relational", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Receipt_list_requires_relational_stored_procedure_provider()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        var transactions = NewRoznamcha(db, 1);
        await transactions.CreateAsync(1, Entry(1, 2, amount: 300));
        await transactions.CreateAsync(2, Entry(2, 1, amount: 900));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AccountsRoznamchaService(db).ListReceiptRozAsync(
                1,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30)));
        Assert.Contains("relational", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Receipt_in_USD_stores_amount_in_USD_column()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        var service = NewRoznamcha(db, 1);
        var request = Entry(1, 2, amount: 250);
        request.CurrencyId = 2;

        var result = await service.CreateAsync(2, request);

        Assert.Equal(2, result.EntryTypeId);
        Assert.Equal(0, result.Amount);
        Assert.Equal(250, result.UsdAmount);
        Assert.StartsWith("LTCR-", result.ReferenceNo);
    }

    [Fact]
    public async Task Transaction_rejects_same_source_and_destination_account()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewRoznamcha(db, 1).CreateAsync(1, Entry(1, 1, amount: 100)));
        Assert.Contains("same account", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Account_with_transactions_cannot_be_deleted()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        await NewRoznamcha(db, 1).CreateAsync(1, Entry(1, 2, amount: 100));
        var service = new AccountService(db, new TestCurrentUser(1), new TestReferences(), new TestFiles());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(1));

        Assert.Contains("transactions", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Monthly_recurring_entry_creates_next_due_date_and_reminder_date()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        db.FrequencyTypes.Add(new FrequencyType { Id = 20, TenantId = 1, Name = "Monthly", Code = "PM" });
        await db.SaveChangesAsync();
        var service = new PayableReceivableService(db, new TestCurrentUser(1), NewRoznamcha(db, 1));
        var original = await service.CreateAsync(new CreateRecurringTransactionRequest
        {
            Kind = "payable", FrequencyId = 20, DueDate = new DateOnly(2026, 1, 31),
            ReminderDays = 5, FromAccountId = 1, ToAccountId = 2, Amount = 500,
            InvoiceTypeId = 8, CurrencyId = 2, Remarks = "Preserve me"
        });

        var next = await service.GenerateNextAsync(original.Id, "payable");

        Assert.Equal(new DateOnly(2026, 2, 28), next.DueDate);
        Assert.Equal(new DateOnly(2026, 2, 23), next.RemindDate);
        Assert.Equal(500, next.Amount);
        Assert.Equal(8, next.InvoiceTypeId);
        Assert.Equal(2, next.CurrencyId);
        Assert.Equal("Preserve me", next.Remarks);
    }

    [Fact]
    public async Task Global_query_filter_hides_other_tenant_accounts()
    {
        var options = Options();
        await using (var seed = new ApplicationDbContext(options, new TestTenant(1)))
        {
            seed.AccountsChartAccounts.AddRange(
                Account(1, 1, "Tenant one"),
                Account(2, 2, "Tenant two"));
            await seed.SaveChangesAsync();
        }
        await using var tenantOne = new ApplicationDbContext(options, new TestTenant(1));
        Assert.Equal(["Tenant one"], await tenantOne.AccountsChartAccounts.Select(x => x.AccountName).ToListAsync());
    }

    [Fact]
    public async Task Roznamcha_reconciles_active_same_tenant_library_documents()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        db.LibraryTypes.Add(new LibraryType { Id = 7, TenantId = 1, Code = "DOC", Name = "Documents" });
        db.LibraryDocuments.Add(new LibraryDocument
        {
            Id = 9, TenantId = 1, LibraryTypeId = 7, DocumentReference = "Doc-9",
            AssetKind = "Document", Title = "Voucher", OriginalFileName = "voucher.pdf",
            StoredFileName = "voucher.pdf", ContentType = "application/pdf", FileExtension = ".pdf",
            FileSizeBytes = 10, IsActive = true
        });
        await db.SaveChangesAsync();
        var request = Entry(1, 2, 100);
        request.LibraryDocumentIds = [9];

        var result = await NewRoznamcha(db, 1).CreateAsync(1, request);

        Assert.True(result.IsFromLibrary);
        var linked = Assert.Single(result.LibraryDocuments);
        Assert.Equal(9, linked.Id);
        Assert.Equal("Doc-9", linked.DocumentReference);
        Assert.Single(await db.RoznamchaLibraryDocuments.ToListAsync());
    }

    [Fact]
    public async Task Roznamcha_library_links_are_idempotent_and_reusable()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        db.LibraryTypes.Add(new LibraryType { Id = 7, TenantId = 1, Code = "DOC", Name = "Documents" });
        db.LibraryDocuments.Add(new LibraryDocument
        {
            Id = 9, TenantId = 1, LibraryTypeId = 7, DocumentReference = "Doc-9",
            AssetKind = "Document", Title = "Voucher", OriginalFileName = "voucher.pdf",
            StoredFileName = "voucher.pdf", ContentType = "application/pdf", FileExtension = ".pdf",
            FileSizeBytes = 10, IsActive = true
        });
        await db.SaveChangesAsync();
        var service = NewRoznamcha(db, 1);
        var first = await service.CreateAsync(1, Entry(1, 2, 100));
        var second = await service.CreateAsync(2, Entry(2, 1, 100));

        await service.LinkLibraryDocumentsAsync(first.Id, [9, 9]);
        await service.LinkLibraryDocumentsAsync(first.Id, [9]);
        await service.LinkLibraryDocumentsAsync(second.Id, [9]);

        Assert.Equal(2, await db.RoznamchaLibraryDocuments.CountAsync());
        Assert.Single(await service.ListLibraryDocumentsAsync(first.Id));
        Assert.Single(await service.ListLibraryDocumentsAsync(second.Id));
    }

    [Fact]
    public async Task Inventory_posted_payment_edit_synchronizes_stock_but_delete_remains_blocked()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        var service = NewRoznamcha(db, 1);
        var payment = await service.CreateAsync(1, Entry(1, 2, amount: 180));
        db.SaleRoznamchaStockReceipts.Add(new SaleRoznamchaStockReceipt
        {
            TenantId = 1,
            ProductId = 1,
            PurchasedQuantity = 1,
            RemainingQuantity = 1,
            UnitCost = 180,
            TotalPurchaseCost = 180,
            CurrencyId = 1,
            FromAccountId = 1,
            PurchasedDate = new DateOnly(2026, 10, 14),
            RoznamchaEntryId = payment.Id
        });
        await db.SaveChangesAsync();

        var update = Entry(1, 2, amount: 90);
        update.CurrencyId = 1;
        update.Quantity = 2;
        update.Rate = 45;
        update.Description = "Adjusted purchase";
        await service.UpdateAsync(payment.Id, 1, update);

        var purchase = await db.SaleRoznamchaStockReceipts.SingleAsync(x => x.RoznamchaEntryId == payment.Id);
        Assert.Equal(2, purchase.PurchasedQuantity);
        Assert.Equal(2, purchase.RemainingQuantity);
        Assert.Equal(45, purchase.UnitCost);
        Assert.Equal(90, purchase.TotalPurchaseCost);
        Assert.Equal("Adjusted purchase", purchase.Description);
        var movement = await db.SaleRoznamchaInventoryMovements.SingleAsync(x => x.StockReceiptId == purchase.Id);
        Assert.Equal(1, movement.QuantityDelta);
        Assert.Equal(2, movement.BalanceAfter);

        var delete = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SoftDeleteAsync(payment.Id));
        Assert.Contains("inventory purchase", delete.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.RoznamchaEntries.SingleAsync(x => x.Id == payment.Id)).IsDeleted);
    }

    [Fact]
    public async Task Roznamcha_rejects_library_document_outside_tenant()
    {
        await using var db = CreateDb(1);
        await SeedAccounts(db, 1);
        db.LibraryTypes.Add(new LibraryType { Id = 8, TenantId = 2, Code = "OTHER", Name = "Other tenant" });
        db.LibraryDocuments.Add(new LibraryDocument
        {
            Id = 10, TenantId = 2, LibraryTypeId = 8, DocumentReference = "Doc-10",
            AssetKind = "Document", Title = "Private", OriginalFileName = "private.pdf",
            StoredFileName = "private.pdf", ContentType = "application/pdf", FileExtension = ".pdf",
            FileSizeBytes = 10, IsActive = true
        });
        await db.SaveChangesAsync();
        var request = Entry(1, 2, 100);
        request.LibraryDocumentIds = [10];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewRoznamcha(db, 1).CreateAsync(1, request));

        Assert.Contains("outside this tenant", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.RoznamchaLibraryDocuments.ToListAsync());
    }

    private static ApplicationDbContext CreateDb(int tenantId)
    {
        var db = new ApplicationDbContext(Options(), new TestTenant(tenantId));
        db.Database.EnsureCreated();
        return db;
    }

    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static async Task SeedAccounts(ApplicationDbContext db, int tenantId)
    {
        db.AccountsCategories.Add(new AccountsCategory { Id = 1, TenantId = tenantId, Name = "Cash", Code = "CASH" });
        db.AccountsChartAccounts.AddRange(Account(1, tenantId, "Cash"), Account(2, tenantId, "Bank"));
        db.AccountsRoznamchaTypes.AddRange(
            new AccountsRoznamchaType { Id = 10, Code = "PAYMENT", Name = "Payment" },
            new AccountsRoznamchaType { Id = 11, Code = "RECEIPT", Name = "Receipt" });
        db.AccountsCurrencies.AddRange(
            new AccountsCurrency { Id = 1, Code = "PKR", Name = "Pakistani Rupee" },
            new AccountsCurrency { Id = 2, Code = "USD", Name = "US Dollar" });
        db.AccountsTaxTypes.Add(new AccountsTaxType { Id = 1, Code = "WHT", Name = "Withholding Tax", IsActive = true });
        await db.SaveChangesAsync();
    }

    private static AccountsChartAccount Account(int id, int tenantId, string name) => new()
    {
        Id = id, TenantId = tenantId, AccountName = name, AccountNumber = $"A-{id:0000}",
        AccountCode = $"A-{id}", CategoryId = 1, IsActive = true, StatusId = 11
    };

    private static SaveRoznamchaEntryRequest Entry(int from, int to, decimal amount, decimal? taxRate = null) => new()
    {
        FromAccountId = from, ToAccountId = to, CategoryId = 1, Amount = amount,
        TaxRate = taxRate, TaxTypeId = taxRate.HasValue ? 1 : null, TransactionDate = new DateOnly(2026, 9, 22)
    };

    private static RoznamchaService NewRoznamcha(ApplicationDbContext db, int tenantId) =>
        new(db, new TestCurrentUser(tenantId), new TestReferences(), new TestFiles());

    private sealed class TestCurrentUser(int tenantId) : ICurrentUserService
    {
        public int TenantId { get; } = tenantId;
        public string? UserId => "test-user";
    }

    private sealed class TestTenant(int tenantId) : ITenantService
    {
        public int? TenantId { get; } = tenantId;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public int RequiredTenantId => TenantId!.Value;
    }

    private sealed class TestReferences : IReferenceGeneratorService
    {
        public Task<string> AccountReferenceAsync(int tenantId, CancellationToken ct = default) => Task.FromResult("LT-Acct-1");
        public Task<string> MainAccountCodeAsync(int tenantId, string categoryCode, CancellationToken ct = default) => Task.FromResult($"{categoryCode}-1");
        public Task<string> SubAccountCodeAsync(int tenantId, int parentId, string parentCode, CancellationToken ct = default) => Task.FromResult($"{parentCode}/1");
        public Task<string> AccountNumberAsync(int tenantId, string accountCode, CancellationToken ct = default) => Task.FromResult($"{accountCode}-0001");
        public Task<string> PaymentReferenceAsync(int tenantId, CancellationToken ct = default) => Task.FromResult("LTDB-00001");
        public Task<string> ReceiptReferenceAsync(int tenantId, CancellationToken ct = default) => Task.FromResult("LTCR-00001");
        public Task<string> DocumentReferenceAsync(int tenantId, CancellationToken ct = default) => Task.FromResult("LT-DOC-00001");
    }

    private sealed class TestFiles : IFileStorageService
    {
        public Task<StoredFileDto> SaveAccountsFileAsync(int tenantId, IFormFile file, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAccountsFileAsync(string storedPath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(Stream Stream, string ContentType, string FileName)?> OpenAccountsFileAsync(string storedPath, CancellationToken ct = default) => Task.FromResult<(Stream, string, string)?>(null);
    }
}
