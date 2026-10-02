using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/account-category-types")]
public sealed class AccountCategoryTypesController(IAccountCategoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<AccountCategoryTypeDto>>.Ok(await service.ListTypesAsync(ct)));

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveAccountCategoryTypeRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountCategoryTypeDto>.Ok(await service.SaveTypeAsync(null, request, ct), "Category type added successfully."));

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] SaveAccountCategoryTypeRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountCategoryTypeDto>.Ok(await service.SaveTypeAsync(id, request, ct), "Category type updated successfully."));

    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Run(async () => { await service.DeleteTypeAsync(id, ct); return ApiResponse<object?>.Ok(null, "Category type deleted successfully."); });

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/account-categories")]
public sealed class AccountCategoriesController(IAccountCategoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<AccountCategoryDto>>.Ok(await service.ListAsync(ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var row = await service.GetAsync(id, ct);
        return row == null ? NotFound(ApiResponse<object?>.Fail("Account category was not found.")) : Ok(ApiResponse<AccountCategoryDto>.Ok(row));
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveAccountCategoryRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountCategoryDto>.Ok(await service.SaveAsync(null, request, ct), "Account category added successfully."));

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] SaveAccountCategoryRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountCategoryDto>.Ok(await service.SaveAsync(id, request, ct), "Account category updated successfully."));

    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Run(async () => { await service.DeleteAsync(id, ct); return ApiResponse<object?>.Ok(null, "Account category deleted successfully."); });

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/accounts")]
public sealed class ChartAccountsController(IAccountService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? categoryId, [FromQuery] int? parentId, [FromQuery] bool activeOnly = false, CancellationToken ct = default) =>
        Ok(ApiResponse<IReadOnlyList<AccountDto>>.Ok(await service.ListAsync(categoryId, parentId, activeOnly, ct)));

    [HttpGet("main")]
    public async Task<IActionResult> Main([FromQuery] int? categoryId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<AccountDto>>.Ok(await service.ListMainAsync(categoryId, ct)));

    [HttpGet("by-category/{categoryId:int}")]
    public async Task<IActionResult> ByCategory(int categoryId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<AccountDto>>.Ok(await service.ListAsync(categoryId, null, true, ct)));

    [HttpGet("{id:int}")]
    [HttpGet("{id:int}/info")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var row = await service.GetAsync(id, ct);
        return row == null ? NotFound(ApiResponse<object?>.Fail("Account was not found.")) : Ok(ApiResponse<AccountDto>.Ok(row));
    }

    [HttpGet("{id:int}/subaccounts")]
    public Task<IActionResult> SubAccounts(int id, CancellationToken ct) =>
        Run(async () => ApiResponse<IReadOnlyList<AccountDto>>.Ok(await service.ListSubAccountsAsync(id, ct)));

    [HttpGet("{accountId:int}/category")]
    public async Task<IActionResult> AccountCategory(int accountId, CancellationToken ct)
    {
        var row = await service.GetAsync(accountId, ct);
        return row == null ? NotFound(ApiResponse<object?>.Fail("Account was not found.")) : Ok(ApiResponse<object>.Ok(new { row.CategoryId, row.CategoryName }));
    }

    [HttpGet("subaccount/{subAccountId:int}/parent")]
    public async Task<IActionResult> Parent(int subAccountId, CancellationToken ct)
    {
        var row = await service.GetAsync(subAccountId, ct);
        if (row == null) return NotFound(ApiResponse<object?>.Fail("Subaccount was not found."));
        if (!row.ParentId.HasValue) return BadRequest(ApiResponse<object?>.Fail("Selected account is a main account."));
        var parent = await service.GetAsync(row.ParentId.Value, ct);
        return parent == null ? NotFound(ApiResponse<object?>.Fail("Parent account was not found.")) : Ok(ApiResponse<AccountDto>.Ok(parent));
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveAccountRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountDto>.Ok(await service.CreateAsync(request, ct), "Account added successfully."));

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] SaveAccountRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountDto>.Ok(await service.UpdateAsync(id, request, ct), "Account updated successfully."));

    [HttpPost("{id:int}/files")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public Task<IActionResult> SaveFiles(
        int id,
        IFormFile? photo,
        IFormFile? attachment,
        CancellationToken ct) =>
        Run(async () => ApiResponse<AccountDto>.Ok(
            await service.SaveFilesAsync(id, photo, attachment, ct),
            "Account files uploaded successfully."));

    [HttpGet("{id:int}/files/{kind}")]
    public async Task<IActionResult> OpenFile(int id, string kind, CancellationToken ct)
    {
        try
        {
            var stored = await service.OpenFileAsync(id, kind, ct);
            return stored == null
                ? NotFound(ApiResponse<object?>.Fail("Account file was not found."))
                : File(stored.Value.Stream, stored.Value.ContentType, stored.Value.FileName);
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Run(async () => { await service.DeleteAsync(id, ct); return ApiResponse<object?>.Ok(null, "Account deleted successfully."); });

    [HttpPost("{id:int}/budget")]
    public Task<IActionResult> Budget(int id, [FromBody] UpdateAccountBudgetRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountDto>.Ok(await service.UpdateBudgetAsync(id, request, ct), "Account budget updated successfully."));

    [HttpGet("{id:int}/ledger")]
    public Task<IActionResult> Ledger(int id, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct) =>
        Run(async () => ApiResponse<IReadOnlyList<AccountLedgerDto>>.Ok(await service.LedgerAsync(id, dateFrom, dateTo, false, ct)));

    [HttpGet("{id:int}/hidden-ledger")]
    public Task<IActionResult> HiddenLedger(int id, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct) =>
        Run(async () => ApiResponse<IReadOnlyList<AccountLedgerDto>>.Ok(await service.LedgerAsync(id, dateFrom, dateTo, true, ct)));

    [HttpPost("ledger-transfer")]
    [Idempotent]
    public Task<IActionResult> Transfer([FromBody] LedgerTransferRequest request, CancellationToken ct) =>
        Run(async () => { await service.TransferLedgerAsync(request, ct); return ApiResponse<object?>.Ok(null, "Ledger entry transferred successfully."); });

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}
