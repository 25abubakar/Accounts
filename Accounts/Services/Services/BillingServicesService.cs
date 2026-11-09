using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class BillingServicesService : IBillingServicesService
{
    private readonly ApplicationDbContext _db;

    public BillingServicesService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BillingReProcessRowDto>> ListReProcessAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingReProcessRowDto>(
            _db,
            "EXEC dbo.usp_Billing_ReProcess_List @TenantId, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    public async Task<IReadOnlyList<BillingCptsProcessedRowDto>> ListCptsProcessedAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingCptsProcessedRowDto>(
            _db,
            "EXEC dbo.usp_Billing_CptsProcessed_List @TenantId, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    public async Task<IReadOnlyList<BillingArReportRowDto>> ListArReportAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingArReportRowDto>(
            _db,
            "EXEC dbo.usp_Billing_ArReport_List @TenantId, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    public async Task<IReadOnlyList<BillingStatusRuleDto>> ListStatusRulesAsync(
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                r.Id,
                r.RuleGroup AS RuleKey,
                r.StatusId,
                COALESCE(s.StatusName, '') AS StatusName,
                r.MixedDefaultStatusId,
                r.SortOrder,
                r.IsActive
            FROM dbo.tblEobStatusNeglectRule r WITH (NOLOCK)
            LEFT JOIN dbo.tblStatus s WITH (NOLOCK) ON r.StatusId = s.Id
            ORDER BY r.RuleGroup, r.SortOrder, r.StatusId;";

        return await _db.Database.SqlQueryRaw<BillingStatusRuleDto>(sql).ToListAsync(ct);
    }

    public async Task<BillingStatusRuleDto?> GetStatusRuleByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                r.Id,
                r.RuleGroup AS RuleKey,
                r.StatusId,
                COALESCE(s.StatusName, '') AS StatusName,
                r.MixedDefaultStatusId,
                r.SortOrder,
                r.IsActive
            FROM dbo.tblEobStatusNeglectRule r WITH (NOLOCK)
            LEFT JOIN dbo.tblStatus s WITH (NOLOCK) ON r.StatusId = s.Id
            WHERE r.Id = {0};";

        return await _db.Database.SqlQueryRaw<BillingStatusRuleDto>(sql, id).FirstOrDefaultAsync(ct);
    }

    public async Task<BillingServiceActionResultDto> SaveStatusRuleAsync(
        BillingStatusRuleSaveRequestDto request,
        string? userName,
        CancellationToken ct = default)
    {
        if (request == null)
        {
            return new BillingServiceActionResultDto { Success = false, Message = "Invalid request." };
        }

        if (request.StatusId <= 0)
        {
            return new BillingServiceActionResultDto { Success = false, Message = "Please select valid status." };
        }

        var normalizedRuleKey = string.IsNullOrWhiteSpace(request.RuleKey) ? null : request.RuleKey.Trim();

        bool alreadyExists = await _db.BillingEobStatusRules
            .AnyAsync(x => x.Id != request.Id
                        && x.RuleGroup == normalizedRuleKey
                        && x.StatusId == request.StatusId
                        && x.IsActive, ct);

        if (alreadyExists)
        {
            return new BillingServiceActionResultDto
            {
                Success = false,
                Message = "This status already exists for this rule key."
            };
        }

        if (request.Id > 0)
        {
            var entity = await _db.BillingEobStatusRules
                .FirstOrDefaultAsync(x => x.Id == request.Id, ct);

            if (entity == null)
            {
                return new BillingServiceActionResultDto { Success = false, Message = "Rule not found." };
            }

            entity.RuleGroup = normalizedRuleKey;
            entity.StatusId = request.StatusId;
            entity.MixedDefaultStatusId = request.MixedDefaultStatusId;
            entity.SortOrder = request.SortOrder;
            entity.IsActive = request.IsActive.GetValueOrDefault(true);
            entity.UpdatedBy = userName;
            entity.UpdatedDate = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return new BillingServiceActionResultDto
            {
                Success = true,
                Message = "Rule updated successfully.",
                Id = entity.Id
            };
        }
        else
        {
            var entity = new BillingEobStatusRule
            {
                RuleGroup = normalizedRuleKey,
                StatusId = request.StatusId,
                MixedDefaultStatusId = request.MixedDefaultStatusId,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive.GetValueOrDefault(true),
                CreatedBy = userName,
                CreatedDate = DateTime.UtcNow
            };

            _db.BillingEobStatusRules.Add(entity);
            await _db.SaveChangesAsync(ct);

            return new BillingServiceActionResultDto
            {
                Success = true,
                Message = "Rule created successfully.",
                Id = entity.Id
            };
        }
    }

    public async Task<BillingServiceActionResultDto> DeleteStatusRuleAsync(
        int id,
        string? userName,
        CancellationToken ct = default)
    {
        var entity = await _db.BillingEobStatusRules
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity == null)
        {
            return new BillingServiceActionResultDto { Success = false, Message = "Rule not found." };
        }

        entity.IsActive = false;
        entity.UpdatedBy = userName;
        entity.UpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return new BillingServiceActionResultDto
        {
            Success = true,
            Message = "Rule removed successfully.",
            Id = entity.Id
        };
    }

    public async Task<IReadOnlyList<BillingStatusOptionDto>> ListStatusOptionsAsync(
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT DISTINCT 
                ps.StatusId AS Id, 
                COALESCE(s.StatusName, '') AS Text
            FROM dbo.tblProcessStatus ps WITH (NOLOCK)
            INNER JOIN dbo.tblStatus s WITH (NOLOCK) ON ps.StatusId = s.Id
            WHERE ps.ProcessId = 20 AND ps.StatusId IS NOT NULL
            ORDER BY Text ASC;";

        return await _db.Database.SqlQueryRaw<BillingStatusOptionDto>(sql).ToListAsync(ct);
    }

    // ── Child 4: Call List ──────────────────────────────────────────────────────
    public async Task<IReadOnlyList<BillingCallListRowDto>> ListCallListAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingCallListRowDto>(
            _db,
            "EXEC dbo.usp_Billing_CallList_List @TenantId, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    // ── Child 5: Closed List ────────────────────────────────────────────────────
    public async Task<IReadOnlyList<BillingClosedListRowDto>> ListClosedListAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingClosedListRowDto>(
            _db,
            "EXEC dbo.usp_Billing_ClosedList_List @TenantId, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    // ── Child 6: Master List ────────────────────────────────────────────────────
    public async Task<IReadOnlyList<BillingMasterListRowDto>> ListMasterListAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingMasterListRowDto>(
            _db,
            "EXEC dbo.usp_Billing_PatientMaster_List @TenantId = @TenantId, @DateFrom = @DateFrom, @DateTo = @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    // ── Child 8: Mics Report ────────────────────────────────────────────────────
    public async Task<IReadOnlyList<BillingMicsReportRowDto>> ListMicsReportAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingMicsReportRowDto>(
            _db,
            "EXEC dbo.usp_Billing_MicsReport_List @TenantId, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    // ── Child 9: Claim Master List ──────────────────────────────────────────────
    public async Task<IReadOnlyList<BillingClaimMasterListRowDto>> ListClaimMasterListAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingClaimMasterListRowDto>(
            _db,
            "EXEC dbo.usp_Billing_ClaimMasterList_List @TenantId, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    // ── Child 11: Code Usage ────────────────────────────────────────────────────
    public async Task<IReadOnlyList<BillingCodeUsageRowDto>> ListCodeUsageAsync(
        int tenantId,
        string code,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingCodeUsageRowDto>(
            _db,
            "EXEC dbo.usp_Billing_CodeUsage_List @TenantId, @Code, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.NVarChar("@Code", code ?? string.Empty),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }
}

