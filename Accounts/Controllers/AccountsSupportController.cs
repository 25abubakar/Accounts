using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/projects")]
public sealed class AccountsProjectsController(ApplicationDbContext db, ICurrentUserService current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<AccountsProjectDto>>.Ok(
        await db.AccountsProjects.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new AccountsProjectDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive })
            .ToListAsync(ct)));

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveAccountsProjectRequest request, CancellationToken ct) => Save(null, request, ct);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] SaveAccountsProjectRequest request, CancellationToken ct) => Save(id, request, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var row = await db.AccountsProjects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(ApiResponse<object?>.Fail("Project was not found."));
        if (await db.RoznamchaEntries.AnyAsync(x => x.ProjectId == id, ct))
            return BadRequest(ApiResponse<object?>.Fail("Project cannot be deleted because it is in use."));
        db.AccountsProjects.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object?>.Ok(null, "Project deleted successfully."));
    }

    private async Task<IActionResult> Save(int? id, SaveAccountsProjectRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(ApiResponse<object?>.Fail("Project name is required."));
        if (await db.AccountsProjects.AsNoTracking().AnyAsync(x => x.Name == name && (!id.HasValue || x.Id != id), ct))
            return BadRequest(ApiResponse<object?>.Fail("A project with this name already exists."));
        AccountsProject row;
        if (id.HasValue)
        {
            var existing = await db.AccountsProjects.FirstOrDefaultAsync(x => x.Id == id.Value, ct);
            if (existing == null) return NotFound(ApiResponse<object?>.Fail("Project was not found."));
            row = existing;
        }
        else
        {
            row = new AccountsProject { TenantId = current.TenantId };
            db.AccountsProjects.Add(row);
        }
        row.Name = name[..Math.Min(name.Length, 160)];
        row.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<AccountsProjectDto>.Ok(new() { Id = row.Id, Name = row.Name, IsActive = row.IsActive },
            id.HasValue ? "Project updated successfully." : "Project added successfully."));
    }
}

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/bank-statements")]
public sealed class BankStatementsController(IBankStatementService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? accountId,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<BankStatementDto>>.Ok(await service.ListAsync(accountId, dateFrom, dateTo, ct)));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var row = await service.GetAsync(id, ct);
        return row == null
            ? NotFound(ApiResponse<object?>.Fail("Bank statement was not found."))
            : Ok(ApiResponse<BankStatementDto>.Ok(row));
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveBankStatementRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<BankStatementDto>.Ok(await service.SaveAsync(null, request, ct), "Bank statement added successfully."));

    [HttpPut("{id:long}")]
    public Task<IActionResult> Update(long id, [FromBody] SaveBankStatementRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<BankStatementDto>.Ok(await service.SaveAsync(id, request, ct), "Bank statement updated successfully."));

    [HttpDelete("{id:long}")]
    public Task<IActionResult> Delete(long id, CancellationToken ct) =>
        Run(async () =>
        {
            await service.DeleteAsync(id, ct);
            return ApiResponse<object?>.Ok(null, "Bank statement deleted successfully.");
        });

    [HttpPost("preview-excel")]
    [RequestSizeLimit(20_000_000)]
    public Task<IActionResult> PreviewExcel(
        [FromForm] int accountId,
        [FromForm] string dateFormat,
        [FromForm] int? yearId,
        IFormFile? excelFile,
        CancellationToken ct) =>
        Run(async () =>
        {
            if (excelFile == null) throw new InvalidOperationException("Excel statement file is required.");
            var preview = await service.PreviewExcelAsync(accountId, dateFormat, yearId, excelFile, ct);
            return ApiResponse<BankStatementPreviewResultDto>.Ok(preview, "Preview generated successfully.");
        });

    [HttpPost("upload-save")]
    [RequestSizeLimit(30_000_000)]
    public Task<IActionResult> UploadSave(
        [FromForm] int accountId,
        [FromForm] string dateFormat,
        [FromForm] int? yearId,
        [FromForm] string rowsJson,
        IFormFile? attachment,
        CancellationToken ct) =>
        Run(async () =>
        {
            var rows = string.IsNullOrWhiteSpace(rowsJson)
                ? new List<BankStatementPreviewRowDto>()
                : System.Text.Json.JsonSerializer.Deserialize<List<BankStatementPreviewRowDto>>(
                    rowsJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? new List<BankStatementPreviewRowDto>();
            var request = new BankStatementUploadSaveRequest
            {
                AccountId = accountId,
                DateFormat = dateFormat,
                YearId = yearId,
                Rows = rows
            };
            var saved = await service.SaveUploadAsync(request, attachment, ct);
            return ApiResponse<IReadOnlyList<BankStatementDto>>.Ok(saved, "File uploaded and data saved successfully.");
        });

    [HttpPost("transfer")]
    public Task<IActionResult> Transfer([FromBody] BankStatementTransferRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            var result = await service.TransferToRoznamchaAsync(request, ct);
            return ApiResponse<BankStatementTransferResultDto>.Ok(result, result.Message);
        });

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}


