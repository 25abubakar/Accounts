using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AccountTypeMasterService(ApplicationDbContext db, ICurrentUserService current) : IAccountTypeMasterService
{
    public const string KindRoznamcha = "roznamcha";
    public const string KindTransType = "transaction-type";
    public const string KindTransMode = "transaction-mode";
    public const string KindDate = "date";
    public const string KindCurrency = "currency";

    public async Task<IReadOnlyList<AccountTypeLookupDto>> ListAsync(string kind, CancellationToken ct = default) =>
        Normalize(kind) switch
        {
            KindRoznamcha => await db.AccountsRoznamchaTypes.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => Map(x.Id, x.Code, x.Name, x.IsActive, x.TenantId)).ToListAsync(ct),
            KindTransType => await db.AccountsTransTypes.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => Map(x.Id, x.Code, x.Name, x.IsActive, x.TenantId)).ToListAsync(ct),
            KindTransMode => await db.AccountsTransModes.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => Map(x.Id, x.Code, x.Name, x.IsActive, x.TenantId)).ToListAsync(ct),
            KindDate => await db.AccountsDateLabels.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => Map(x.Id, x.Code, x.Name, x.IsActive, x.TenantId)).ToListAsync(ct),
            KindCurrency => await db.AccountsCurrencies.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => Map(x.Id, x.Code, x.Name, x.IsActive, x.TenantId)).ToListAsync(ct),
            _ => throw new InvalidOperationException("Unknown account type kind."),
        };

    public Task<AccountTypeLookupDto> SaveAsync(string kind, int? id, SaveAccountTypeLookupRequest request, CancellationToken ct = default)
    {
        var name = Required(request.Name, FieldLabel(kind), MaxName(kind));
        var code = ToCode(name, MaxCode(kind));
        return Normalize(kind) switch
        {
            KindRoznamcha => SaveRoznamchaAsync(id, name, code, request.IsActive, ct),
            KindTransType => SaveTransTypeAsync(id, name, code, request.IsActive, ct),
            KindTransMode => SaveTransModeAsync(id, name, code, request.IsActive, ct),
            KindDate => SaveDateAsync(id, name, code, request.IsActive, ct),
            KindCurrency => SaveCurrencyAsync(id, name, code, request.IsActive, ct),
            _ => throw new InvalidOperationException("Unknown account type kind."),
        };
    }

    public async Task DeleteAsync(string kind, int id, CancellationToken ct = default)
    {
        switch (Normalize(kind))
        {
            case KindRoznamcha:
            {
                var row = await db.AccountsRoznamchaTypes.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Roznamcha type was not found.");
                EnsureCanMutate(row.TenantId);
                if (await db.RoznamchaEntries.AnyAsync(x => x.RoznamchaTypeId == id, ct)
                    || await db.AccountsModuleSettings.AnyAsync(x => x.PaymentRozTypeId == id || x.ReceiptRozTypeId == id, ct))
                    throw new InvalidOperationException("Roznamcha type cannot be deleted because entries use it.");
                db.AccountsRoznamchaTypes.Remove(row);
                break;
            }
            case KindTransType:
            {
                var row = await db.AccountsTransTypes.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Transaction type was not found.");
                EnsureCanMutate(row.TenantId);
                if (await db.RoznamchaEntries.AnyAsync(x => x.TransTypeId == id, ct)
                    || await db.ReminderReceivables.AnyAsync(x => x.TransTypeId == id, ct)
                    || await db.ReminderPayables.AnyAsync(x => x.TransTypeId == id, ct))
                    throw new InvalidOperationException("Transaction type cannot be deleted because records use it.");
                db.AccountsTransTypes.Remove(row);
                break;
            }
            case KindTransMode:
            {
                var row = await db.AccountsTransModes.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Transaction mode was not found.");
                EnsureCanMutate(row.TenantId);
                if (await db.RoznamchaEntries.AnyAsync(x => x.TransModeId == id, ct))
                    throw new InvalidOperationException("Transaction mode cannot be deleted because entries use it.");
                db.AccountsTransModes.Remove(row);
                break;
            }
            case KindDate:
            {
                var row = await db.AccountsDateLabels.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Date label was not found.");
                EnsureCanMutate(row.TenantId);
                db.AccountsDateLabels.Remove(row);
                break;
            }
            case KindCurrency:
            {
                var row = await db.AccountsCurrencies.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Currency was not found.");
                EnsureCanMutate(row.TenantId);
                if (await db.AccountsModuleSettings.AnyAsync(x => x.DefaultCurrencyId == id, ct)
                    || await db.RoznamchaEntries.AnyAsync(x => x.CurrencyId == id, ct)
                    || await db.ReminderReceivables.AnyAsync(x => x.CurrencyId == id, ct)
                    || await db.ReminderPayables.AnyAsync(x => x.CurrencyId == id, ct)
                    || await db.SaleRoznamchaProducts.AnyAsync(x => x.CurrencyId == id, ct))
                    throw new InvalidOperationException("Currency cannot be deleted because records use it.");
                db.AccountsCurrencies.Remove(row);
                break;
            }
            default:
                throw new InvalidOperationException("Unknown account type kind.");
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<AccountTypeLookupDto> SaveRoznamchaAsync(int? id, string name, string code, bool isActive, CancellationToken ct)
    {
        if (await db.AccountsRoznamchaTypes.AsNoTracking()
                .AnyAsync(x => (x.Name == name || x.Code == code) && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("Roznamcha type already exists.");

        AccountsRoznamchaType row;
        if (id.HasValue)
        {
            row = await db.AccountsRoznamchaTypes.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Roznamcha type was not found.");
            EnsureCanMutate(row.TenantId);
            row.Name = name;
            row.Code = code;
            row.IsActive = isActive;
        }
        else
        {
            row = new AccountsRoznamchaType { TenantId = current.TenantId, Name = name, Code = code, IsActive = isActive };
            db.AccountsRoznamchaTypes.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return Map(row.Id, row.Code, row.Name, row.IsActive, row.TenantId);
    }

    private async Task<AccountTypeLookupDto> SaveTransTypeAsync(int? id, string name, string code, bool isActive, CancellationToken ct)
    {
        if (await db.AccountsTransTypes.AsNoTracking()
                .AnyAsync(x => (x.Name == name || x.Code == code) && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("Transaction type already exists.");

        AccountsTransType row;
        if (id.HasValue)
        {
            row = await db.AccountsTransTypes.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Transaction type was not found.");
            EnsureCanMutate(row.TenantId);
            row.Name = name;
            row.Code = code;
            row.IsActive = isActive;
        }
        else
        {
            row = new AccountsTransType { TenantId = current.TenantId, Name = name, Code = code, IsActive = isActive };
            db.AccountsTransTypes.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return Map(row.Id, row.Code, row.Name, row.IsActive, row.TenantId);
    }

    private async Task<AccountTypeLookupDto> SaveTransModeAsync(int? id, string name, string code, bool isActive, CancellationToken ct)
    {
        if (await db.AccountsTransModes.AsNoTracking()
                .AnyAsync(x => (x.Name == name || x.Code == code) && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("Transaction mode already exists.");

        AccountsTransMode row;
        if (id.HasValue)
        {
            row = await db.AccountsTransModes.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Transaction mode was not found.");
            EnsureCanMutate(row.TenantId);
            row.Name = name;
            row.Code = code;
            row.IsActive = isActive;
        }
        else
        {
            row = new AccountsTransMode { TenantId = current.TenantId, Name = name, Code = code, IsActive = isActive };
            db.AccountsTransModes.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return Map(row.Id, row.Code, row.Name, row.IsActive, row.TenantId);
    }

    private async Task<AccountTypeLookupDto> SaveDateAsync(int? id, string name, string code, bool isActive, CancellationToken ct)
    {
        if (await db.AccountsDateLabels.AsNoTracking()
                .AnyAsync(x => (x.Name == name || x.Code == code) && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("Date label already exists.");

        AccountsDateLabel row;
        if (id.HasValue)
        {
            row = await db.AccountsDateLabels.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Date label was not found.");
            EnsureCanMutate(row.TenantId);
            row.Name = name;
            row.Code = code;
            row.IsActive = isActive;
        }
        else
        {
            row = new AccountsDateLabel { TenantId = current.TenantId, Name = name, Code = code, IsActive = isActive };
            db.AccountsDateLabels.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return Map(row.Id, row.Code, row.Name, row.IsActive, row.TenantId);
    }

    private async Task<AccountTypeLookupDto> SaveCurrencyAsync(int? id, string name, string code, bool isActive, CancellationToken ct)
    {
        var currencyCode = Regex.IsMatch(name, @"^[A-Za-z]{2,10}$")
            ? name.Trim().ToUpperInvariant()
            : code;
        if (currencyCode.Length > 10) currencyCode = currencyCode[..10];

        if (await db.AccountsCurrencies.AsNoTracking()
                .AnyAsync(x => (x.Name == name || x.Code == currencyCode) && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("Currency already exists.");

        AccountsCurrency row;
        if (id.HasValue)
        {
            row = await db.AccountsCurrencies.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Currency was not found.");
            EnsureCanMutate(row.TenantId);
            row.Name = name;
            row.Code = currencyCode;
            row.IsActive = isActive;
        }
        else
        {
            row = new AccountsCurrency { TenantId = current.TenantId, Name = name, Code = currencyCode, IsActive = isActive };
            db.AccountsCurrencies.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return Map(row.Id, row.Code, row.Name, row.IsActive, row.TenantId);
    }

    private void EnsureCanMutate(int? rowTenantId)
    {
        if (rowTenantId == null)
            throw new InvalidOperationException("Platform lookup rows cannot be edited. Add a tenant-specific entry instead.");
        if (rowTenantId != current.TenantId)
            throw new InvalidOperationException("You can only change lookup rows for your tenant.");
    }

    private static AccountTypeLookupDto Map(int id, string code, string name, bool isActive, int? tenantId) =>
        new() { Id = id, Code = code, Name = name, IsActive = isActive, IsPlatform = tenantId == null };

    private static string Normalize(string kind) => (kind ?? string.Empty).Trim().ToLowerInvariant();

    private static string FieldLabel(string kind) => Normalize(kind) switch
    {
        KindRoznamcha => "Roznamcha type",
        KindTransType => "Transaction type",
        KindTransMode => "Transaction mode",
        KindDate => "Date label",
        KindCurrency => "Currency",
        _ => "Name",
    };

    private static int MaxName(string kind) => Normalize(kind) == KindCurrency ? 80 : 120;
    private static int MaxCode(string kind) => Normalize(kind) == KindCurrency ? 10 : 40;

    private static string Required(string? value, string label, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"{label} is required.");
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string ToCode(string name, int maxLen)
    {
        var cleaned = Regex.Replace(name.Trim().ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
        if (string.IsNullOrEmpty(cleaned)) cleaned = "ITEM";
        return cleaned.Length <= maxLen ? cleaned : cleaned[..maxLen];
    }
}
