using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

/// <summary>
/// Copies platform settings (types + designations) from a master tenant so new
/// companies inherit the same configuration without manual data entry.
/// </summary>
public sealed class PlatformSettingsProvisioningService
{
    private static readonly string[] MasterTenantNames =
    [
        "LAL TECHNOLOGIES",
        "LAL GROUP OF TECHNOLOGIES"
    ];

    private readonly ApplicationDbContext _db;

    public PlatformSettingsProvisioningService(ApplicationDbContext db) => _db = db;

    public async Task EnsureTenantPlatformSettingsAsync(int tenantId, int? sourceTenantId = null, CancellationToken ct = default)
    {
        var sourceId = sourceTenantId ?? await ResolveMasterTenantIdAsync(ct);
        if (!sourceId.HasValue || sourceId.Value == tenantId)
        {
            await SyncDesignationsFromPlatformTypeValuesAsync(tenantId, ct);
            return;
        }

        await CopyDesignationsAsync(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<ContractType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<FrequencyType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<RateType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<AllowanceType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<TadaType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<LeaveType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<AnnouncementType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<AssessmentType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<AttendanceType>(sourceId.Value, tenantId, ct);
        await CopyPlatformTypeRowsAsync<BenefitType>(sourceId.Value, tenantId, ct);
        await SyncDesignationsFromPlatformTypeValuesAsync(tenantId, ct);
    }

    public async Task EnsureAllTenantsAsync(CancellationToken ct = default)
    {
        var masterId = await ResolveMasterTenantIdAsync(ct);
        if (!masterId.HasValue) return;

        var tenantIds = await _db.Tenants.AsNoTracking()
            .Where(tenant => tenant.IsActive)
            .Select(tenant => tenant.Id)
            .ToListAsync(ct);

        foreach (var tenantId in tenantIds)
            await EnsureTenantPlatformSettingsAsync(tenantId, masterId.Value, ct);
    }

    private async Task<int?> ResolveMasterTenantIdAsync(CancellationToken ct)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .Where(tenant => tenant.IsActive)
            .Select(tenant => new { tenant.Id, tenant.TenantName })
            .ToListAsync(ct);

        foreach (var masterName in MasterTenantNames)
        {
            var match = tenants.FirstOrDefault(tenant =>
                string.Equals(tenant.TenantName.Trim(), masterName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Id;
        }

        var designationCounts = await _db.Designations.AsNoTracking()
            .GroupBy(d => d.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var countByTenant = designationCounts.ToDictionary(row => row.Key, row => row.Count);

        return tenants
            .OrderByDescending(tenant => countByTenant.GetValueOrDefault(tenant.Id))
            .Select(tenant => (int?)tenant.Id)
            .FirstOrDefault();
    }

    private async Task CopyDesignationsAsync(int sourceTenantId, int targetTenantId, CancellationToken ct)
    {
        var sourceRows = await _db.Designations.AsNoTracking()
            .Where(d => d.TenantId == sourceTenantId)
            .ToListAsync(ct);
        if (sourceRows.Count == 0) return;

        var existingNames = await _db.Designations.AsNoTracking()
            .Where(d => d.TenantId == targetTenantId)
            .Select(d => d.Name.Trim().ToUpper())
            .ToListAsync(ct);
        var existingSet = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sourceRows)
        {
            if (existingSet.Contains(source.Name.Trim())) continue;
            _db.Designations.Add(new Designation
            {
                TenantId = targetTenantId,
                Name = source.Name.Trim(),
                AttendanceVisibilityScope = source.AttendanceVisibilityScope
            });
            existingSet.Add(source.Name.Trim());
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SyncDesignationsFromPlatformTypeValuesAsync(int tenantId, CancellationToken ct)
    {
        var categoryId = await _db.PlatformTypeCategories.AsNoTracking()
            .Where(category => category.Code == "DESIGNATION" && category.IsActive)
            .Select(category => (int?)category.Id)
            .FirstOrDefaultAsync(ct);
        if (!categoryId.HasValue) return;

        var platformValues = await _db.PlatformTypeValues.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.CategoryId == categoryId.Value && value.IsActive)
            .Select(value => value.Name)
            .ToListAsync(ct);
        if (platformValues.Count == 0) return;

        var existingNames = await _db.Designations.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .Select(d => d.Name.Trim().ToUpper())
            .ToListAsync(ct);
        var existingSet = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var name in platformValues)
        {
            var normalized = name.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || existingSet.Contains(normalized)) continue;
            _db.Designations.Add(new Designation
            {
                TenantId = tenantId,
                Name = normalized,
                AttendanceVisibilityScope = AttendanceVisibilityScope.Self
            });
            existingSet.Add(normalized);
            added = true;
        }

        if (added)
            await _db.SaveChangesAsync(ct);
    }

    private async Task CopyPlatformTypeRowsAsync<TEntity>(int sourceTenantId, int targetTenantId, CancellationToken ct)
        where TEntity : PlatformTypeTableRow, new()
    {
        var sourceRows = await _db.Set<TEntity>().AsNoTracking()
            .Where(row => row.TenantId == sourceTenantId)
            .ToListAsync(ct);
        if (sourceRows.Count == 0) return;

        var existingCodes = await _db.Set<TEntity>().AsNoTracking()
            .Where(row => row.TenantId == targetTenantId)
            .Select(row => row.Code)
            .ToListAsync(ct);
        var existingSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sourceRows)
        {
            if (existingSet.Contains(source.Code)) continue;
            _db.Set<TEntity>().Add(new TEntity
            {
                TenantId = targetTenantId,
                Name = source.Name,
                Code = source.Code,
                DisplayOrder = source.DisplayOrder,
                IsActive = source.IsActive,
                CreatedOnUtc = DateTime.UtcNow
            });
            existingSet.Add(source.Code);
        }

        await _db.SaveChangesAsync(ct);
    }
}
