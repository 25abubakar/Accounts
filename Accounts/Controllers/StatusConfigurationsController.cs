using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Accounts.Controllers;

[ApiController, Route("api/status-configurations"), Authorize]
public sealed class StatusConfigurationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    public StatusConfigurationsController(ApplicationDbContext db, ITenantService tenant) { _db = db; _tenant = tenant; }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;
        var rows = await _db.StatusConfigurationManagementRows.AsNoTracking()
            .Where(x => x.TenantId == null || (tenantId.HasValue && x.TenantId == tenantId))
            .OrderBy(x => x.ProcessName)
            .ThenBy(x => x.TenantId == null ? 0 : 1)
            .ThenBy(x => x.DisplayOrder)
            .ToListAsync(ct);

        return Ok(rows.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create(StatusConfigurationWriteDto dto, CancellationToken ct)
    {
        if (!await CanManageAsync("ADD", ct)) return Forbid();
        var entity = new ProcessStatusStyle {
            CreatedDate = DateTime.UtcNow,
            TenantId = _tenant.IsSuperAdmin ? null : _tenant.TenantId,
            IsSystem = _tenant.IsSuperAdmin
        };
        var error = await ApplyAsync(entity, dto, null, ct);
        if (error != null) return Conflict(new { message = error });
        _db.ProcessStatusStyles.Add(entity); await _db.SaveChangesAsync(ct);
        return Ok(await LoadDto(entity.Id, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, StatusConfigurationWriteDto dto, CancellationToken ct)
    {
        if (!await CanManageAsync("EDIT", ct)) return Forbid();
        var entity = await EditableRow(id, ct);
        if (entity == null) return NotFound();
        var error = await ApplyAsync(entity, dto, id, ct);
        if (error != null) return Conflict(new { message = error });
        entity.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct);
        return Ok(await LoadDto(id, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        if (!await CanManageAsync("DELETE", ct)) return Forbid();
        var entity = await EditableRow(id, ct);
        if (entity == null) return NotFound();
        entity.IsActive = false; entity.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string?> ApplyAsync(ProcessStatusStyle entity, StatusConfigurationWriteDto dto, int? excludingId, CancellationToken ct)
    {
        var processName=dto.ProcessName.Trim();
        var statusName=dto.StatusName.Trim();
        var ownerTenantId = entity.TenantId;
        var code=await ResolveCodeAsync(statusName, dto.Code, ownerTenantId, excludingId, ct);
        var duplicate=await _db.ProcessStatusStyles.AnyAsync(
            x => (!excludingId.HasValue || x.Id != excludingId.Value)
                 && ((ownerTenantId == null && x.TenantId == null) || (ownerTenantId != null && x.TenantId == ownerTenantId))
                 && x.Process.ProcessName == processName && x.Code == code, ct);
        if(duplicate) return $"Code '{code}' already exists for process '{processName}'.";
        var process=await _db.Processes.FirstOrDefaultAsync(x=>x.ProcessName==processName,ct) ?? new ProcessMaster{ProcessName=processName};
        var status=await _db.Statuses.FirstOrDefaultAsync(x=>x.StatusName==statusName,ct) ?? new StatusDefinition{StatusName=statusName};
        var color=string.IsNullOrWhiteSpace(dto.ColorCode) ? "#64748B" : dto.ColorCode.Trim().ToUpperInvariant();
        var font=string.IsNullOrWhiteSpace(dto.FontColor) ? "#FFFFFF" : dto.FontColor.Trim().ToUpperInvariant();
        var size=string.IsNullOrWhiteSpace(dto.FontSize) ? "12px" : dto.FontSize.Trim();
        var colorName=dto.ColorName.Trim();
        var style=await _db.ColorStyles.FirstOrDefaultAsync(x=>x.ColorName==colorName&&x.ColorCode==color&&x.FontColor==font&&x.FontSize==size,ct)
            ?? new ColorStyle{ColorName=colorName,ColorCode=color,FontColor=font,FontSize=size};
        entity.Process=process; entity.Status=status; entity.ColorStyle=style; entity.Code=code; entity.Description=dto.Description?.Trim();
        entity.DisplayOrder=dto.DisplayOrder; entity.IsPaid=dto.IsPaid; entity.IsActive=dto.IsActive; return null;
    }

    private async Task<string> ResolveCodeAsync(string statusName, string? requestedCode, int? ownerTenantId, int? excludingId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode))
            return requestedCode.Trim().ToUpperInvariant();

        var existingCode = await _db.ProcessStatusStyles.AsNoTracking()
            .Where(x => (!excludingId.HasValue || x.Id != excludingId.Value)
                && x.IsActive
                && x.Status.StatusName == statusName
                && (
                    (ownerTenantId == null && x.TenantId == null) ||
                    (ownerTenantId != null && (x.TenantId == ownerTenantId || x.TenantId == null))
                ))
            .OrderByDescending(x => ownerTenantId != null && x.TenantId == ownerTenantId)
            .ThenBy(x => x.DisplayOrder)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(existingCode)
            ? BuildAbbreviation(statusName)
            : existingCode.Trim().ToUpperInvariant();
    }

    private static string BuildAbbreviation(string value)
    {
        var code = string.Concat(value
            .Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0]))
            .Trim();
        if (string.IsNullOrWhiteSpace(code)) code = "STAT";
        return code.Length <= 10 ? code : code[..10];
    }
    private async Task<object?> LoadDto(int id,CancellationToken ct)
    {
        var row=await _db.ProcessStatusStyles.AsNoTracking().Include(x=>x.Process).Include(x=>x.Status).Include(x=>x.ColorStyle).FirstOrDefaultAsync(x=>x.Id==id,ct);
        return row==null?null:ToDto(row);
    }
    private StatusConfigurationDto ToDto(ProcessStatusStyle x)=>new(x.Id,x.Process.ProcessName,x.Status.StatusName,x.Code,x.Description,x.ColorStyle.ColorName,x.ColorStyle.ColorCode,x.ColorStyle.FontColor,x.ColorStyle.FontSize,x.DisplayOrder,x.IsPaid,x.IsActive,x.IsSystem,x.TenantId,x.IsSystem?"Platform default":"Company custom",_tenant.IsSuperAdmin?x.TenantId==null:x.TenantId==_tenant.TenantId);
    private StatusConfigurationDto ToDto(StatusConfigurationManagementRow x)=>new(x.Id,x.ProcessName,x.StatusName,x.Code,x.Description,x.ColorName,x.ColorCode,x.FontColor,x.FontSize,x.DisplayOrder,x.IsPaid,x.IsActive,x.IsSystem,x.TenantId,x.IsSystem?"Platform default":"Company custom",_tenant.IsSuperAdmin?x.TenantId==null:x.TenantId==_tenant.TenantId);

    private Task<ProcessStatusStyle?> EditableRow(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return _db.ProcessStatusStyles.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == null, ct);
        var tenantId = _tenant.TenantId;
        return _db.ProcessStatusStyles.FirstOrDefaultAsync(x => x.Id == id && tenantId.HasValue && x.TenantId == tenantId, ct);
    }

    private async Task<bool> CanManageAsync(string operation, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || _tenant.IsTenantAdmin) return true;
        if (!_tenant.TenantId.HasValue) return false;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId)
            .Select(x => x.Staff == null ? (Guid?)null : x.Staff.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        return await _db.StaffMenuAccesses.AsNoTracking().AnyAsync(x =>
            x.StaffId == staffId && x.IsAllow && x.Menu != null &&
            x.Menu.Route == "/settings/statuses" &&
            x.AccessFeatures.Any(f => f.IsAllow && f.Feature != null &&
                (f.Feature.FeatureKey.EndsWith("_" + operation) ||
                 (operation != "DELETE" && f.Feature.FeatureKey.EndsWith("_ALL")))), ct);
    }
}

public sealed record StatusConfigurationDto(int Id,string ProcessName,string StatusName,string Code,string? Description,string ColorName,string ColorCode,string FontColor,string FontSize,int DisplayOrder,bool IsPaid,bool IsActive,bool IsSystem,int? TenantId,string ScopeLabel,bool CanModify);
public sealed class StatusConfigurationWriteDto
{
    [Required,MaxLength(100)] public string ProcessName{get;set;}=string.Empty;
    [Required,MaxLength(100)] public string StatusName{get;set;}=string.Empty;
    [MaxLength(10)] public string Code{get;set;}=string.Empty;
    [MaxLength(500)] public string? Description{get;set;}
    [Required,MaxLength(100)] public string ColorName{get;set;}=string.Empty;
    [MaxLength(20)] public string ColorCode{get;set;}="#64748B";
    [MaxLength(20)] public string FontColor{get;set;}="#FFFFFF";
    [MaxLength(20)] public string FontSize{get;set;}="12px";
    public int DisplayOrder{get;set;} public bool IsPaid{get;set;} public bool IsActive{get;set;}=true;
}
