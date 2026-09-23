using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class CurrentUserService(ITenantService tenant, IHttpContextAccessor accessor) : ICurrentUserService
{
    public int TenantId => tenant.RequiredTenantId;
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}

public sealed class ReferenceGeneratorService(ApplicationDbContext db) : IReferenceGeneratorService
{
    public async Task<string> AccountReferenceAsync(int tenantId, CancellationToken ct = default)
    {
        var prefix = await TenantPrefixAsync(tenantId, ct);
        var values = await db.AccountsChartAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AccountReference != null)
            .Select(x => x.AccountReference!)
            .ToListAsync(ct);
        return $"{prefix}-Acct-{NextNumericSuffix(values)}";
    }

    public async Task<string> MainAccountCodeAsync(int tenantId, string categoryCode, CancellationToken ct = default)
    {
        var prefix = categoryCode.Trim().ToUpperInvariant();
        var codes = await db.AccountsChartAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ParentId == null && x.AccountCode != null && x.AccountCode.StartsWith(prefix + "-"))
            .Select(x => x.AccountCode!)
            .ToListAsync(ct);
        return $"{prefix}-{NextNumericSuffix(codes)}";
    }

    public async Task<string> SubAccountCodeAsync(int tenantId, int parentId, string parentCode, CancellationToken ct = default)
    {
        var codes = await db.AccountsChartAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ParentId == parentId && x.AccountCode != null)
            .Select(x => x.AccountCode!)
            .ToListAsync(ct);
        return $"{parentCode}/{NextNumericSuffix(codes)}";
    }

    public Task<string> AccountNumberAsync(int tenantId, string accountCode, CancellationToken ct = default) =>
        Task.FromResult($"{accountCode}-0001");

    public Task<string> PaymentReferenceAsync(int tenantId, CancellationToken ct = default) =>
        JournalReferenceAsync(tenantId, "DB", ct);

    public Task<string> ReceiptReferenceAsync(int tenantId, CancellationToken ct = default) =>
        JournalReferenceAsync(tenantId, "CR", ct);

    public async Task<string> DocumentReferenceAsync(int tenantId, CancellationToken ct = default)
    {
        var prefix = await TenantPrefixAsync(tenantId, ct);
        var next = await db.AccountsEntryDocuments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId, ct) + 1;
        return $"{prefix}-DOC-{next:00000}";
    }

    private async Task<string> JournalReferenceAsync(int tenantId, string marker, CancellationToken ct)
    {
        var prefix = await TenantPrefixAsync(tenantId, ct);
        var starts = $"{prefix}{marker}-";
        var refs = await db.RoznamchaEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Ref != null && x.Ref.StartsWith(starts))
            .Select(x => x.Ref!)
            .ToListAsync(ct);
        return $"{starts}{NextNumericSuffix(refs):00000}";
    }

    private async Task<string> TenantPrefixAsync(int tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => new { x.TenantCode, x.TenantName })
            .SingleAsync(ct);
        if (!string.IsNullOrWhiteSpace(tenant.TenantCode))
            return tenant.TenantCode.Trim().ToUpperInvariant();
        return BuildPrefix(tenant.TenantName);
    }

    public static string BuildPrefix(string? organizationName)
    {
        var words = (organizationName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? "ORG" : string.Concat(words.Select(x => char.ToUpperInvariant(x[0])));
    }

    public static int NextNumericSuffix(IEnumerable<string> values)
    {
        var max = 0;
        foreach (var value in values)
        {
            var tail = value[(Math.Max(value.LastIndexOf('-'), value.LastIndexOf('/')) + 1)..];
            if (int.TryParse(tail, out var number) && number > max) max = number;
        }
        return max + 1;
    }
}

public sealed class AccountsFileStorageService(IWebHostEnvironment environment) : IFileStorageService
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".doc", ".docx", ".xls", ".xlsx"
    };

    public async Task<StoredFileDto> SaveAccountsFileAsync(int tenantId, IFormFile file, CancellationToken ct = default)
    {
        if (file.Length <= 0) throw new InvalidOperationException("The uploaded file is empty.");
        if (file.Length > MaxBytes) throw new InvalidOperationException("The uploaded file cannot exceed 10 MB.");
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension)) throw new InvalidOperationException("This file type is not allowed.");

        var safeName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relative = Path.Combine("accounts", tenantId.ToString(), safeName).Replace('\\', '/');
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "accounts", tenantId.ToString());
        Directory.CreateDirectory(root);
        var absolute = Path.Combine(root, safeName);
        await using var output = File.Create(absolute);
        await file.CopyToAsync(output, ct);
        return new StoredFileDto
        {
            FileName = Path.GetFileName(file.FileName),
            StoredPath = relative,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length
        };
    }

    public Task DeleteAccountsFileAsync(string storedPath, CancellationToken ct = default)
    {
        var absolute = Resolve(storedPath);
        if (File.Exists(absolute)) File.Delete(absolute);
        return Task.CompletedTask;
    }

    public Task<(Stream Stream, string ContentType, string FileName)?> OpenAccountsFileAsync(string storedPath, CancellationToken ct = default)
    {
        var absolute = Resolve(storedPath);
        if (!File.Exists(absolute)) return Task.FromResult<(Stream, string, string)?>(null);
        var stream = (Stream)File.OpenRead(absolute);
        return Task.FromResult<(Stream, string, string)?>(new(stream, "application/octet-stream", Path.GetFileName(absolute)));
    }

    private string Resolve(string storedPath)
    {
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data"));
        var full = Path.GetFullPath(Path.Combine(root, storedPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid stored file path.");
        return full;
    }
}
