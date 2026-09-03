using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs.CommCenter;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/app-lookups")]
[Authorize]
[Produces("application/json")]
public sealed class AppLookupsController(
    ApplicationDbContext db,
    ITenantService tenant,
    RbacService rbac,
    TenantPermissionService tenantPermissions) : ControllerBase
{
    private const string RoutePath = "/settings/lookup-masters";

    [HttpGet("all")]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.AppLookupValues.AsNoTracking().Include(v => v.LookupType).AsQueryable();
        if (!includeInactive)
            query = query.Where(v => v.IsActive && v.LookupType != null && v.LookupType.IsActive);
        var values = await query
            .OrderBy(v => v.LookupType!.LookupTypeCode)
            .ThenBy(v => v.SortOrder)
            .Select(v => Map(v))
            .ToListAsync(ct);
        return Ok(CommApiResponse<List<AppLookupDto>>.Ok(values));
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetTypes([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.AppLookupTypes.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(t => t.IsActive);
        var types = await query.OrderBy(t => t.LookupTypeCode)
            .Select(t => new AppLookupTypeDto
            {
                LookupTypeId = t.LookupTypeId,
                LookupTypeCode = t.LookupTypeCode,
                LookupTypeName = t.LookupTypeName,
                IsActive = t.IsActive
            })
            .ToListAsync(ct);
        return Ok(CommApiResponse<List<AppLookupTypeDto>>.Ok(types));
    }

    [HttpGet("{lookupTypeCode}")]
    public async Task<IActionResult> GetByType(string lookupTypeCode, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.AppLookupValues.AsNoTracking().Include(v => v.LookupType)
            .Where(v => v.LookupType != null && v.LookupType.LookupTypeCode == lookupTypeCode);
        if (!includeInactive)
            query = query.Where(v => v.IsActive && v.LookupType!.IsActive);
        var values = await query.OrderBy(v => v.SortOrder).Select(v => Map(v)).ToListAsync(ct);
        return Ok(CommApiResponse<List<AppLookupDto>>.Ok(values));
    }

    [HttpPost("types")]
    public async Task<IActionResult> CreateType(AppLookupTypeSave dto, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        var code = CleanCode(dto.LookupTypeCode);
        var name = Clean(dto.LookupTypeName);
        if (code == null || name == null) return BadRequest(CommApiResponse<object>.Fail("Lookup type code and name are required."));
        if (await db.AppLookupTypes.AnyAsync(t => t.LookupTypeCode == code, ct))
            return Conflict(CommApiResponse<object>.Fail("Lookup type code already exists."));
        var row = new AppLookupType
        {
            LookupTypeCode = code,
            LookupTypeName = name,
            IsActive = dto.IsActive,
            CreatedOn = DateTime.UtcNow
        };
        db.AppLookupTypes.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(CommApiResponse<object>.Ok(new { row.LookupTypeId, row.LookupTypeCode, row.LookupTypeName, row.IsActive }, "Lookup type created."));
    }

    [HttpPut("types/{id:int}")]
    public async Task<IActionResult> UpdateType(int id, AppLookupTypeSave dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var row = await db.AppLookupTypes.SingleOrDefaultAsync(t => t.LookupTypeId == id, ct);
        if (row == null) return NotFound();
        var code = CleanCode(dto.LookupTypeCode);
        var name = Clean(dto.LookupTypeName);
        if (code == null || name == null) return BadRequest(CommApiResponse<object>.Fail("Lookup type code and name are required."));
        if (await db.AppLookupTypes.AnyAsync(t => t.LookupTypeId != id && t.LookupTypeCode == code, ct))
            return Conflict(CommApiResponse<object>.Fail("Lookup type code already exists."));
        row.LookupTypeCode = code;
        row.LookupTypeName = name;
        row.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(CommApiResponse<object>.Ok(new { row.LookupTypeId, row.LookupTypeCode, row.LookupTypeName, row.IsActive }, "Lookup type updated."));
    }

    [HttpPost("values")]
    public async Task<IActionResult> CreateValue(AppLookupValueSave dto, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        var type = await ResolveTypeAsync(dto, ct);
        if (type == null) return BadRequest(CommApiResponse<object>.Fail("Select a valid lookup type."));
        var valueCode = Clean(dto.ValueCode);
        var display = Clean(dto.DisplayText);
        if (valueCode == null || display == null) return BadRequest(CommApiResponse<object>.Fail("Value code and display text are required."));
        if (await db.AppLookupValues.AnyAsync(v => v.LookupTypeId == type.LookupTypeId && v.ValueCode == valueCode, ct))
            return Conflict(CommApiResponse<object>.Fail("Value code already exists for this lookup type."));
        if (dto.IsDefault)
            await ClearDefaultsAsync(type.LookupTypeId, null, ct);
        var row = new AppLookupValue
        {
            LookupTypeId = type.LookupTypeId,
            ValueCode = valueCode,
            DisplayText = display,
            SortOrder = dto.SortOrder,
            IsDefault = dto.IsDefault,
            IsActive = dto.IsActive,
            MetadataJson = Clean(dto.MetadataJson),
            CreatedOn = DateTime.UtcNow
        };
        db.AppLookupValues.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(CommApiResponse<object>.Ok(MapEntity(row, type), "Lookup value created."));
    }

    [HttpPut("values/{id:int}")]
    public async Task<IActionResult> UpdateValue(int id, AppLookupValueSave dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var row = await db.AppLookupValues.Include(v => v.LookupType).SingleOrDefaultAsync(v => v.LookupValueId == id, ct);
        if (row == null) return NotFound();
        var type = await ResolveTypeAsync(dto, ct) ?? row.LookupType;
        if (type == null) return BadRequest(CommApiResponse<object>.Fail("Select a valid lookup type."));
        var valueCode = Clean(dto.ValueCode);
        var display = Clean(dto.DisplayText);
        if (valueCode == null || display == null) return BadRequest(CommApiResponse<object>.Fail("Value code and display text are required."));
        if (await db.AppLookupValues.AnyAsync(v => v.LookupValueId != id && v.LookupTypeId == type.LookupTypeId && v.ValueCode == valueCode, ct))
            return Conflict(CommApiResponse<object>.Fail("Value code already exists for this lookup type."));
        if (dto.IsDefault)
            await ClearDefaultsAsync(type.LookupTypeId, id, ct);
        row.LookupTypeId = type.LookupTypeId;
        row.ValueCode = valueCode;
        row.DisplayText = display;
        row.SortOrder = dto.SortOrder;
        row.IsDefault = dto.IsDefault;
        row.IsActive = dto.IsActive;
        row.MetadataJson = Clean(dto.MetadataJson);
        await db.SaveChangesAsync(ct);
        return Ok(CommApiResponse<object>.Ok(MapEntity(row, type), "Lookup value updated."));
    }

    [HttpDelete("values/{id:int}")]
    public async Task<IActionResult> DeactivateValue(int id, CancellationToken ct)
    {
        var denied = await Guard("DELETE", ct); if (denied != null) return denied;
        var row = await db.AppLookupValues.SingleOrDefaultAsync(v => v.LookupValueId == id, ct);
        if (row == null) return NotFound();
        row.IsActive = false;
        row.IsDefault = false;
        await db.SaveChangesAsync(ct);
        return Ok(CommApiResponse<object>.Ok(null!, "Lookup value deactivated."));
    }

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return null;
        if (!tenant.TenantId.HasValue) return Forbid();
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, [RoutePath], action, ct) ? null : Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        var menuId = await db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == RoutePath)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return Forbid();
        if (action == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return null;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{action}") ? null : Forbid();
    }

    private async Task<AppLookupType?> ResolveTypeAsync(AppLookupValueSave dto, CancellationToken ct)
    {
        if (dto.LookupTypeId is > 0)
            return await db.AppLookupTypes.SingleOrDefaultAsync(t => t.LookupTypeId == dto.LookupTypeId, ct);
        var code = CleanCode(dto.LookupTypeCode);
        if (code == null) return null;
        return await db.AppLookupTypes.SingleOrDefaultAsync(t => t.LookupTypeCode == code, ct);
    }

    private async Task ClearDefaultsAsync(int lookupTypeId, int? exceptId, CancellationToken ct)
    {
        var rows = await db.AppLookupValues
            .Where(v => v.LookupTypeId == lookupTypeId && v.IsDefault && (exceptId == null || v.LookupValueId != exceptId))
            .ToListAsync(ct);
        foreach (var row in rows) row.IsDefault = false;
    }

    private static AppLookupDto Map(AppLookupValue v) => new()
    {
        LookupValueId = v.LookupValueId,
        LookupTypeId = v.LookupTypeId,
        LookupTypeCode = v.LookupType!.LookupTypeCode,
        LookupTypeName = v.LookupType.LookupTypeName,
        ValueCode = v.ValueCode,
        DisplayText = v.DisplayText,
        SortOrder = v.SortOrder,
        IsDefault = v.IsDefault,
        IsActive = v.IsActive,
        MetadataJson = v.MetadataJson
    };

    private static AppLookupDto MapEntity(AppLookupValue v, AppLookupType type) => new()
    {
        LookupValueId = v.LookupValueId,
        LookupTypeId = type.LookupTypeId,
        LookupTypeCode = type.LookupTypeCode,
        LookupTypeName = type.LookupTypeName,
        ValueCode = v.ValueCode,
        DisplayText = v.DisplayText,
        SortOrder = v.SortOrder,
        IsDefault = v.IsDefault,
        IsActive = v.IsActive,
        MetadataJson = v.MetadataJson
    };

    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    private static string? CleanCode(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
}

public sealed record AppLookupTypeSave(string LookupTypeCode, string LookupTypeName, bool IsActive = true);
public sealed record AppLookupValueSave(int? LookupTypeId, string? LookupTypeCode, string ValueCode, string DisplayText, int SortOrder, bool IsDefault, bool IsActive = true, string? MetadataJson = null);
public sealed class AppLookupTypeDto
{
    public int LookupTypeId { get; set; }
    public string LookupTypeCode { get; set; } = string.Empty;
    public string LookupTypeName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
