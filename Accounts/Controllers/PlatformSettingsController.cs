using System.Security.Claims;
using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/platform-settings")]
[Authorize]
[AutoValidateAntiforgeryToken]
[Produces("application/json")]
public sealed partial class PlatformSettingsController(
    ApplicationDbContext db,
    ITenantService tenant,
    RbacService rbac,
    TenantPermissionService tenantPermissions) : ControllerBase
{
    private const string RoutePath = "/settings/configuration";

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (tenant.IsSuperAdmin) return Ok(new PlatformSettingsDto());
        if (!tenant.TenantId.HasValue) return Forbid();
        if (!await HasActionAsync("VIEW", ct)) return Forbid();

        return Ok(new PlatformSettingsDto
        {
            Actions = await db.PlatformSettingActions.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new NamedSettingDto(x.Id, x.Name, x.IsActive)).ToListAsync(ct),
            Statuses = await db.PlatformSettingStatuses.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new NamedSettingDto(x.Id, x.Name, x.IsActive)).ToListAsync(ct),
            Colors = await db.PlatformSettingColors.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new ColorSettingDto(x.Id, x.ColorCode, x.FontColor, x.IsActive)).ToListAsync(ct),
            ActionStatuses = await db.PlatformSettingActionStatuses.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new ActionStatusSettingDto(x.Id, x.ActionId, x.Action.Name, x.ColorId,
                    x.Color == null ? null : x.Color.ColorCode, x.StatusId, x.Status.Name)).ToListAsync(ct),
            StatusCrDbValues = await db.PlatformSettingStatusCrDbValues.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new StatusCrDbValueDto(x.Id, x.StatusId, x.Status.Name, x.CrValue, x.DbValue)).ToListAsync(ct)
        });
    }

    [HttpPost("actions")]
    public Task<IActionResult> CreateAction([FromBody] SaveNamedSettingDto dto, CancellationToken ct) =>
        CreateNamedAsync<PlatformSettingAction>(dto, ct);

    [HttpPut("actions/{id:int}")]
    public Task<IActionResult> UpdateAction(int id, [FromBody] SaveNamedSettingDto dto, CancellationToken ct) =>
        UpdateNamedAsync<PlatformSettingAction>(id, dto, ct);

    [HttpDelete("actions/{id:int}")]
    public Task<IActionResult> DeleteAction(int id, CancellationToken ct) => DeleteNamedAsync<PlatformSettingAction>(id, ct);

    [HttpPost("statuses")]
    public Task<IActionResult> CreateStatus([FromBody] SaveNamedSettingDto dto, CancellationToken ct) =>
        CreateNamedAsync<PlatformSettingStatus>(dto, ct);

    [HttpPut("statuses/{id:int}")]
    public Task<IActionResult> UpdateStatus(int id, [FromBody] SaveNamedSettingDto dto, CancellationToken ct) =>
        UpdateNamedAsync<PlatformSettingStatus>(id, dto, ct);

    [HttpDelete("statuses/{id:int}")]
    public Task<IActionResult> DeleteStatus(int id, CancellationToken ct) => DeleteNamedAsync<PlatformSettingStatus>(id, ct);

    [HttpPost("colors")]
    public async Task<IActionResult> CreateColor([FromBody] SaveColorSettingDto dto, CancellationToken ct)
    {
        if (!await CanMutateAsync("ADD", ct)) return Forbid();
        var validation = ValidateColor(dto);
        if (validation != null) return BadRequest(new { message = validation });
        var colorCode = NormalizeColor(dto.ColorCode)!;
        if (await db.PlatformSettingColors.AnyAsync(x => x.ColorCode == colorCode, ct))
            return Conflict(new { message = "This background color already exists." });

        var row = new PlatformSettingColor
        {
            TenantId = tenant.RequiredTenantId,
            ColorCode = colorCode,
            FontColor = NormalizeColor(dto.FontColor),
            IsActive = dto.IsActive,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        db.PlatformSettingColors.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new ColorSettingDto(row.Id, row.ColorCode, row.FontColor, row.IsActive));
    }

    [HttpPut("colors/{id:int}")]
    public async Task<IActionResult> UpdateColor(int id, [FromBody] SaveColorSettingDto dto, CancellationToken ct)
    {
        if (!await CanMutateAsync("EDIT", ct)) return Forbid();
        var validation = ValidateColor(dto);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await db.PlatformSettingColors.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Color not found." });
        var colorCode = NormalizeColor(dto.ColorCode)!;
        if (await db.PlatformSettingColors.AnyAsync(x => x.ColorCode == colorCode && x.Id != id, ct))
            return Conflict(new { message = "This background color already exists." });
        row.ColorCode = colorCode;
        row.FontColor = NormalizeColor(dto.FontColor);
        row.IsActive = dto.IsActive;
        row.ModifiedOnUtc = DateTime.UtcNow;
        row.ModifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        return Ok(new ColorSettingDto(row.Id, row.ColorCode, row.FontColor, row.IsActive));
    }

    [HttpDelete("colors/{id:int}")]
    public async Task<IActionResult> DeleteColor(int id, CancellationToken ct)
    {
        if (!await CanMutateAsync("DELETE", ct)) return Forbid();
        var row = await db.PlatformSettingColors.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Color not found." });
        db.PlatformSettingColors.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Color deleted successfully." });
    }

    [HttpPost("action-statuses")]
    public Task<IActionResult> CreateActionStatus([FromBody] SaveActionStatusDto dto, CancellationToken ct) =>
        SaveActionStatusAsync(null, dto, ct);

    [HttpPut("action-statuses/{id:int}")]
    public Task<IActionResult> UpdateActionStatus(int id, [FromBody] SaveActionStatusDto dto, CancellationToken ct) =>
        SaveActionStatusAsync(id, dto, ct);

    [HttpDelete("action-statuses/{id:int}")]
    public async Task<IActionResult> DeleteActionStatus(int id, CancellationToken ct)
    {
        if (!await CanMutateAsync("DELETE", ct)) return Forbid();
        var row = await db.PlatformSettingActionStatuses.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Action status mapping not found." });
        db.PlatformSettingActionStatuses.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Action status mapping deleted successfully." });
    }

    [HttpPost("status-cr-db-values")]
    public Task<IActionResult> CreateStatusCrDbValue([FromBody] SaveStatusCrDbValueDto dto, CancellationToken ct) =>
        SaveStatusCrDbValueAsync(null, dto, ct);

    [HttpPut("status-cr-db-values/{id:int}")]
    public Task<IActionResult> UpdateStatusCrDbValue(int id, [FromBody] SaveStatusCrDbValueDto dto, CancellationToken ct) =>
        SaveStatusCrDbValueAsync(id, dto, ct);

    [HttpDelete("status-cr-db-values/{id:int}")]
    public async Task<IActionResult> DeleteStatusCrDbValue(int id, CancellationToken ct)
    {
        if (!await CanMutateAsync("DELETE", ct)) return Forbid();
        var row = await db.PlatformSettingStatusCrDbValues.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Status CR/DB value not found." });
        db.PlatformSettingStatusCrDbValues.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Status CR/DB value deleted successfully." });
    }

    private async Task<IActionResult> SaveActionStatusAsync(int? id, SaveActionStatusDto dto, CancellationToken ct)
    {
        if (!await CanMutateAsync(id.HasValue ? "EDIT" : "ADD", ct)) return Forbid();
        var action = await db.PlatformSettingActions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.ActionId, ct);
        var status = await db.PlatformSettingStatuses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.StatusId, ct);
        if (action == null || status == null) return BadRequest(new { message = "Select a valid action and status." });
        PlatformSettingColor? color = null;
        if (dto.ColorId.HasValue)
        {
            color = await db.PlatformSettingColors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.ColorId.Value, ct);
            if (color == null) return BadRequest(new { message = "Select a valid color." });
        }
        if (await db.PlatformSettingActionStatuses.AnyAsync(x => x.ActionId == dto.ActionId && x.StatusId == dto.StatusId && (!id.HasValue || x.Id != id.Value), ct))
            return Conflict(new { message = "This action and status combination already exists." });
        var row = id.HasValue
            ? await db.PlatformSettingActionStatuses.SingleOrDefaultAsync(x => x.Id == id.Value, ct)
            : new PlatformSettingActionStatus
            {
                TenantId = tenant.RequiredTenantId,
                CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            };
        if (row == null) return NotFound(new { message = "Action status mapping not found." });
        row.ActionId = dto.ActionId;
        row.StatusId = dto.StatusId;
        row.ColorId = dto.ColorId;
        if (id.HasValue)
        {
            row.ModifiedOnUtc = DateTime.UtcNow;
            row.ModifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        else db.PlatformSettingActionStatuses.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new ActionStatusSettingDto(row.Id, row.ActionId, action.Name, row.ColorId,
            color?.ColorCode, row.StatusId, status.Name));
    }

    private async Task<IActionResult> SaveStatusCrDbValueAsync(int? id, SaveStatusCrDbValueDto dto, CancellationToken ct)
    {
        if (!await CanMutateAsync(id.HasValue ? "EDIT" : "ADD", ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.CrValue) || string.IsNullOrWhiteSpace(dto.DbValue))
            return BadRequest(new { message = "CR value and DB value are required." });
        if (dto.CrValue.Trim().Length > 150 || dto.DbValue.Trim().Length > 150)
            return BadRequest(new { message = "CR value and DB value must be 150 characters or less." });
        var status = await db.PlatformSettingStatuses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.StatusId, ct);
        if (status == null) return BadRequest(new { message = "Select a valid status." });
        if (await db.PlatformSettingStatusCrDbValues.AnyAsync(x => x.StatusId == dto.StatusId && (!id.HasValue || x.Id != id.Value), ct))
            return Conflict(new { message = "This status already has CR/DB values." });
        var row = id.HasValue
            ? await db.PlatformSettingStatusCrDbValues.SingleOrDefaultAsync(x => x.Id == id.Value, ct)
            : new PlatformSettingStatusCrDbValue
            {
                TenantId = tenant.RequiredTenantId,
                CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            };
        if (row == null) return NotFound(new { message = "Status CR/DB value not found." });
        row.StatusId = dto.StatusId;
        row.CrValue = dto.CrValue.Trim();
        row.DbValue = dto.DbValue.Trim();
        if (id.HasValue)
        {
            row.ModifiedOnUtc = DateTime.UtcNow;
            row.ModifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        else db.PlatformSettingStatusCrDbValues.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new StatusCrDbValueDto(row.Id, row.StatusId, status.Name, row.CrValue, row.DbValue));
    }

    private async Task<IActionResult> CreateNamedAsync<TEntity>(SaveNamedSettingDto dto, CancellationToken ct)
        where TEntity : PlatformSettingNamedRow, new()
    {
        if (!await CanMutateAsync("ADD", ct)) return Forbid();
        var validation = ValidateName(dto.Name);
        if (validation != null) return BadRequest(new { message = validation });
        var name = dto.Name.Trim();
        if (await db.Set<TEntity>().AnyAsync(x => x.Name == name, ct))
            return Conflict(new { message = "This name already exists." });
        var row = new TEntity
        {
            TenantId = tenant.RequiredTenantId,
            Name = name,
            IsActive = dto.IsActive,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        db.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new NamedSettingDto(row.Id, row.Name, row.IsActive));
    }

    private async Task<IActionResult> UpdateNamedAsync<TEntity>(int id, SaveNamedSettingDto dto, CancellationToken ct)
        where TEntity : PlatformSettingNamedRow
    {
        if (!await CanMutateAsync("EDIT", ct)) return Forbid();
        var validation = ValidateName(dto.Name);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await db.Set<TEntity>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Setting value not found." });
        var name = dto.Name.Trim();
        if (await db.Set<TEntity>().AnyAsync(x => x.Name == name && x.Id != id, ct))
            return Conflict(new { message = "This name already exists." });
        row.Name = name;
        row.IsActive = dto.IsActive;
        row.ModifiedOnUtc = DateTime.UtcNow;
        row.ModifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        return Ok(new NamedSettingDto(row.Id, row.Name, row.IsActive));
    }

    private async Task<IActionResult> DeleteNamedAsync<TEntity>(int id, CancellationToken ct)
        where TEntity : PlatformSettingNamedRow
    {
        if (!await CanMutateAsync("DELETE", ct)) return Forbid();
        var row = await db.Set<TEntity>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Setting value not found." });
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Setting value deleted successfully." });
    }

    private async Task<bool> CanMutateAsync(string action, CancellationToken ct) =>
        tenant.TenantId.HasValue && await HasActionAsync(action, ct);

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, [RoutePath], action, ct);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuIds = await db.Menus.AsNoTracking().Where(x => x.IsActive && x.Route == RoutePath)
            .Select(x => x.Id).ToListAsync(ct);
        if (menuIds.Count == 0) return false;
        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await db.StaffMenuAccesses.AsNoTracking()
                .AnyAsync(x => x.StaffId == staffId.Value && menuIds.Contains(x.MenuId) && x.IsAllow, ct))
            return true;
        foreach (var menuId in menuIds)
        {
            if (normalized == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}")) return true;
            if (await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalized}")) return true;
        }
        return false;
    }

    private static string? ValidateName(string? name) => string.IsNullOrWhiteSpace(name)
        ? "Name is required."
        : name.Trim().Length > 150 ? "Name must be 150 characters or less." : null;

    private static string? ValidateColor(SaveColorSettingDto dto)
    {
        if (NormalizeColor(dto.ColorCode) == null) return "A valid background color in #RRGGBB format is required.";
        if (!string.IsNullOrWhiteSpace(dto.FontColor) && NormalizeColor(dto.FontColor) == null)
            return "Font color must use #RRGGBB format.";
        return null;
    }

    private static string? NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var color = value.Trim();
        if (!color.StartsWith('#')) color = $"#{color}";
        return HexColor().IsMatch(color) ? color.ToUpperInvariant() : null;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}

