using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Security.Claims;

namespace Accounts.Controllers;

[ApiController]
[Route("api/process-category-approvers")]
[Authorize]
public sealed class ProcessCategoryApproversController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly RbacService _rbac;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly MenuAuthorityService _menuAuthority;

    public ProcessCategoryApproversController(
        ApplicationDbContext db,
        ITenantService tenant,
        RbacService rbac,
        TenantPermissionService tenantPermissions,
        MenuAuthorityService menuAuthority)
    {
        _db = db;
        _tenant = tenant;
        _rbac = rbac;
        _tenantPermissions = tenantPermissions;
        _menuAuthority = menuAuthority;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue)
            return Forbid();

        var tenantId = _tenant.TenantId.Value;

        var categories = await QueryAsync(
            """
            SELECT Id, Code, Name, DisplayOrder
            FROM dbo.ProcessWorkflowCategories
            WHERE IsActive = 1
            ORDER BY DisplayOrder
            """,
            null,
            reader => new CategoryRow
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Code = reader.GetString(reader.GetOrdinal("Code")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder"))
            }, ct);

        var assignments = await QueryAsync(
                """
                SELECT
                    pca.Id,
                    pca.CategoryId,
                    cat.Code       AS CategoryCode,
                    cat.Name       AS CategoryName,
                    pca.StaffId,
                    per.FullName   AS StaffName,
                    sv.LoginId     AS StaffNumber,
                    org.Name       AS Department,
                    jt.TitleName   AS Designation,
                    per.ProfilePhotoUrl
                FROM dbo.ProcessCategoryApprovers pca
                JOIN dbo.ProcessWorkflowCategories cat ON cat.Id = pca.CategoryId
                JOIN dbo.StaffVacancy sv               ON sv.StaffId = pca.StaffId
                JOIN dbo.Persons per                   ON per.PersonId = sv.PersonId
                LEFT JOIN dbo.Vacancies v              ON v.VacancyId = sv.VacancyId
                LEFT JOIN dbo.OrganizationTree org     ON org.Id = v.OrganizationId AND org.Label = N'Department'
                LEFT JOIN dbo.JobTitles jt             ON jt.Id = v.JobTitleId
                WHERE pca.TenantId = @tenantId
                ORDER BY cat.DisplayOrder, per.FullName
                """,
                command => AddParameter(command, "@tenantId", tenantId),
                reader => new AssignmentRow
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                    CategoryCode = reader.GetString(reader.GetOrdinal("CategoryCode")),
                    CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                    StaffId = reader.GetGuid(reader.GetOrdinal("StaffId")),
                    StaffName = reader.GetString(reader.GetOrdinal("StaffName")),
                    StaffNumber = GetNullableString(reader, "StaffNumber"),
                    Department = GetNullableString(reader, "Department"),
                    Designation = GetNullableString(reader, "Designation"),
                    ProfilePhotoUrl = GetNullableString(reader, "ProfilePhotoUrl")
                }, ct);

        var actionAuthorities = await (
            from authority in _db.ProcessActionAuthorities.AsNoTracking()
            join staff in _db.StaffVacancies.AsNoTracking() on authority.StaffId equals staff.StaffId
            join person in _db.Persons.AsNoTracking() on staff.PersonId equals person.PersonId
            where authority.TenantId == tenantId && authority.IsActive
            orderby authority.ProcessCode, authority.ActionCode, person.FullName
            select new
            {
                authority.Id,
                authority.ProcessCode,
                authority.ActionCode,
                authority.StaffId,
                StaffName = person.FullName,
                StaffNumber = staff.LoginId,
                person.ProfilePhotoUrl
            }).ToListAsync(ct);

        var menus = (await _db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive)
            .OrderBy(menu => menu.SortOrder)
            .ThenBy(menu => menu.Title)
            .Select(menu => new { menu.Id, menu.Title, menu.Route, menu.ParentId, menu.SortOrder })
            .ToListAsync(ct))
            .Select(menu => new MenuNode(menu.Id, menu.Title, menu.Route, menu.ParentId, menu.SortOrder))
            .ToList();

        List<string> pinNames;
        try
        {
            pinNames = await _db.MenuAuthorityActions.AsNoTracking()
                .Where(row => row.IsActive && row.SupportsPin && row.PinProcessName != null)
                .Select(row => row.PinProcessName!)
                .Distinct()
                .ToListAsync(ct);
        }
        catch
        {
            // Table may not exist yet on an older DB — still return menu modules.
            pinNames = [];
        }

        pinNames.AddRange(["DeductionAdjustment", "DeductionOvertime", "CameraAttendance", "PayrollApproval"]);
        pinNames = pinNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Dictionary<string, int> pinByProcess;
        try
        {
            var pinRows = await _db.ProcessApprovalCodes.AsNoTracking()
                .Where(code => code.TenantId == tenantId && pinNames.Contains(code.ProcessName))
                .Select(code => new { code.ProcessName, code.PinCode })
                .ToListAsync(ct);
            pinByProcess = pinRows.ToDictionary(row => row.ProcessName, row => row.PinCode, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            pinByProcess = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var modules = new List<object>();
        var approvalProcesses = new List<object>();
        foreach (var root in menus.Where(menu => menu.ParentId == null))
        {
            var children = FlattenMenuChildren(root.Id, menus)
                .Where(child => !string.IsNullOrWhiteSpace(child.Route))
                .ToList();

            // Root itself can be a screen (e.g. Dashboard).
            if (!string.IsNullOrWhiteSpace(root.Route) &&
                children.All(child => child.Id != root.Id))
            {
                children.Insert(0, root);
            }

            if (children.Count == 0)
                continue;

            var childPayload = new List<object>();
            foreach (var child in children)
            {
                var processCode = MenuAuthorityService.ResolveProcessCode(child.Id, child.Route);
                IReadOnlyList<MenuAuthorityService.StageDefinition> stages;
                try
                {
                    stages = await _menuAuthority.GetStagesForMenuAsync(child.Id, child.Route, ct);
                }
                catch
                {
                    stages = [new MenuAuthorityService.StageDefinition("APPROVE", "Approve", 1, true, $"Menu{child.Id}Approval")];
                }

                var actions = stages.Select(stage =>
                {
                    var pinName = stage.PinProcessName;
                    var pinConfigured = !string.IsNullOrWhiteSpace(pinName) &&
                                        pinByProcess.TryGetValue(pinName!, out var pin) &&
                                        pin > 0;
                    if (stage.SupportsPin)
                    {
                        approvalProcesses.Add(new
                        {
                            categoryCode = processCode,
                            code = stage.ActionCode,
                            name = stage.DisplayName,
                            processName = pinName ?? $"{processCode}{stage.ActionCode}",
                            menuRoute = child.Route,
                            requirePin = pinConfigured,
                            pinConfigured
                        });
                    }

                    return new
                    {
                        actionCode = stage.ActionCode,
                        displayName = stage.DisplayName,
                        rankOrder = stage.RankOrder,
                        supportsPin = stage.SupportsPin,
                        pinProcessName = pinName,
                        requirePin = pinConfigured,
                        pinConfigured
                    };
                }).ToList();

                childPayload.Add(new
                {
                    id = child.Id,
                    title = child.Title,
                    route = child.Route,
                    processCode,
                    parentPath = BuildParentPath(child.ParentId, menus),
                    actions
                });
            }

            modules.Add(new
            {
                id = root.Id,
                title = root.Title,
                children = childPayload
            });
        }

        return Ok(new
        {
            categories,
            assignments,
            actionAuthorities,
            approvalProcesses,
            modules,
            moduleCount = modules.Count
        });
    }

    [HttpGet("staff")]
    public async Task<IActionResult> GetStaff(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue)
            return Forbid();

        var tenantId = _tenant.TenantId.Value;

        var rows = await QueryAsync(
                """
                SELECT
                    sv.StaffId,
                    per.FullName,
                    sv.LoginId        AS EmployeeId,
                    per.ProfilePhotoUrl,
                    org.Name          AS Department,
                    jt.TitleName      AS Designation
                FROM dbo.StaffVacancy sv
                JOIN dbo.Persons per            ON per.PersonId  = sv.PersonId
                LEFT JOIN dbo.Vacancies v       ON v.VacancyId = sv.VacancyId
                LEFT JOIN dbo.OrganizationTree org ON org.Id = v.OrganizationId AND org.Label = N'Department'
                LEFT JOIN dbo.JobTitles jt      ON jt.Id = v.JobTitleId
                WHERE sv.TenantId = @tenantId AND per.IsActive = 1
                ORDER BY per.FullName
                """,
                command => AddParameter(command, "@tenantId", tenantId),
                reader => new StaffPickerRow
                {
                    StaffId = reader.GetGuid(reader.GetOrdinal("StaffId")),
                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                    EmployeeId = GetNullableString(reader, "EmployeeId"),
                    ProfilePhotoUrl = GetNullableString(reader, "ProfilePhotoUrl"),
                    Department = GetNullableString(reader, "Department"),
                    Designation = GetNullableString(reader, "Designation")
                }, ct);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] AssignApproverDto dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue)
            return Forbid();

        var tenantId = _tenant.TenantId.Value;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var categoryExists = await ExistsAsync(
            "SELECT 1 FROM dbo.ProcessWorkflowCategories WHERE Id = @categoryId AND IsActive = 1",
            command => AddParameter(command, "@categoryId", dto.CategoryId), ct);
        if (!categoryExists)
            return BadRequest(new { message = "Category not found." });

        var staffExists = await ExistsAsync(
            "SELECT 1 FROM dbo.StaffVacancy WHERE StaffId = @staffId AND TenantId = @tenantId",
            command =>
            {
                AddParameter(command, "@staffId", dto.StaffId);
                AddParameter(command, "@tenantId", tenantId);
            }, ct);
        if (!staffExists)
            return BadRequest(new { message = "Staff member not found in this tenant." });

        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO dbo.ProcessCategoryApprovers (TenantId, CategoryId, StaffId, CreatedByUserId)
                SELECT {0}, {1}, {2}, {3}
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.ProcessCategoryApprovers
                    WHERE TenantId = {0} AND CategoryId = {1} AND StaffId = {2}
                )
                """,
                tenantId, dto.CategoryId, dto.StaffId, userId);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Unique constraint race — harmless
        }

        return Ok(new { message = "Approver assigned successfully." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue)
            return Forbid();

        var tenantId = _tenant.TenantId.Value;

        var affected = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM dbo.ProcessCategoryApprovers WHERE Id = {0} AND TenantId = {1}",
            id, tenantId);

        if (affected == 0)
            return NotFound(new { message = "Assignment not found." });

        return Ok(new { message = "Approver removed." });
    }

    /// <summary>
    /// Set or clear PIN for an approval process (e.g. DeductionAdjustment).
    /// Never returns the existing PIN value to the client.
    /// </summary>
    [HttpPost("pin-settings")]
    public async Task<IActionResult> SavePinSettings([FromBody] SavePinSettingsDto dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue)
            return Forbid();

        var process = await ResolvePinProcessNameAsync(dto.ProcessName, ct);
        if (string.IsNullOrWhiteSpace(process))
            return BadRequest(new { message = "Select a valid approval process that supports a PIN." });

        var tenantId = _tenant.RequiredTenantId;
        var existing = await _db.ProcessApprovalCodes
            .FirstOrDefaultAsync(code => code.TenantId == tenantId && code.ProcessName == process, ct);

        if (!dto.RequirePin)
        {
            if (existing != null)
                _db.ProcessApprovalCodes.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "PIN requirement cleared for this process." });
        }

        var hasNewPin = dto.PinCode is >= 1000 and <= 99999999;
        if (!hasNewPin)
        {
            if (existing != null && existing.PinCode > 0)
                return Ok(new { message = "PIN requirement already active for this process." });
            return BadRequest(new { message = "Enter a PIN between 4 and 8 digits." });
        }

        if (existing == null)
        {
            _db.ProcessApprovalCodes.Add(new ProcessApprovalCode
            {
                TenantId = tenantId,
                ProcessName = process,
                PinCode = dto.PinCode!.Value
            });
        }
        else
        {
            existing.PinCode = dto.PinCode!.Value;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "PIN saved for this approval process." });
    }

    [HttpGet("my-authorities")]
    public async Task<IActionResult> MyAuthorities([FromQuery] string processCode, [FromQuery] int? menuId, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        string? normalizedProcess = null;
        if (menuId.HasValue)
        {
            var menu = await _db.Menus.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == menuId.Value && item.IsActive, ct);
            if (menu == null) return BadRequest(new { message = "Menu not found." });
            normalizedProcess = MenuAuthorityService.ResolveProcessCode(menu.Id, menu.Route);
        }
        else
        {
            normalizedProcess = await NormalizeProcessCodeAsync(processCode, ct);
        }

        if (normalizedProcess == null)
            return BadRequest(new { message = "A valid process code or menu is required." });

        var staffId = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == userId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return Ok(Array.Empty<string>());

        var assigned = await _db.ProcessActionAuthorities.AsNoTracking()
            .Where(authority => authority.ProcessCode == normalizedProcess && authority.StaffId == staffId.Value && authority.IsActive)
            .Select(authority => authority.ActionCode)
            .ToListAsync(ct);

        var expanded = await _menuAuthority.ExpandAuthoritiesAsync(normalizedProcess, assigned, ct);
        return Ok(expanded);
    }

    [HttpPost("action-authorities")]
    public async Task<IActionResult> AssignActionAuthority([FromBody] AssignActionAuthorityDto dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue) return Forbid();

        string? processCode = null;
        Menu? menu = null;
        if (dto.MenuId.HasValue)
        {
            menu = await _db.Menus.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == dto.MenuId.Value && item.IsActive, ct);
            if (menu == null || string.IsNullOrWhiteSpace(menu.Route))
                return BadRequest(new { message = "Select a valid child menu screen." });
            processCode = MenuAuthorityService.ResolveProcessCode(menu.Id, menu.Route);
        }
        else
        {
            processCode = await NormalizeProcessCodeAsync(dto.ProcessCode, ct);
            menu = processCode == null ? null : await _menuAuthority.ResolveMenuForProcessCodeAsync(processCode, ct);
        }

        var actionCode = MenuAuthorityService.NormalizeActionCode(dto.ActionCode);
        if (processCode == null || actionCode == null)
            return BadRequest(new { message = "Select a valid module screen and workflow stage." });

        if (menu != null)
        {
            var stages = await _menuAuthority.GetStagesForMenuAsync(menu.Id, menu.Route, ct);
            if (!stages.Any(stage => string.Equals(stage.ActionCode, actionCode, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new { message = "That stage is not available on the selected screen." });
        }

        if (!await _db.StaffVacancies.AsNoTracking().AnyAsync(staff => staff.StaffId == dto.StaffId && staff.TenantId == _tenant.RequiredTenantId, ct))
            return BadRequest(new { message = "Staff member not found in this tenant." });

        var existing = await _db.ProcessActionAuthorities
            .SingleOrDefaultAsync(authority => authority.ProcessCode == processCode && authority.ActionCode == actionCode && authority.StaffId == dto.StaffId, ct);
        if (existing == null)
        {
            _db.ProcessActionAuthorities.Add(new ProcessActionAuthority
            {
                TenantId = _tenant.RequiredTenantId,
                ProcessCode = processCode,
                ActionCode = actionCode,
                StaffId = dto.StaffId,
                IsActive = true,
                CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            });
        }
        else
        {
            existing.IsActive = true;
        }

        // Keep Deduction category approver list in sync for maker-checker.
        if (string.Equals(processCode, "DEDUCTION", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionCode, "APPROVE", StringComparison.OrdinalIgnoreCase))
        {
            await SyncDeductionCategoryApproverAsync(dto.StaffId, ct);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Process authority assigned successfully.", processCode, actionCode });
    }

    [HttpDelete("action-authorities/{id:int}")]
    public async Task<IActionResult> RemoveActionAuthority(int id, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue) return Forbid();
        var authority = await _db.ProcessActionAuthorities.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (authority == null) return NotFound(new { message = "Process authority assignment not found." });
        _db.ProcessActionAuthorities.Remove(authority);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Process authority removed." });
    }

    private async Task<string?> NormalizeProcessCodeAsync(string? value, CancellationToken ct)
    {
        var code = (value ?? string.Empty).Trim();
        if (code.Length == 0) return null;
        if (string.Equals(code, "PAYROLL", StringComparison.OrdinalIgnoreCase)) return "PAYROLL";
        if (string.Equals(code, "DEDUCTION", StringComparison.OrdinalIgnoreCase)) return "DEDUCTION";
        if (code.StartsWith("M", StringComparison.OrdinalIgnoreCase) && int.TryParse(code[1..], out var menuId))
        {
            var exists = await _db.Menus.AsNoTracking().AnyAsync(menu => menu.Id == menuId && menu.IsActive, ct);
            return exists ? $"M{menuId}" : null;
        }

        var byRoute = await _db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive && menu.Route != null && menu.Route == code)
            .Select(menu => new { menu.Id, menu.Route })
            .FirstOrDefaultAsync(ct);
        return byRoute == null ? null : MenuAuthorityService.ResolveProcessCode(byRoute.Id, byRoute.Route);
    }

    private async Task<string?> ResolvePinProcessNameAsync(string? value, CancellationToken ct)
    {
        var key = (value ?? string.Empty).Trim();
        if (key.Length == 0) return null;

        var fromTable = await _db.MenuAuthorityActions.AsNoTracking()
            .Where(row => row.IsActive && row.SupportsPin && row.PinProcessName != null &&
                          (row.PinProcessName == key || row.ActionCode == key))
            .Select(row => row.PinProcessName)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(fromTable))
            return fromTable;

        // Backward-compatible known PIN keys.
        string[] known = ["DeductionAdjustment", "DeductionOvertime", "CameraAttendance", "PayrollApproval", "LeaveApproval", "HrApproval", "GeneralApproval"];
        return known.FirstOrDefault(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SyncDeductionCategoryApproverAsync(Guid staffId, CancellationToken ct)
    {
        var categoryId = await ExistsScalarIntAsync(
            "SELECT TOP 1 Id FROM dbo.ProcessWorkflowCategories WHERE Code = N'DEDUCTION' AND IsActive = 1",
            ct);
        if (categoryId is null or 0) return;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantId = _tenant.RequiredTenantId;
        await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.ProcessCategoryApprovers (TenantId, CategoryId, StaffId, CreatedByUserId)
            SELECT {0}, {1}, {2}, {3}
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.ProcessCategoryApprovers
                WHERE TenantId = {0} AND CategoryId = {1} AND StaffId = {2}
            )
            """,
            tenantId, categoryId.Value, staffId, userId ?? (object)DBNull.Value);
    }

    private async Task<int?> ExistsScalarIntAsync(string sql, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(ct);
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }

    private static List<MenuNode> FlattenMenuChildren(int parentId, IReadOnlyList<MenuNode> all)
    {
        var result = new List<MenuNode>();
        var queue = new Queue<int>();
        queue.Enqueue(parentId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in all.Where(menu => menu.ParentId == current))
            {
                result.Add(child);
                queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    private static string BuildParentPath(int? parentId, IReadOnlyList<MenuNode> all)
    {
        if (!parentId.HasValue) return string.Empty;
        var parts = new List<string>();
        var current = parentId;
        var guard = 0;
        while (current.HasValue && guard++ < 20)
        {
            var node = all.FirstOrDefault(menu => menu.Id == current.Value);
            if (node == null) break;
            parts.Insert(0, node.Title);
            current = node.ParentId;
        }
        return string.Join(" / ", parts);
    }

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
    {
        const string route = "/hr/process/report";
        if (!_tenant.TenantId.HasValue || TenantPermissionService.IsSuperAdmin(User)) return Forbid();
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [route], action, ct) ? null : Forbid();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == userId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        var menuId = await _db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive && menu.Route == route)
            .Select(menu => (int?)menu.Id)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return Forbid();
        if (action == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return null;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{action}") ? null : Forbid();
    }

    private async Task<List<T>> QueryAsync<T>(
        string sql,
        Action<DbCommand>? configure,
        Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            configure?.Invoke(command);

            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add(map(reader));
            return rows;
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }

    private async Task<bool> ExistsAsync(string sql, Action<DbCommand> configure, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            configure(command);
            return await command.ExecuteScalarAsync(ct) != null;
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string? GetNullableString(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}

file sealed class CategoryRow
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

file sealed class AssignmentRow
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public Guid StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string? StaffNumber { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? ProfilePhotoUrl { get; set; }
}

file sealed class StaffPickerRow
{
    public Guid StaffId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
}

public sealed class AssignApproverDto
{
    public int CategoryId { get; set; }
    public Guid StaffId { get; set; }
}

public sealed class AssignActionAuthorityDto
{
    public int? MenuId { get; set; }
    public string ProcessCode { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public Guid StaffId { get; set; }
}

public sealed class SavePinSettingsDto
{
    /// <summary>ProcessName (e.g. DeductionAdjustment) or catalog Code (e.g. ADJUSTMENT).</summary>
    public string ProcessName { get; set; } = string.Empty;
    public bool RequirePin { get; set; }
    public int? PinCode { get; set; }
}

sealed class MenuNode
{
    public MenuNode(int id, string title, string? route, int? parentId, int sortOrder)
    {
        Id = id;
        Title = title;
        Route = route;
        ParentId = parentId;
        SortOrder = sortOrder;
    }

    public int Id { get; }
    public string Title { get; }
    public string? Route { get; }
    public int? ParentId { get; }
    public int SortOrder { get; }
}

sealed class AuthorityModuleDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<AuthorityChildDto> Children { get; set; } = [];
}

sealed class AuthorityChildDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Route { get; set; }
    public string ProcessCode { get; set; } = string.Empty;
    public string ParentPath { get; set; } = string.Empty;
    public List<AuthorityActionDto> Actions { get; set; } = [];
}

sealed class AuthorityActionDto
{
    public string ActionCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int RankOrder { get; set; }
    public bool SupportsPin { get; set; }
    public string? PinProcessName { get; set; }
    public bool RequirePin { get; set; }
    public bool PinConfigured { get; set; }
}
