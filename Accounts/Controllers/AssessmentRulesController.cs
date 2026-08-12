using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/assessment/rules")]
[Authorize]
public sealed class AssessmentRulesController(
    ApplicationDbContext db,
    ITenantService tenant,
    TenantPermissionService tenantPermissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue || tenant.IsSuperAdmin ||
            !await CanManageAsync("VIEW", ct)) return Forbid();
        await AssessmentSchema.EnsureCurrentAsync(db);
        var rows = await db.AssessmentBonusRules.AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.RankNumber, row.BonusAmount, row.DecrementAmount, row.MinimumBonusAmount, row.AppliesToHigherRanks, row.IsActive })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RuleDto dto, CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue || !await CanManageAsync("ADD", ct)) return Forbid();
        await AssessmentSchema.EnsureCurrentAsync(db);
        var error = Validate(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.AssessmentBonusRules.AnyAsync(ct))
            return Conflict(new { message = "Only one assessment bonus rule is allowed. Edit the existing rule." });
        var row = new AssessmentBonusRule { TenantId = tenant.TenantId.Value, RankNumber = 1,
            BonusAmount = dto.BonusAmount, DecrementAmount = dto.DecrementAmount, MinimumBonusAmount = dto.MinimumBonusAmount, AppliesToHigherRanks = true,
            IsActive = dto.IsActive, CreatedDateUtc = DateTime.UtcNow };
        db.AssessmentBonusRules.Add(row); await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, message = "Assessment bonus rule created." });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RuleDto dto, CancellationToken ct)
    {
        if (!await CanManageAsync("EDIT", ct)) return Forbid();
        await AssessmentSchema.EnsureCurrentAsync(db);
        var error = Validate(dto); if (error != null) return BadRequest(new { message = error });
        var row = await db.AssessmentBonusRules.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (row == null) return NotFound();
        row.RankNumber = 1; row.BonusAmount = dto.BonusAmount;
        row.DecrementAmount = dto.DecrementAmount; row.MinimumBonusAmount = dto.MinimumBonusAmount;
        row.AppliesToHigherRanks = true; row.IsActive = dto.IsActive;
        row.ModifiedDateUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        return Ok(new { message = "Assessment bonus rule updated." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!await CanManageAsync("DELETE", ct)) return Forbid();
        var row = await db.AssessmentBonusRules.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (row == null) return NotFound();
        db.AssessmentBonusRules.Remove(row); await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> CanManageAsync(string action, CancellationToken ct) =>
        tenantPermissions.HasMenuRouteAsync(User, ["/assessment/rules"], action, ct);
    private static string? Validate(RuleDto dto) => dto.BonusAmount < 0 ? "Base bonus cannot be negative."
        : dto.DecrementAmount < 0 ? "Decrement cannot be negative."
        : dto.MinimumBonusAmount < 0 ? "Minimum bonus cannot be negative."
        : dto.MinimumBonusAmount > dto.BonusAmount ? "Minimum bonus cannot exceed the base bonus." : null;
    public sealed class RuleDto
    {
        public int RankNumber { get; set; }
        public decimal BonusAmount { get; set; }
        public decimal DecrementAmount { get; set; }
        public decimal MinimumBonusAmount { get; set; }
        public bool AppliesToHigherRanks { get; set; }
        public bool IsActive { get; set; } = true;
    }
}