public sealed class PlatformSettingsDto
{
    public List<NamedSettingDto> Actions { get; set; } = [];
    public List<NamedSettingDto> Statuses { get; set; } = [];
    public List<ColorSettingDto> Colors { get; set; } = [];
    public List<ActionStatusSettingDto> ActionStatuses { get; set; } = [];
    public List<StatusCrDbValueDto> StatusCrDbValues { get; set; } = [];
}

public sealed record NamedSettingDto(int Id, string Name, bool IsActive);
public sealed record ColorSettingDto(int Id, string ColorCode, string? FontColor, bool IsActive);
public sealed record ActionStatusSettingDto(int Id, int ActionId, string ActionName, int? ColorId, string? ColorCode, int StatusId, string StatusName);
public sealed record StatusCrDbValueDto(int Id, int StatusId, string StatusName, string CrValue, string DbValue);
public sealed class SaveNamedSettingDto { public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } = true; }
public sealed class SaveColorSettingDto { public string ColorCode { get; set; } = string.Empty; public string? FontColor { get; set; } public bool IsActive { get; set; } = true; }
public sealed class SaveActionStatusDto { public int ActionId { get; set; } public int StatusId { get; set; } public int? ColorId { get; set; } }
public sealed class SaveStatusCrDbValueDto { public int StatusId { get; set; } public string CrValue { get; set; } = string.Empty; public string DbValue { get; set; } = string.Empty; }
