using Accounts.Data;
using Accounts.Models;
using Accounts.Models.SpListRows;
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

        public async Task<IEnumerable<VacancyDto>> GetAllAsync() =>
            await LoadVacanciesAsync(filledFilter: null);

        public async Task<VacancyDto?> GetByIdAsync(Guid id)
        {
            var v = await WithIncludes().FirstOrDefaultAsync(v => v.VacancyId == id);
            return v == null ? null : MapToDto(v);
        }

        public async Task<IEnumerable<VacancyDto>> GetVacantAsync() =>
            await LoadVacanciesAsync(filledFilter: "0");

        public async Task<IEnumerable<VacancyDto>> GetFilledAsync() =>
            await LoadVacanciesAsync(filledFilter: "1");

        public async Task<IEnumerable<VacancyDto>> GetByNodeAsync(int orgId)
        {
            var all = await LoadVacanciesAsync(filledFilter: null);
            return all.Where(v => v.OrganizationId == orgId);
        }

        public async Task<IEnumerable<OrgVacancyReportDto>> GetReportAsync()
        {
            var rows = await LoadVacanciesAsync(filledFilter: null);
            return rows.Select(v => new OrgVacancyReportDto
            {
                Country = v.CountryName ?? "-",
                Company = v.CompanyName ?? "-",
                Branch = v.BranchName ?? "-",
                VacancyCode = v.VacancyCode,
                Designation = v.Designation,
                Department = v.Department,
                IsFilled = v.IsFilled,
                EmployeeName = v.Employee?.FullName,
                EmployeeEmail = v.Employee?.Email,
                JoiningDate = null
            });
        }

        private async Task<List<VacancyDto>> LoadVacanciesAsync(string? filledFilter)
        {
            var rows = await SpListQuery.ExecAsync<HrVacancyListRow>(
                _db,
                "EXEC dbo.usp_Hr_Vacancies_List @TenantId, @FilledFilter",
                CancellationToken.None,
                SpListQuery.TenantId(_tenantService.RequiredTenantId),
                SpListQuery.NVarChar("@FilledFilter", filledFilter));
            return rows.Select(MapVacancyRow).ToList();
        }

        private static VacancyDto MapVacancyRow(HrVacancyListRow row) => new()
        {
            VacancyId = row.VacancyId,
            OrganizationId = row.OrganizationId,
            BranchName = row.BranchName,
            CompanyName = row.CompanyName,
            CountryName = row.CountryName,
            NodeLabel = row.NodeLabel,
            VacancyCode = row.VacancyCode,
            DesignationId = row.DesignationId,
            Designation = row.Designation,
            Department = row.Department,
            IsFilled = row.IsFilled,
            CreatedDate = row.CreatedDate,
            Employee = row.EmployeeStaffId == null ? null : new StaffDto
            {
                StaffId = row.EmployeeStaffId.Value,
                FullName = row.EmployeeFullName ?? "-",
                Email = row.EmployeeEmail,
                Phone = row.EmployeePhone,
                PhotoUrl = row.EmployeePhotoUrl,
                VacancyId = row.VacancyId,
                VacancyCode = row.VacancyCode,
                Designation = row.Designation,
                Department = row.Department,
                BranchName = row.BranchName,
                CompanyName = row.CompanyName,
                CountryName = row.CountryName,
                JoiningDate = DateTime.UtcNow
            }
        };

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
