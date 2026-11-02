using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Route("api/billing/claims")]
public sealed class BillingClaimsController : ControllerBase
{
    private const string MenuRoute = "/billing/claims";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IBillingClaimsService _claims;
    private readonly IFileStorageService _files;

    public BillingClaimsController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IBillingClaimsService claims,
        IFileStorageService files)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _claims = claims;
        _files = files;
    }

    private string? UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], action, ct);
        if (!_tenant.TenantId.HasValue) return false;
        var userId = UserId();
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuId = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == MenuRoute)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;
        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
            return true;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string tab = "claims",
        [FromQuery] int? statusId = null,
        [FromQuery] int? carrierId = null,
        CancellationToken ct = default)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var rows = await _claims.ListClaimsAsync(_tenant.RequiredTenantId, dateFrom, dateTo, tab, statusId, carrierId, ct);
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var claim = await _claims.GetClaimByIdAsync(_tenant.RequiredTenantId, id, ct);
        return claim != null ? Ok(claim) : NotFound(new { message = "Claim was not found." });
    }

    [HttpPost]
    [Idempotent]
    public async Task<IActionResult> Create([FromBody] CreateBillingClaimRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("ADD", ct))
            return Forbid();

        var claimId = await _claims.CreateClaimAsync(_tenant.RequiredTenantId, request, UserId(), ct);
        return Ok(new { success = true, claimId, message = "Claim created successfully." });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateBillingClaimRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var ok = await _claims.UpdateClaimAsync(_tenant.RequiredTenantId, id, request, UserId(), ct);
        return ok ? Ok(new { success = true, message = "Claim updated successfully." }) : NotFound(new { message = "Claim was not found." });
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] BillingStatusUpdateRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var ok = await _claims.UpdateClaimStatusAsync(_tenant.RequiredTenantId, id, request.StatusId, request.Remarks, UserId(), ct);
        return ok ? Ok(new { success = true, message = "Status updated successfully." }) : NotFound(new { message = "Claim was not found." });
    }

    [HttpPost("{id:int}/payment")]
    [Idempotent]
    public async Task<IActionResult> PostPayment(int id, [FromBody] PostClaimPaymentRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var ok = await _claims.PostClaimPaymentAsync(_tenant.RequiredTenantId, id, request, UserId(), ct);
        return ok ? Ok(new { success = true, message = "Payment posted successfully." }) : NotFound(new { message = "Claim was not found." });
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var lookups = await _claims.GetClaimLookupsAsync(_tenant.RequiredTenantId, ct);
        return Ok(lookups);
    }

    [HttpGet("{id:int}/logs")]
    public async Task<IActionResult> GetLogs(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var logs = await _claims.GetClaimLogsAsync(_tenant.RequiredTenantId, id, ct);
        return Ok(logs);
    }

    [HttpGet("{id:int}/documents")]
    public async Task<IActionResult> GetDocuments(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var docs = await _db.BillingClaimEobDocs.AsNoTracking()
            .Where(x => x.ClaimId == id)
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync(ct);

        return Ok(docs);
    }

    [HttpPost("{id:int}/documents")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadDocument(int id, [FromForm] UploadAccountsFileRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.ClaimId == id, ct);
        if (claim == null) return NotFound(new { message = "Claim was not found." });

        if (request.File == null || request.File.Length == 0)
            return BadRequest(new { message = "A valid file is required." });

        var stored = await _files.SaveAccountsFileAsync(_tenant.RequiredTenantId, request.File, ct);
        var doc = new BillingClaimEobDoc
        {
            ClaimId = id,
            Attachment = stored.StoredPath,
            UploadDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingClaimEobDocs.Add(doc);

        claim.Attachment = stored.StoredPath;
        await _db.SaveChangesAsync(ct);

        return Ok(new { success = true, message = "Document uploaded successfully." });
    }
}
