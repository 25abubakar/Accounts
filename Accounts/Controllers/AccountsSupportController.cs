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
public sealed class BankStatementsController(ApplicationDbContext db, ICurrentUserService current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? accountId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var query = db.BankStatements.AsNoTracking();
        if (accountId.HasValue) query = query.Where(x => x.ChartAccountId == accountId);
        if (dateFrom.HasValue) query = query.Where(x => (x.PostingDate ?? x.ValueDate ?? x.StatementDate) >= dateFrom);
        if (dateTo.HasValue) query = query.Where(x => (x.PostingDate ?? x.ValueDate ?? x.StatementDate) <= dateTo);
        return Ok(ApiResponse<IReadOnlyList<BankStatementDto>>.Ok(await Map(query)
            .OrderByDescending(x => x.PostingDate).ThenByDescending(x => x.Id).ToListAsync(ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var row = await Map(db.BankStatements.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? NotFound(ApiResponse<object?>.Fail("Bank statement was not found.")) : Ok(ApiResponse<BankStatementDto>.Ok(row));
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveBankStatementRequest request, CancellationToken ct) => Save(null, request, ct);

    [HttpPut("{id:long}")]
    public Task<IActionResult> Update(long id, [FromBody] SaveBankStatementRequest request, CancellationToken ct) => Save(id, request, ct);

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var row = await db.BankStatements.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(ApiResponse<object?>.Fail("Bank statement was not found."));
        db.BankStatements.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object?>.Ok(null, "Bank statement deleted successfully."));
    }

    private async Task<IActionResult> Save(long? id, SaveBankStatementRequest request, CancellationToken ct)
    {
        var account = await db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.Id == request.AccountId && x.IsActive)
            .Select(x => new { x.AccountReference, x.AccountNumber })
            .FirstOrDefaultAsync(ct);
        if (account == null)
            return BadRequest(ApiResponse<object?>.Fail("Account was not found or is inactive."));
        if (request.Debit < 0 || request.Credit < 0)
            return BadRequest(ApiResponse<object?>.Fail("Debit and credit cannot be negative."));
        if (request.Debit > 0 && request.Credit > 0)
            return BadRequest(ApiResponse<object?>.Fail("A statement row cannot contain both debit and credit."));
        BankStatement row;
        if (id.HasValue)
        {
            var existing = await db.BankStatements.FirstOrDefaultAsync(x => x.Id == id.Value, ct);
            if (existing == null) return NotFound(ApiResponse<object?>.Fail("Bank statement was not found."));
            row = existing;
        }
        else
        {
            row = new BankStatement { TenantId = current.TenantId, CreatedByUserId = current.UserId };
            db.BankStatements.Add(row);
        }
        row.ChartAccountId = request.AccountId;
        row.AccountNumber = Clean(account.AccountReference ?? account.AccountNumber, 50);
        row.ValueDate = request.ValueDate;
        row.PostingDate = request.PostingDate;
        row.InstrumentNo = Clean(request.InstrumentNo, 100);
        row.Description = Clean(request.TransactionDetails, 1000);
        row.TransactionReferenceNumber = Clean(request.TransactionReferenceNo, 100);
        row.Debit = decimal.Round(request.Debit, 2);
        row.Credit = decimal.Round(request.Credit, 2);
        row.Balance = decimal.Round(request.Balance, 2);
        row.Remarks = Clean(request.Remarks, 2000);
        row.ReferenceNumber = Clean(request.ReferenceNo, 80);
        row.Attachment = Clean(request.Attachment, 500);
        row.IsSettled = request.IsSettled;
        row.IsReversal = request.IsReversal;
        row.IsManual = request.IsManual;
        row.StatusId = request.StatusId;
        row.DateFormat = Clean(request.DateFormat, 40);
        if (id.HasValue) { row.UpdatedByUserId = current.UserId; row.UpdatedOnUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
        var dto = await Map(db.BankStatements.AsNoTracking()).FirstAsync(x => x.Id == row.Id, ct);
        return Ok(ApiResponse<BankStatementDto>.Ok(dto, id.HasValue ? "Bank statement updated successfully." : "Bank statement added successfully."));
    }

    private static IQueryable<BankStatementDto> Map(IQueryable<BankStatement> query) => query.Select(x => new BankStatementDto
    {
        Id = x.Id, AccountId = x.ChartAccountId, AccountReference = x.AccountNumber,
        ValueDate = x.ValueDate, PostingDate = x.PostingDate, InstrumentNo = x.InstrumentNo,
        TransactionDetails = x.Description, TransactionReferenceNo = x.TransactionReferenceNumber,
        Debit = x.Debit ?? 0, Credit = x.Credit ?? 0, Balance = x.Balance ?? 0, Remarks = x.Remarks,
        ReferenceNo = x.ReferenceNumber, Attachment = x.Attachment, IsSettled = x.IsSettled,
        IsReversal = x.IsReversal, IsManual = x.IsManual, StatusId = x.StatusId, DateFormat = x.DateFormat
    });

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
