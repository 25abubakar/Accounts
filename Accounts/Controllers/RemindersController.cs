using System.Security.Claims;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/reminders")]
[Authorize]
[Produces("application/json")]
public sealed class RemindersController : ControllerBase
{
    private const string ReceivableRoute = "/reminders/receivable";
    private const string PayableRoute = "/reminders/payable";
    private const string AccountTypeLookup = "REMINDER_ACCOUNT_TYPE";
    private const string ReminderTypeLookup = "REMINDER_RECEIVABLE_TYPE";
    private const string InvoiceTypeLookup = "REMINDER_INVOICE_TYPE";

    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IReminderReceivableService _receivables;
    private readonly IReminderPayableService _payables;

    public RemindersController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IReminderReceivableService receivables,
        IReminderPayableService payables)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _receivables = receivables;
        _payables = payables;
    }

    [HttpGet("receivable")]
    public async Task<IActionResult> ListReceivable(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return Ok(Array.Empty<object>());

        if (!_tenant.TenantId.HasValue || !await HasActionAsync(ReceivableRoute, "VIEW", ct))
            return Forbid();

        var rows = await _receivables.ListAsync(_tenant.RequiredTenantId, ct);
        return Ok(rows);
    }

    [HttpGet("receivable/lookups")]
    public async Task<IActionResult> ReceivableLookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return Ok(new
            {
                accounts = Array.Empty<object>(), categories = Array.Empty<object>(), frequencies = Array.Empty<object>(),
                transTypes = Array.Empty<object>(), accountTypes = Array.Empty<object>(), reminderTypes = Array.Empty<object>(),
                invoiceTypes = Array.Empty<object>()
            });

        if (!_tenant.TenantId.HasValue || !await HasActionAsync(ReceivableRoute, "VIEW", ct))
            return Forbid();

        var accounts = await _db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.AccountName)
            .Select(x => new { x.Id, x.AccountName, x.AccountNumber, x.CategoryId })
            .ToListAsync(ct);

        var categories = await _db.AccountsCategories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToListAsync(ct);

        var frequencies = await _db.FrequencyTypes.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        var transTypeRows = await _db.AccountsTransTypes.AsNoTracking()
            .Where(x => x.IsActive && (x.Code == "PAYMENT" || x.Code == "RECEIPT"))
            .Select(x => new { x.Id, x.Name, x.Code, x.TenantId })
            .ToListAsync(ct);
        var transTypes = transTypeRows
            .GroupBy(x => x.Code)
            .Select(group => group.OrderByDescending(x => x.TenantId.HasValue).First())
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToList();

        var reminderLookups = await _db.AppLookupValues.AsNoTracking()
            .Where(x => x.IsActive && x.LookupType != null && x.LookupType.IsActive &&
                        (x.LookupType.LookupTypeCode == AccountTypeLookup ||
                         x.LookupType.LookupTypeCode == ReminderTypeLookup ||
                         x.LookupType.LookupTypeCode == InvoiceTypeLookup))
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                Id = x.LookupValueId,
                Name = x.DisplayText,
                Code = x.ValueCode,
                LookupType = x.LookupType!.LookupTypeCode
            })
            .ToListAsync(ct);

        return Ok(new
        {
            accounts,
            categories,
            frequencies,
            transTypes,
            accountTypes = reminderLookups.Where(x => x.LookupType == AccountTypeLookup),
            reminderTypes = reminderLookups.Where(x => x.LookupType == ReminderTypeLookup),
            invoiceTypes = reminderLookups.Where(x => x.LookupType == InvoiceTypeLookup)
        });
    }

    [HttpGet("payable")]
    public async Task<IActionResult> ListPayable(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return Ok(Array.Empty<object>());

        if (!_tenant.TenantId.HasValue || !await HasActionAsync(PayableRoute, "VIEW", ct))
            return Forbid();

        var rows = await _payables.ListAsync(_tenant.RequiredTenantId, ct);
        return Ok(rows);
    }

    [HttpGet("payable/lookups")]
    public async Task<IActionResult> PayableLookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return Ok(new
            {
                accounts = Array.Empty<object>(), categories = Array.Empty<object>(), frequencies = Array.Empty<object>(),
                transTypes = Array.Empty<object>(), accountTypes = Array.Empty<object>(), reminderTypes = Array.Empty<object>(),
                invoiceTypes = Array.Empty<object>()
            });

        if (!_tenant.TenantId.HasValue || !await HasActionAsync(PayableRoute, "VIEW", ct))
            return Forbid();

        var accounts = await _db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.AccountName)
            .Select(x => new { x.Id, x.AccountName, x.AccountNumber, x.CategoryId })
            .ToListAsync(ct);

        var categories = await _db.AccountsCategories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToListAsync(ct);

        var frequencies = await _db.FrequencyTypes.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        var transTypeRows = await _db.AccountsTransTypes.AsNoTracking()
            .Where(x => x.IsActive && (x.Code == "PAYMENT" || x.Code == "RECEIPT"))
            .Select(x => new { x.Id, x.Name, x.Code, x.TenantId })
            .ToListAsync(ct);
        var transTypes = transTypeRows
            .GroupBy(x => x.Code)
            .Select(group => group.OrderByDescending(x => x.TenantId.HasValue).First())
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToList();

        var reminderLookups = await _db.AppLookupValues.AsNoTracking()
            .Where(x => x.IsActive && x.LookupType != null && x.LookupType.IsActive &&
                        (x.LookupType.LookupTypeCode == AccountTypeLookup ||
                         x.LookupType.LookupTypeCode == ReminderTypeLookup ||
                         x.LookupType.LookupTypeCode == InvoiceTypeLookup))
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                Id = x.LookupValueId,
                Name = x.DisplayText,
                Code = x.ValueCode,
                LookupType = x.LookupType!.LookupTypeCode
            })
            .ToListAsync(ct);

        return Ok(new
        {
            accounts,
            categories,
            frequencies,
            transTypes,
            accountTypes = reminderLookups.Where(x => x.LookupType == AccountTypeLookup),
            reminderTypes = reminderLookups.Where(x => x.LookupType == ReminderTypeLookup),
            invoiceTypes = reminderLookups.Where(x => x.LookupType == InvoiceTypeLookup)
        });
    }

    [HttpPost("payable")]
    public async Task<IActionResult> CreatePayable([FromBody] SaveReminderReceivableDto dto, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(PayableRoute, "ADD", ct))
            return Forbid();

        var error = await ValidatePayableAsync(dto, excludeId: null, ct);
        if (error != null)
            return BadRequest(new { message = error });

        var row = new ReminderPayable
        {
            TenantId = _tenant.RequiredTenantId,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedOnUtc = DateTime.UtcNow
        };
        Apply(row, dto);
        _db.ReminderPayables.Add(row);
        await _db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(row.Ref))
        {
            row.Ref = $"Pay-{row.Id}";
            await _db.SaveChangesAsync(ct);
        }

        var rows = await _payables.ListAsync(_tenant.RequiredTenantId, ct);
        return Ok(rows.FirstOrDefault(x => x.Id == row.Id));
    }

    [HttpPut("payable/{id:int}")]
    public async Task<IActionResult> UpdatePayable(int id, [FromBody] SaveReminderReceivableDto dto, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(PayableRoute, "EDIT", ct))
            return Forbid();

        var row = await _db.ReminderPayables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null)
            return NotFound(new { message = "Payable reminder was not found." });

        var error = await ValidatePayableAsync(dto, excludeId: id, ct);
        if (error != null)
            return BadRequest(new { message = error });

        Apply(row, dto);
        row.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        row.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var rows = await _payables.ListAsync(_tenant.RequiredTenantId, ct);
        return Ok(rows.FirstOrDefault(x => x.Id == row.Id));
    }

    [HttpDelete("payable/{id:int}")]
    public async Task<IActionResult> DeletePayable(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(PayableRoute, "DELETE", ct))
            return Forbid();

        var row = await _db.ReminderPayables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null)
            return NotFound(new { message = "Payable reminder was not found." });

        _db.ReminderPayables.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id });
    }

    [HttpPost("receivable")]
    public async Task<IActionResult> CreateReceivable([FromBody] SaveReminderReceivableDto dto, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(ReceivableRoute, "ADD", ct))
            return Forbid();

        var error = await ValidateAsync(dto, excludeId: null, ct);
        if (error != null)
            return BadRequest(new { message = error });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var row = new ReminderReceivable
        {
            TenantId = _tenant.RequiredTenantId,
            CreatedByUserId = userId,
            CreatedOnUtc = DateTime.UtcNow
        };
        Apply(row, dto);
        _db.ReminderReceivables.Add(row);
        await _db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(row.Ref))
        {
            row.Ref = $"Rec-{row.Id}";
            await _db.SaveChangesAsync(ct);
        }

        var rows = await _receivables.ListAsync(_tenant.RequiredTenantId, ct);
        return Ok(rows.FirstOrDefault(x => x.Id == row.Id));
    }

    [HttpPut("receivable/{id:int}")]
    public async Task<IActionResult> UpdateReceivable(int id, [FromBody] SaveReminderReceivableDto dto, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(ReceivableRoute, "EDIT", ct))
            return Forbid();

        var row = await _db.ReminderReceivables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null)
            return NotFound(new { message = "Receivable reminder was not found." });

        var error = await ValidateAsync(dto, excludeId: id, ct);
        if (error != null)
            return BadRequest(new { message = error });

        Apply(row, dto);
        row.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        row.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var rows = await _receivables.ListAsync(_tenant.RequiredTenantId, ct);
        return Ok(rows.FirstOrDefault(x => x.Id == row.Id));
    }

    [HttpDelete("receivable/{id:int}")]
    public async Task<IActionResult> DeleteReceivable(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(ReceivableRoute, "DELETE", ct))
            return Forbid();

        var row = await _db.ReminderReceivables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null)
            return NotFound(new { message = "Receivable reminder was not found." });

        _db.ReminderReceivables.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id });
    }

    private Task<string?> ValidatePayableAsync(SaveReminderReceivableDto dto, int? excludeId, CancellationToken ct) =>
        ValidateAsync(dto, excludeId, ct, payable: true);

    private async Task<string?> ValidateAsync(
        SaveReminderReceivableDto dto,
        int? excludeId,
        CancellationToken ct,
        bool payable = false)
    {
        if (dto.Amount < 0)
            return "Amount cannot be negative.";

        if (dto.RemindDay is < 0 or > 3650)
            return "Remind Day must be between 0 and 3650.";

        var refValue = Clean(dto.Ref, 80);
        if (!string.IsNullOrWhiteSpace(refValue))
        {
            var exists = payable
                ? await _db.ReminderPayables.AsNoTracking()
                    .AnyAsync(x => x.Ref == refValue && (!excludeId.HasValue || x.Id != excludeId.Value), ct)
                : await _db.ReminderReceivables.AsNoTracking()
                    .AnyAsync(x => x.Ref == refValue && (!excludeId.HasValue || x.Id != excludeId.Value), ct);
            if (exists)
                return "Ref already exists for this tenant.";
        }

        if (dto.AccountTypeId.HasValue && !await IsActiveLookupAsync(dto.AccountTypeId.Value, AccountTypeLookup, ct))
            return "Account Type must be a saved active reminder account type.";

        if (dto.ReminderTypeId.HasValue && !await IsActiveLookupAsync(dto.ReminderTypeId.Value, ReminderTypeLookup, ct))
            return "Type must be a saved active receivable type.";

        if (dto.InvoiceTypeId.HasValue && !await IsActiveLookupAsync(dto.InvoiceTypeId.Value, InvoiceTypeLookup, ct))
            return "Invoice must be a saved active reminder invoice type.";

        if (dto.CategoryId.HasValue &&
            !await _db.AccountsCategories.AsNoTracking().AnyAsync(x => x.Id == dto.CategoryId.Value && x.IsActive, ct))
            return "Category must be a saved active account category.";

        if (dto.ToCategoryId.HasValue &&
            !await _db.AccountsCategories.AsNoTracking().AnyAsync(x => x.Id == dto.ToCategoryId.Value && x.IsActive, ct))
            return "To Category must be a saved active account category.";

        if (dto.FromAccountId.HasValue)
        {
            var fromAccount = await _db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.Id == dto.FromAccountId.Value && x.IsActive)
                .Select(x => new { x.CategoryId })
                .FirstOrDefaultAsync(ct);
            if (fromAccount == null)
                return "From Account must be a saved active chart account.";
            if (dto.CategoryId.HasValue && fromAccount.CategoryId != dto.CategoryId)
                return "From Account does not belong to the selected Category.";
        }

        if (dto.ToAccountId.HasValue)
        {
            var toAccount = await _db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.Id == dto.ToAccountId.Value && x.IsActive)
                .Select(x => new { x.CategoryId })
                .FirstOrDefaultAsync(ct);
            if (toAccount == null)
                return "To Account must be a saved active chart account.";
            if (dto.ToCategoryId.HasValue && toAccount.CategoryId != dto.ToCategoryId)
                return "To Account does not belong to the selected To Category.";
        }

        if (dto.FrequencyTypeId.HasValue &&
            !await _db.FrequencyTypes.AsNoTracking().AnyAsync(x => x.Id == dto.FrequencyTypeId.Value && x.IsActive, ct))
            return "Frequency must be an active frequency type.";

        if (dto.TransTypeId.HasValue &&
            !await _db.AccountsTransTypes.AsNoTracking().AnyAsync(x => x.Id == dto.TransTypeId.Value && x.IsActive, ct))
            return "TransactionType must be an active transaction type.";

        if (Clean(dto.InvoiceType, 120) is { Length: > 120 })
            return "InvoiceType must be 120 characters or less.";

        if (Clean(dto.Remarks, 2000) is { Length: > 2000 })
            return "Remarks must be 2000 characters or less.";

        return null;
    }

    private Task<bool> IsActiveLookupAsync(int lookupValueId, string lookupTypeCode, CancellationToken ct) =>
        _db.AppLookupValues.AsNoTracking().AnyAsync(x =>
            x.LookupValueId == lookupValueId && x.IsActive && x.LookupType != null &&
            x.LookupType.IsActive && x.LookupType.LookupTypeCode == lookupTypeCode, ct);

    private static void Apply(ReminderReceivable row, SaveReminderReceivableDto dto)
    {
        row.Ref = Clean(dto.Ref, 80);
        row.AccountTypeId = dto.AccountTypeId;
        row.ReminderTypeId = dto.ReminderTypeId;
        row.InvoiceTypeId = dto.InvoiceTypeId;
        row.CategoryId = dto.CategoryId;
        row.FromAccountId = dto.FromAccountId;
        row.ToCategoryId = dto.ToCategoryId;
        row.ToAccountId = dto.ToAccountId;
        row.CreatedOn = dto.CreatedOn;
        row.DueDate = dto.DueDate;
        row.RemindDay = dto.RemindDay;
        row.RemindDate = dto.RemindDate;
        row.ReceivedOn = dto.ReceivedOn;
        row.LastPaidDate = dto.LastPaidDate;
        row.PaidOn = dto.PaidOn;
        row.Amount = decimal.Round(dto.Amount, 2, MidpointRounding.AwayFromZero);
        row.FrequencyTypeId = dto.FrequencyTypeId;
        row.InvoiceType = Clean(dto.InvoiceType, 120);
        row.TransTypeId = dto.TransTypeId;
        row.Remarks = Clean(dto.Remarks, 2000);
        row.IsActive = dto.IsActive;
        row.AlertRoznamcha = dto.AlertRoznamcha;
    }

    private static void Apply(ReminderPayable row, SaveReminderReceivableDto dto)
    {
        row.Ref = Clean(dto.Ref, 80);
        row.AccountTypeId = dto.AccountTypeId;
        row.ReminderTypeId = dto.ReminderTypeId;
        row.InvoiceTypeId = dto.InvoiceTypeId;
        row.CategoryId = dto.CategoryId;
        row.FromAccountId = dto.FromAccountId;
        row.ToCategoryId = dto.ToCategoryId;
        row.ToAccountId = dto.ToAccountId;
        row.CreatedOn = dto.CreatedOn;
        row.DueDate = dto.DueDate;
        row.RemindDay = dto.RemindDay;
        row.RemindDate = dto.RemindDate;
        row.ReceivedOn = dto.ReceivedOn;
        row.LastPaidDate = dto.LastPaidDate;
        row.PaidOn = dto.PaidOn;
        row.Amount = decimal.Round(dto.Amount, 2, MidpointRounding.AwayFromZero);
        row.FrequencyTypeId = dto.FrequencyTypeId;
        row.InvoiceType = Clean(dto.InvoiceType, 120);
        row.TransTypeId = dto.TransTypeId;
        row.Remarks = Clean(dto.Remarks, 2000);
        row.IsActive = dto.IsActive;
        row.InAlertRoznamcha = dto.InAlertRoznamcha;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private async Task<bool> HasActionAsync(string route, string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [route], action, ct);
        if (!_tenant.TenantId.HasValue) return false;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuId = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == route)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;
        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
            return true;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }
}

public sealed class SaveReminderReceivableDto
{
    public string? Ref { get; set; }
    public int? AccountTypeId { get; set; }
    public int? ReminderTypeId { get; set; }
    public int? InvoiceTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? FromAccountId { get; set; }
    public int? ToCategoryId { get; set; }
    public int? ToAccountId { get; set; }
    public DateOnly? CreatedOn { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? RemindDay { get; set; }
    public DateOnly? RemindDate { get; set; }
    public DateOnly? ReceivedOn { get; set; }
    public DateOnly? LastPaidDate { get; set; }
    public DateOnly? PaidOn { get; set; }
    public decimal Amount { get; set; }
    public int? FrequencyTypeId { get; set; }
    public string? InvoiceType { get; set; }
    public int? TransTypeId { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AlertRoznamcha { get; set; }
    public bool InAlertRoznamcha { get; set; }
}
