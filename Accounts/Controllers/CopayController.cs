using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/[controller]")]
public sealed class CopayController(
    ICopayService copayService,
    ApplicationDbContext db,
    ITenantService tenant,
    TenantPermissionService tenantPermissions,
    RbacService rbac) : ControllerBase
{
    private const string ImsExcelRoute = "/copay/ims-excel";
    private const string ImsToMerchantRoute = "/copay/ims-to-merchant";
    private const string MerchantToBankRoute = "/copay/merchant-to-bank";

    // ── 1. Merchant To Bank Grid & Settlement ─────────────────────────────────

    /// <summary>
    /// Gets paginated merchant batch entries (isSHow = false) vs bank deposit matching status.
    /// </summary>
    [HttpGet("merchant-to-bank")]
    public async Task<IActionResult> GetMerchantToBankData([FromQuery] CopayFilterDto filter, CancellationToken ct)
    {
        if (!await HasActionAsync(MerchantToBankRoute, "VIEW", ct)) return Forbid();
        var result = await copayService.GetMerchantToBankListAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<MerchantToBankItemDto>>.Ok(result));
    }

    /// <summary>
    /// Auto-matches batch deposits with bank statements using same-day and multi-day tolerance window.
    /// </summary>
    [HttpPost("settle-merchant-to-bank")]
    [Idempotent]
    public async Task<IActionResult> SettleMerchantToBank([FromBody] DateRangeDto dateRange, CancellationToken ct)
    {
        if (!await HasActionAsync(MerchantToBankRoute, "EDIT", ct)) return Forbid();
        var result = await copayService.SettleMerchantToBankAutoAsync(dateRange, ct);
        return Ok(ApiResponse<SettlementResultDto>.Ok(result, "Merchant to Bank settlement completed."));
    }

    // ── 2. Copay IMS To Merchant Statement Grid & Upload ──────────────────────

    /// <summary>
    /// Gets paginated credit card merchant machine/terminal statements.
    /// </summary>
    [HttpGet("merchant-statements")]
    public async Task<IActionResult> GetMerchantStatements([FromQuery] CopayFilterDto filter, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsToMerchantRoute, "VIEW", ct)) return Forbid();
        var result = await copayService.GetMerchantStatementsAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<MerchantStatementItemDto>>.Ok(result));
    }

    /// <summary>
    /// Uploads and parses merchant terminal processor Excel/CSV statement sheet.
    /// </summary>
    [HttpPost("upload-merchant-statement")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadMerchantStatement([FromForm] UploadMerchantStatementDto dto, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsToMerchantRoute, "ADD", ct)) return Forbid();
        var importedCount = await copayService.ImportMerchantStatementExcelAsync(dto, ct);
        return Ok(ApiResponse<int>.Ok(importedCount, $"Successfully imported {importedCount} merchant statement entries."));
    }

    // ── 3. Copay IMS - Excel Grid, Upload & Settle ────────────────────────────

    /// <summary>
    /// Gets paginated front desk patient copay collection entries (isSHow = true).
    /// </summary>
    [HttpGet("ims-receipts")]
    public async Task<IActionResult> GetImsReceipts([FromQuery] CopayFilterDto filter, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsExcelRoute, "VIEW", ct)) return Forbid();
        var result = await copayService.GetImsReceiptsAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<ImsReceiptItemDto>>.Ok(result));
    }

    /// <summary>
    /// Uploads and parses clinic daily copay collection Excel/CSV sheet.
    /// </summary>
    [HttpPost("upload-ims-daily-sheet")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImsDailySheet([FromForm] UploadImsSheetDto dto, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsExcelRoute, "ADD", ct)) return Forbid();
        var count = await copayService.ImportImsDailySheetAsync(dto, ct);
        return Ok(ApiResponse<int>.Ok(count, $"Successfully imported {count} front desk copay collection rows."));
    }

    /// <summary>
    /// Automatically matches front desk patient copays against merchant terminal statements.
    /// </summary>
    [HttpPost("settle-ims-auto")]
    [Idempotent]
    public async Task<IActionResult> SettleImsAuto([FromBody] DateRangeDto dateRange, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsExcelRoute, "EDIT", ct)) return Forbid();
        var result = await copayService.SettleImsAutoAsync(dateRange, ct);
        return Ok(ApiResponse<SettlementResultDto>.Ok(result, "IMS auto-settlement completed."));
    }

    /// <summary>
    /// Directly matches front desk copay receipts (e.g. Cheques, direct deposits) against bank statements.
    /// </summary>
    [HttpPost("settle-ims-with-bank")]
    [Idempotent]
    public async Task<IActionResult> SettleImsWithBank([FromBody] DateRangeDto dateRange, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsExcelRoute, "EDIT", ct)) return Forbid();
        var result = await copayService.SettleImsWithBankAutoAsync(dateRange, ct);
        return Ok(ApiResponse<SettlementResultDto>.Ok(result, "IMS direct bank settlement completed."));
    }

    // ── 4. Manual Settlement & Un-settle ──────────────────────────────────────

    /// <summary>
    /// Manually links selected copay rows, optionally creating a consolidated batch for Merchant-to-Bank matching.
    /// </summary>
    [HttpPost("settle-ims-manual")]
    [Idempotent]
    public async Task<IActionResult> SettleImsManual([FromBody] ManualSettleRequestDto request, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsExcelRoute, "EDIT", ct)) return Forbid();
        var result = await copayService.SettleImsManualAsync(request, ct);
        return Ok(ApiResponse<SettlementResultDto>.Ok(result, "Manual settlement completed."));
    }

    /// <summary>
    /// Rolls back settlement for an entry or merchant statement, reverting status to Open.
    /// </summary>
    [HttpPost("unsettle")]
    [Idempotent]
    public async Task<IActionResult> UnsettleEntry([FromBody] UnsettleRequestDto request, CancellationToken ct)
    {
        if (!await HasActionAsync(ImsExcelRoute, "EDIT", ct)) return Forbid();
        var success = await copayService.UnsettleEntryAsync(request, ct);
        return Ok(ApiResponse<bool>.Ok(success, "Entry successfully un-settled and returned to Open status."));
    }

    // ── 5. Lookups & Dropdowns for React UI ───────────────────────────────────

    /// <summary>
    /// Gets active bank chart accounts, statuses, transaction modes, and years for UI dropdowns.
    /// </summary>
    [HttpGet("lookups")]
    public async Task<IActionResult> GetLookups(CancellationToken ct)
    {
        if (!await HasActionAsync(ImsExcelRoute, "VIEW", ct)
            && !await HasActionAsync(ImsToMerchantRoute, "VIEW", ct)
            && !await HasActionAsync(MerchantToBankRoute, "VIEW", ct)) return Forbid();
        var result = await copayService.GetLookupsAsync(ct);
        return Ok(ApiResponse<CopayLookupsDto>.Ok(result));
    }

    private async Task<bool> HasActionAsync(string route, string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return false;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, [route], action, ct);
        if (!tenant.TenantId.HasValue) return false;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;

        var staffId = await db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        var menuId = await db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == route)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return false;

        var normalized = action.ToUpperInvariant();
        return (normalized == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}"))
            || await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalized}");
    }
}
