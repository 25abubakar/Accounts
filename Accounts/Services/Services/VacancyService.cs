using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class VacancyService : IVacancyService
    {
        private readonly ApplicationDbContext _db;
        private readonly VacancyCodeService _codeService;
        private readonly DesignationService _designationService;
        private readonly ITenantService _tenantService;

        public VacancyService(
            ApplicationDbContext db,
            VacancyCodeService codeService,
            DesignationService designationService,
            ITenantService tenantService)
        {
            _db = db;
            _codeService = codeService;
            _designationService = designationService;
            _tenantService = tenantService;
        }

        public async Task<IEnumerable<VacancyDto>> GetAllAsync()
        {
            var list = await WithIncludes().OrderBy(v => v.VacancyCode).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<VacancyDto?> GetByIdAsync(Guid id)
        {
            var v = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == id);
            return v == null ? null : MapToDto(v);
        }

        public async Task<IEnumerable<VacancyDto>> GetVacantAsync()
        {
            var list = await WithIncludes().Where(v => !v.IsFilled).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<VacancyDto>> GetFilledAsync()
        {
            var list = await WithIncludes().Where(v => v.IsFilled).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<VacancyDto>> GetByNodeAsync(int orgId)
        {
            var list = await WithIncludes().Where(v => v.OrganizationId == orgId).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<OrgVacancyReportDto>> GetReportAsync()
        {
            var list = await WithIncludes().OrderBy(v => v.Organization!.Name).ToListAsync();
            return list.Select(v =>
            {
                var node = v.Organization;
                var p1 = node?.Parent;
                var p2 = p1?.Parent;
                return new OrgVacancyReportDto
                {
                    Country = p2?.Name ?? "-",
                    Company = p1?.Name ?? "-",
                    Branch = node?.Name ?? "-",
                    VacancyCode = v.VacancyCode,
                    Designation = v.ResolvedDesignation,
                    Department = v.Department,
                    IsFilled = v.IsFilled,
                    EmployeeName = v.Staff?.Person?.FullName,
                    EmployeeEmail = v.Staff?.Person?.Email,
                    JoiningDate = null
                };
            });
        }

        public async Task<string?> PreviewCodeAsync(int organizationId, string designation)
        {
            var orgNode = await _db.OrganizationTree.FindAsync(organizationId);
            if (orgNode == null) return null;
            return await _codeService.PreviewAsync(organizationId, designation);
        }

        public async Task<(VacancyDto? Vacancy, string? Error)> CreateAsync(CreateVacancyDto dto)
        {
            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return (null, $"Organization node {dto.OrganizationId} not found.");

            var (designationId, designationForCode, error) = await ResolveDesignationAsync(dto);
            if (error != null) return (null, error);

            var vacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, designationForCode!);

            var vacancy = new Vacancy
            {
                VacancyId = Guid.NewGuid(),
                TenantId = _tenantService.RequiredTenantId,
                OrganizationId = dto.OrganizationId,
                VacancyCode = vacancyCode,
                DesignationId = designationId,
                Department = dto.Department,
                IsFilled = false,
                CreatedDate = DateTime.UtcNow
            };

            _db.Vacancies.Add(vacancy);
            await _db.SaveChangesAsync();

            var created = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == vacancy.VacancyId);
            return (MapToDto(created!), null);
        }

        public async Task<(IEnumerable<VacancyDto> Created, IEnumerable<string> Errors)> CreateBulkAsync(CreateVacancyDto dto)
        {
            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return ([], [$"Organization node {dto.OrganizationId} not found."]);

            var count = dto.VacancyCount < 1 ? 1 : dto.VacancyCount;
            var created = new List<VacancyDto>();
            var errors = new List<string>();

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var (designationId, designationForCode, error) = await ResolveDesignationAsync(dto);
                    if (error != null)
                    {
                        errors.Add($"Vacancy {i + 1} failed: {error}");
                        continue;
                    }

                    var vacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, designationForCode!);

                    var vacancy = new Vacancy
                    {
                        VacancyId = Guid.NewGuid(),
                        TenantId = _tenantService.RequiredTenantId,
                        OrganizationId = dto.OrganizationId,
                        VacancyCode = vacancyCode,
                        DesignationId = designationId,
                        Department = dto.Department,
                        IsFilled = false,
                        CreatedDate = DateTime.UtcNow
                    };

                    _db.Vacancies.Add(vacancy);
                    await _db.SaveChangesAsync();

                    var saved = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == vacancy.VacancyId);
                    if (saved != null) created.Add(MapToDto(saved));
                }
                catch (Exception ex)
                {
                    errors.Add($"Vacancy {i + 1} failed: {ex.Message}");
                }
            }

            return (created, errors);
        }

        public async Task<(VacancyDto? Vacancy, string? Error)> UpdateAsync(Guid id, UpdateVacancyDto dto)
        {
            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return (null, $"Position {id} not found.");

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null) return (null, $"Organization node {dto.OrganizationId} not found.");

            var (newDesignationId, designationForCode, error) = await ResolveDesignationAsync(dto);
            if (error != null) return (null, error);

            var needsNewCode = vacancy.DesignationId != newDesignationId || vacancy.OrganizationId != dto.OrganizationId;

            vacancy.DesignationId = newDesignationId;
            vacancy.Department = dto.Department;
            vacancy.OrganizationId = dto.OrganizationId;

            if (needsNewCode)
                vacancy.VacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, designationForCode!);

            await _db.SaveChangesAsync();

            var updated = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == id);
            return (MapToDto(updated!), null);
        }

        public async Task<(bool Success, string Message)> DeleteAsync(Guid id)
        {
            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return (false, $"Position {id} not found.");

            if (vacancy.IsFilled)
                return (false, "Cannot delete a filled position. Remove the employee first.");

            _db.Vacancies.Remove(vacancy);
            await _db.SaveChangesAsync();
            return (true, $"Position '{vacancy.VacancyCode}' deleted.");
        }

        private async Task<(int DesignationId, string? DesignationForCode, string? Error)> ResolveDesignationAsync(CreateVacancyDto dto)
        {
            if (dto.DesignationId.HasValue && dto.DesignationId.Value > 0)
            {
                var designation = await _db.Designations.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == dto.DesignationId.Value);
                if (designation == null)
                    return (0, null, $"Designation Id {dto.DesignationId.Value} not found.");
                return (designation.Id, designation.Name, null);
            }

            var name = !string.IsNullOrWhiteSpace(dto.DesignationName)
                ? dto.DesignationName
                : dto.JobTitle;

            if (!string.IsNullOrWhiteSpace(name))
            {
                var id = await _designationService.UpsertByNameAsync(name);
                return (id, name.Trim(), null);
            }

            return (0, null, "DesignationId, DesignationName, or JobTitle (legacy) is required.");
        }

        private Task<(int DesignationId, string? DesignationForCode, string? Error)> ResolveDesignationAsync(UpdateVacancyDto dto)
            => ResolveDesignationAsync(new CreateVacancyDto
            {
                DesignationId = dto.DesignationId,
                DesignationName = dto.DesignationName,
                JobTitle = dto.JobTitle
            });

        private IQueryable<Vacancy> WithIncludes() =>
            _db.Vacancies
               .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
               .Include(v => v.DesignationNav)
               .Include(v => v.Staff).ThenInclude(s => s!.Person);

        private static VacancyDto MapToDto(Vacancy v)
        {
            var node = v.Organization;
            var p1 = node?.Parent;
            var p2 = p1?.Parent;

            return new VacancyDto
            {
                VacancyId = v.VacancyId,
                OrganizationId = v.OrganizationId,
                BranchName = node?.Name ?? "-",
                CompanyName = p1?.Name ?? "-",
                CountryName = p2?.Name ?? "-",
                NodeLabel = node?.Label ?? "-",
                VacancyCode = v.VacancyCode,
                DesignationId = v.DesignationId,
                Designation = v.ResolvedDesignation,
                Department = v.Department,
                IsFilled = v.IsFilled,
                CreatedDate = v.CreatedDate,
                Employee = v.Staff == null ? null : new StaffDto
                {
                    StaffId = v.Staff.StaffId,
                    FullName = v.Staff.Person?.FullName ?? "-",
                    Email = v.Staff.Person?.Email,
                    Phone = v.Staff.Person?.Phone,
                    PhotoUrl = v.Staff.Person?.ProfilePhotoUrl,
                    VacancyId = v.Staff.VacancyId,
                    VacancyCode = v.VacancyCode,
                    Designation = v.ResolvedDesignation,
                    Department = v.Department ?? node?.Name,
                    BranchName = node?.Name,
                    CompanyName = p1?.Name,
                    CountryName = p2?.Name,
                    JoiningDate = DateTime.UtcNow
                }
            };
        }
    }
}
