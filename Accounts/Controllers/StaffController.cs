using Accounts.Authorization;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/employees")]
    [Produces("application/json")]
    public class StaffController : ControllerBase
    {
        private readonly IStaffService _service;
        public StaffController(IStaffService service) => _service = service;

        [HasPermission("EMPLOYEE_VIEW")]
        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HasPermission("EMPLOYEE_VIEW")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var s = await _service.GetByIdAsync(id);
            return s == null ? NotFound(new { message = $"Employee {id} not found." }) : Ok(s);
        }

        [HasPermission("EMPLOYEE_VIEW")]
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.SearchAsync(q));
        }

        [HasPermission("VACANCY_ASSIGN")]
        [HttpPost("hire/{vacancyId:guid}")]
        public async Task<IActionResult> Hire(Guid vacancyId, [FromBody] HireStaffDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.HireAsync(vacancyId, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = staff!.StaffId }, staff);
        }

        [HasPermission("VACANCY_ASSIGN")]
        [HttpPost("hire-person/{vacancyId:guid}")]
        public async Task<IActionResult> HirePerson(Guid vacancyId, [FromQuery] Guid personId)
        {
            var (staff, error) = await _service.HirePersonAsync(vacancyId, personId);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = staff!.StaffId }, staff);
        }

        [HasPermission("EMPLOYEE_EDIT")]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return NotFound(new { message = error });
            return Ok(staff);
        }

        [HasPermission("EMPLOYEE_EDIT")]
        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var (photoUrl, fullUrl, error) = await _service.UploadPhotoAsync(id, photo, baseUrl);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(new { message = "Photo uploaded successfully.", photoUrl, fullUrl });
        }

        [HasPermission("EMPLOYEE_EDIT")]
        [HttpDelete("{id:guid}/photo")]
        public async Task<IActionResult> DeletePhoto(Guid id)
        {
            var (success, message) = await _service.DeletePhotoAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HasPermission("EMPLOYEE_TRANSFER")]
        [HttpPut("{id:guid}/transfer")]
        public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferStaffDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.TransferAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(staff);
        }

        [HasPermission("EMPLOYEE_DELETE")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
