using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

/// <summary>
/// Resolves menu-driven process codes, stage catalogs, and cumulative authority expansion.
/// </summary>
public sealed class MenuAuthorityService
{
    private readonly ApplicationDbContext _db;

    public MenuAuthorityService(ApplicationDbContext db) => _db = db;

    public static string ResolveProcessCode(int menuId, string? route)
    {
        var normalized = (route ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "/pay-allowances/payroll" or "/payroll")
            return "PAYROLL";
        if (normalized is "/attendance/deduction")
            return "DEDUCTION";
        return $"M{menuId}";
    }

    public static string? NormalizeActionCode(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CREATE" => "CREATE",
        "VERIFY" => "VERIFY",
        "APPROVE" => "APPROVE",
        "PAY" => "PAY",
        "PROCESS" => "PROCESS",
        "OVERTIME" => "OVERTIME",
        _ => null
    };

    public async Task<IReadOnlyList<StageDefinition>> GetStagesForMenuAsync(int menuId, string? route, CancellationToken ct)
    {
        List<MenuAuthorityAction> rows;
        try
        {
            rows = await _db.MenuAuthorityActions.AsNoTracking()
                .Where(row => row.MenuId == menuId && row.IsActive)
                .OrderBy(row => row.RankOrder)
                .ThenBy(row => row.Id)
                .ToListAsync(ct);
        }
        catch
        {
            rows = [];
        }

        if (rows.Count > 0)
        {
            return rows.Select(row => new StageDefinition(
                row.ActionCode.Trim().ToUpperInvariant(),
                row.DisplayName,
                row.RankOrder,
                row.SupportsPin,
                string.IsNullOrWhiteSpace(row.PinProcessName) ? null : row.PinProcessName.Trim())).ToList();
        }

        // New menus auto-appear with a single Approve stage until specialized stages are seeded.
        var pinName = ResolveProcessCode(menuId, route) switch
        {
            "DEDUCTION" => "DeductionAdjustment",
            "PAYROLL" => "PayrollApproval",
            _ => $"Menu{menuId}Approval"
        };
        return
        [
            new StageDefinition("APPROVE", "Approve", 1, SupportsPin: true, PinProcessName: pinName)
        ];
    }

    public async Task<IReadOnlyList<string>> ExpandAuthoritiesAsync(
        string processCode,
        IEnumerable<string> assignedActionCodes,
        CancellationToken ct)
    {
        var assigned = assignedActionCodes
            .Select(NormalizeActionCode)
            .Where(code => code != null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (assigned.Count == 0)
            return Array.Empty<string>();

        var menu = await ResolveMenuForProcessCodeAsync(processCode, ct);
        var stages = menu == null
            ? DefaultStagesForProcess(processCode)
            : await GetStagesForMenuAsync(menu.Id, menu.Route, ct);

        if (stages.Count == 0)
            return assigned;

        var rankByAction = stages.ToDictionary(stage => stage.ActionCode, stage => stage.RankOrder, StringComparer.OrdinalIgnoreCase);
        var maxRank = assigned
            .Select(code => rankByAction.TryGetValue(code, out var rank) ? rank : 0)
            .DefaultIfEmpty(0)
            .Max();

        if (maxRank <= 0)
            return assigned;

        return stages
            .Where(stage => stage.RankOrder <= maxRank)
            .Select(stage => stage.ActionCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> HasAuthorityAsync(string processCode, string actionCode, Guid staffId, CancellationToken ct)
    {
        var normalizedAction = NormalizeActionCode(actionCode);
        if (normalizedAction == null)
            return false;

        var assigned = await _db.ProcessActionAuthorities.AsNoTracking()
            .Where(row =>
                row.ProcessCode == processCode &&
                row.StaffId == staffId &&
                row.IsActive)
            .Select(row => row.ActionCode)
            .ToListAsync(ct);

        var expanded = await ExpandAuthoritiesAsync(processCode, assigned, ct);
        return expanded.Contains(normalizedAction, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Menu?> ResolveMenuForProcessCodeAsync(string processCode, CancellationToken ct)
    {
        var code = (processCode ?? string.Empty).Trim();
        if (code.Length == 0)
            return null;

        if (code.StartsWith("M", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(code[1..], out var menuId))
        {
            return await _db.Menus.AsNoTracking()
                .FirstOrDefaultAsync(menu => menu.Id == menuId && menu.IsActive, ct);
        }

        if (string.Equals(code, "PAYROLL", StringComparison.OrdinalIgnoreCase))
        {
            return await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive && menu.Route == "/pay-allowances/payroll")
                .OrderBy(menu => menu.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (string.Equals(code, "DEDUCTION", StringComparison.OrdinalIgnoreCase))
        {
            return await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive && menu.Route == "/attendance/deduction")
                .OrderBy(menu => menu.Id)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private static IReadOnlyList<StageDefinition> DefaultStagesForProcess(string processCode)
    {
        if (string.Equals(processCode, "PAYROLL", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("CREATE", "Create payroll", 1, false, null),
                new("VERIFY", "Verify payroll", 2, false, null),
                new("APPROVE", "Final approval", 3, true, "PayrollApproval"),
                new("PAY", "Salary dispatch", 4, false, null)
            ];
        }

        if (string.Equals(processCode, "DEDUCTION", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("APPROVE", "Approve adjustment", 1, true, "DeductionAdjustment"),
                new("OVERTIME", "Approve overtime", 2, true, "DeductionOvertime")
            ];
        }

        return [new("APPROVE", "Approve", 1, true, null)];
    }

    public readonly record struct StageDefinition(
        string ActionCode,
        string DisplayName,
        int RankOrder,
        bool SupportsPin,
        string? PinProcessName);
}
