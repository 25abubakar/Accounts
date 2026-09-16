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

    public ProcessCategoryApproversController(
        ApplicationDbContext db,
        ITenantService tenant,
        RbacService rbac,
        TenantPermissionService tenantPermissions)
    {
        _db = db;
        _tenant = tenant;
        _rbac = rbac;
        _tenantPermissions = tenantPermissions;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue)
            return Forbid();

        var tenantId = _tenant.TenantId.Value;
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

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

        var actionAuthorities = await QueryAsync(
                """
                SELECT
                    paa.Id,
                    paa.ProcessCode,
                    paa.ActionCode,
                    paa.StaffId,
                    per.FullName AS StaffName,
                    sv.LoginId AS StaffNumber,
                    per.ProfilePhotoUrl,
                    org.Name AS Department,
                    jt.TitleName AS Designation,
                    CAST(CASE WHEN paa.PinHash IS NOT NULL THEN 1 ELSE 0 END AS bit) AS PinConfigured,
                    CAST(CASE WHEN per.IdentityUserId = @currentUserId THEN 1 ELSE 0 END AS bit) AS IsCurrentUser
                FROM dbo.ProcessActionAuthorities paa
                JOIN dbo.StaffVacancy sv ON sv.StaffId = paa.StaffId
                JOIN dbo.Persons per ON per.PersonId = sv.PersonId
                LEFT JOIN dbo.Vacancies v ON v.VacancyId = sv.VacancyId
                LEFT JOIN dbo.OrganizationTree org ON org.Id = v.OrganizationId AND org.Label = N'Department'
                LEFT JOIN dbo.JobTitles jt ON jt.Id = v.JobTitleId
                WHERE paa.TenantId = @tenantId AND paa.IsActive = 1
                ORDER BY paa.ProcessCode, paa.ActionCode, per.FullName
                """,
                command =>
                {
                    AddParameter(command, "@tenantId", tenantId);
                    AddParameter(command, "@currentUserId", (object?)currentUserId ?? DBNull.Value);
                },
                reader => new ActionAuthorityRow
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    ProcessCode = reader.GetString(reader.GetOrdinal("ProcessCode")),
                    ActionCode = reader.GetString(reader.GetOrdinal("ActionCode")),
                    StaffId = reader.GetGuid(reader.GetOrdinal("StaffId")),
                    StaffName = reader.GetString(reader.GetOrdinal("StaffName")),
                    StaffNumber = GetNullableString(reader, "StaffNumber"),
                    ProfilePhotoUrl = GetNullableString(reader, "ProfilePhotoUrl"),
                    Department = GetNullableString(reader, "Department"),
                    Designation = GetNullableString(reader, "Designation"),
                    PinConfigured = reader.GetBoolean(reader.GetOrdinal("PinConfigured")),
                    IsCurrentUser = reader.GetBoolean(reader.GetOrdinal("IsCurrentUser"))
                }, ct);

        return Ok(new { categories, assignments, actionAuthorities });
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

    [HttpGet("my-authorities")]
    public async Task<IActionResult> MyAuthorities([FromQuery] string processCode, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        var normalizedProcess = NormalizeProcessCode(processCode);
        if (normalizedProcess == null) return BadRequest(new { message = "A valid process code is required." });

        var staffId = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == userId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return Ok(Array.Empty<string>());

        var actions = await _db.ProcessActionAuthorities.AsNoTracking()
            .Where(authority => authority.ProcessCode == normalizedProcess && authority.StaffId == staffId.Value && authority.IsActive)
            .Select(authority => authority.ActionCode)
            .Distinct()
            .OrderBy(action => action)
            .ToListAsync(ct);
        return Ok(actions);
    }

    [HttpPost("action-authorities")]
    public async Task<IActionResult> AssignActionAuthority([FromBody] AssignActionAuthorityDto dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue) return Forbid();
        var processCode = NormalizeProcessCode(dto.ProcessCode);
        var actionCode = NormalizeActionCode(dto.ActionCode);
        if (processCode == null || actionCode == null || !IsValidProcessAction(processCode, actionCode))
            return BadRequest(new { message = "Select a valid process and workflow stage." });
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

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Process authority assigned successfully." });
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

    [HttpPut("action-authorities/{id:int}/pin")]
    public async Task<IActionResult> UpdateMyAuthorityPin(int id, [FromBody] UpdateAuthorityPinDto dto, CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        if (!_tenant.TenantId.HasValue || _tenant.IsSuperAdmin) return Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        var staffId = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == userId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return Forbid();

        var authority = await _db.ProcessActionAuthorities.SingleOrDefaultAsync(row =>
            row.Id == id && row.StaffId == staffId.Value && row.IsActive, ct);
        if (authority == null)
            return NotFound(new { message = "This workflow authority is not assigned to your account." });
        if (!RequiresPin(authority.ProcessCode, authority.ActionCode))
            return BadRequest(new { message = "This workflow stage does not require a security PIN." });

        var newPin = dto.NewPin?.Trim() ?? string.Empty;
        if (!IsValidPin(newPin))
            return BadRequest(new { message = "PIN must contain 4 to 8 digits." });
        if (!string.Equals(newPin, dto.ConfirmPin?.Trim(), StringComparison.Ordinal))
            return BadRequest(new { message = "New PIN and confirmation do not match." });
        if (!string.IsNullOrWhiteSpace(authority.PinHash) &&
            !ProcessAuthorityPinHasher.Verify(dto.CurrentPin?.Trim() ?? string.Empty, authority.PinHash))
            return BadRequest(new { message = "Current PIN is incorrect." });

        authority.PinHash = ProcessAuthorityPinHasher.Hash(newPin);
        authority.PinUpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Security PIN updated successfully.", pinConfigured = true, authority.PinUpdatedOnUtc });
    }

    private static string? NormalizeProcessCode(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PAYROLL" => "PAYROLL",
        "DEDUCTION" => "DEDUCTION",
        "ASSESSMENT" => "ASSESSMENT",
        _ => null
    };

    private static string? NormalizeActionCode(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CREATE" => "CREATE",
        "VERIFY" => "VERIFY",
        "APPROVE" => "APPROVE",
        "PAY" => "PAY",
        _ => null
    };

    private static bool IsValidProcessAction(string processCode, string actionCode) => processCode switch
    {
        "PAYROLL" => actionCode is "CREATE" or "VERIFY" or "APPROVE" or "PAY",
        "DEDUCTION" => actionCode == "APPROVE",
        "ASSESSMENT" => actionCode == "PAY",
        _ => false
    };

    private static bool RequiresPin(string processCode, string actionCode) =>
        (processCode == "PAYROLL" && actionCode is "APPROVE" or "PAY") ||
        (processCode == "DEDUCTION" && actionCode == "APPROVE") ||
        (processCode == "ASSESSMENT" && actionCode == "PAY");

    private static bool IsValidPin(string value) =>
        value.Length is >= 4 and <= 8 && value.All(char.IsDigit);

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

file sealed class ActionAuthorityRow
{
    public int Id { get; set; }
    public string ProcessCode { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public Guid StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string? StaffNumber { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public bool PinConfigured { get; set; }
    public bool IsCurrentUser { get; set; }
}

public sealed class AssignApproverDto
{
    public int CategoryId { get; set; }
    public Guid StaffId { get; set; }
}

public sealed class AssignActionAuthorityDto
{
    public string ProcessCode { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public Guid StaffId { get; set; }
}

public sealed class UpdateAuthorityPinDto
{
    public string? CurrentPin { get; set; }
    public string? NewPin { get; set; }
    public string? ConfirmPin { get; set; }
}
