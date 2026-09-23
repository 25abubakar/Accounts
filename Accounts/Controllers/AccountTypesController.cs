using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/account-types")]
public sealed class AccountTypesController(IAccountTypeMasterService service) : ControllerBase
{
    [HttpGet("{kind}")]
    public async Task<IActionResult> List(string kind, CancellationToken ct) =>
        await Run(async () => ApiResponse<IReadOnlyList<AccountTypeLookupDto>>.Ok(await service.ListAsync(kind, ct)));

    [HttpPost("{kind}")]
    public Task<IActionResult> Create(string kind, [FromBody] SaveAccountTypeLookupRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountTypeLookupDto>.Ok(await service.SaveAsync(kind, null, request, ct), "Added successfully."));

    [HttpPut("{kind}/{id:int}")]
    public Task<IActionResult> Update(string kind, int id, [FromBody] SaveAccountTypeLookupRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<AccountTypeLookupDto>.Ok(await service.SaveAsync(kind, id, request, ct), "Updated successfully."));

    [HttpDelete("{kind}/{id:int}")]
    public Task<IActionResult> Delete(string kind, int id, CancellationToken ct) =>
        Run(async () =>
        {
            await service.DeleteAsync(kind, id, ct);
            return ApiResponse<object?>.Ok(null, "Deleted successfully.");
        });

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}
