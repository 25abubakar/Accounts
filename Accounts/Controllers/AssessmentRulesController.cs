using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/assessment/rules")]
[Authorize]
public sealed class AssessmentRulesController(ApplicationDbContext db, ITenantService tenant) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue || tenant.IsSuperAdmin) return Forbid();
        var rows = await db.AssessmentBonusRules.AsNoTracking()
            .OrderBy(row => row.RankNumber)
            .Select(row => new { row.Id, row.RankNumber, row.BonusAmount, row.AppliesToHigherRanks, row.IsActive })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RuleDto dto, CancellationToken ct)
    {
        if (!CanManage() || !tenant.TenantId.HasValue) return Forbid();
        var error = Validate(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.AssessmentBonusRules.AnyAsync(row => row.RankNumber == dto.RankNumber, ct))
            return Conflict(new { message = $"A bonus rule already exists for rank {dto.RankNumber}." });
        if (dto.AppliesToHigherRanks && await db.AssessmentBonusRules.AnyAsync(row => row.AppliesToHigherRanks, ct))
            return Conflict(new { message = "Only one minimum fallback rule is allowed." });
        var row = new AssessmentBonusRule { TenantId = tenant.TenantId.Value, RankNumber = dto.RankNumber,
            BonusAmount = dto.BonusAmount, AppliesToHigherRanks = dto.AppliesToHigherRanks,
            IsActive = dto.IsActive, CreatedDateUtc = DateTime.UtcNow };
        db.AssessmentBonusRules.Add(row); await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, message = "Assessment bonus rule created." });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RuleDto dto, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var error = Validate(dto); if (error != null) return BadRequest(new { message = error });
        var row = await db.AssessmentBonusRules.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (row == null) return NotFound();
        if (await db.AssessmentBonusRules.AnyAsync(item => item.Id != id && item.RankNumber == dto.RankNumber, ct))
            return Conflict(new { message = $"A bonus rule already exists for rank {dto.RankNumber}." });
        if (dto.AppliesToHigherRanks && await db.AssessmentBonusRules.AnyAsync(item => item.Id != id && item.AppliesToHigherRanks, ct))
            return Conflict(new { message = "Only one minimum fallback rule is allowed." });
        row.RankNumber = dto.RankNumber; row.BonusAmount = dto.BonusAmount;
        row.AppliesToHigherRanks = dto.AppliesToHigherRanks; row.IsActive = dto.IsActive;
        row.ModifiedDateUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        return Ok(new { message = "Assessment bonus rule updated." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var row = await db.AssessmentBonusRules.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (row == null) return NotFound();
        db.AssessmentBonusRules.Remove(row); await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private bool CanManage() => tenant.IsTenantAdmin || User.IsInRole("TenantAdmin");
    private static string? Validate(RuleDto dto) => dto.RankNumber < 1 ? "Rank must be 1 or higher."
        : dto.BonusAmount < 0 ? "Bonus amount cannot be negative." : null;
    public sealed class RuleDto
    {
        public int RankNumber { get; set; }
        public decimal BonusAmount { get; set; }
        public bool AppliesToHigherRanks { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
