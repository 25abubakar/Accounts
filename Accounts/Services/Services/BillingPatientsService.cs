using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class BillingPatientsService : IBillingPatientsService
{
    private readonly ApplicationDbContext _db;

    public BillingPatientsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BillingPatientRowDto>> ListAppointmentsAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        int? officeId = null,
        CancellationToken ct = default)
    {
        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        return await SpListQuery.ExecAsync<BillingPatientRowDto>(
            _db,
            "EXEC dbo.usp_Billing_PatientMaster_List @TenantId, @OrgId, @DateFrom, @DateTo, @OfficeId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Int("@OrgId", orgId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo),
            SpListQuery.IntNullable("@OfficeId", officeId));
    }

    public async Task<IReadOnlyList<BillingPatientRowDto>> SearchPatientsAsync(
        int tenantId,
        string search,
        CancellationToken ct = default)
    {
        var clean = (search ?? string.Empty).Trim().ToLower();
        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        var list = await SpListQuery.ExecAsync<BillingPatientRowDto>(
            _db,
            "EXEC dbo.usp_Billing_PatientMaster_List @TenantId, @OrgId, @DateFrom, @DateTo, @OfficeId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Int("@OrgId", orgId),
            SpListQuery.DateNullable("@DateFrom", null),
            SpListQuery.DateNullable("@DateTo", null),
            SpListQuery.IntNullable("@OfficeId", null));

        return list.Where(x =>
            (!string.IsNullOrEmpty(x.FullName) && x.FullName.ToLower().Contains(clean)) ||
            (!string.IsNullOrEmpty(x.PatientNumber) && x.PatientNumber.ToLower().Contains(clean)) ||
            (!string.IsNullOrEmpty(x.Phone) && x.Phone.Contains(clean))).ToList();
    }

    public async Task<int> CreatePatientAndAppointmentAsync(
        int tenantId,
        CreatePatientAppointmentRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        var firstName = (request.FirstName ?? string.Empty).Trim();
        var lastName = (request.LastName ?? string.Empty).Trim();
        if (firstName.Length == 0 && lastName.Length == 0)
            throw new InvalidOperationException("Patient first or last name is required.");

        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var now = DateTime.UtcNow;
        var suppliedPatientNo = request.PatientNo?.Trim();
        BillingPatient? patient = null;
        BillingPerson? person = null;

        if (!string.IsNullOrWhiteSpace(suppliedPatientNo))
        {
            patient = await _db.BillingPatients
                .OrderBy(x => x.PatientId)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.OrgId == orgId && x.PatientNo == suppliedPatientNo, ct);
            if (patient?.PersonId is int existingPersonId)
                person = await _db.BillingPersons
                    .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OrgId == orgId && x.PersonId == existingPersonId, ct);
        }

        person ??= new BillingPerson
        {
            TenantId = tenantId,
            OrgId = orgId,
            CreatedDate = now
        };
        person.FirstName = firstName;
        person.LastName = lastName;
        person.FullName = $"{firstName} {lastName}".Trim();
        person.PhoneNumber = request.PhoneNumber?.Trim();
        person.Email = request.Email?.Trim();
        person.DOB = request.DateOfBirth;
        person.Age = request.DateOfBirth.HasValue
            ? CalculateAge(request.DateOfBirth.Value, now.Date)
            : null;
        if (person.PersonId == 0)
        {
            _db.BillingPersons.Add(person);
            await _db.SaveChangesAsync(ct);
        }

        patient ??= new BillingPatient
        {
            TenantId = tenantId,
            OrgId = orgId,
            CreatedDate = now,
            CreatedBy = userId
        };
        patient.PersonId = person.PersonId;
        patient.PatientNo = !string.IsNullOrWhiteSpace(suppliedPatientNo)
            ? suppliedPatientNo
            : $"PT-{now:yyyyMMdd}-{person.PersonId}";
        patient.PatientName = person.FullName;
        patient.OfficeId = request.OfficeId ?? patient.OfficeId;
        if (patient.PatientId == 0)
        {
            _db.BillingPatients.Add(patient);
            await _db.SaveChangesAsync(ct);
        }

        var appointment = new BillingAppointment
        {
            TenantId = tenantId,
            OrgId = orgId,
            PatientId = patient.PatientId,
            OfficeId = request.OfficeId,
            PtTypeId = request.PtTypeId,
            AppointmentDate = request.DateOfService.ToString("yyyy-MM-dd"),
            DOS = request.DateOfService,
            StatusId = 1045,
            AppStatusId = 1045,
            ProviderName = request.ProviderName?.Trim(),
            InsuranceName = request.InsuranceName?.Trim(),
            IsClaimed = false,
            CreatedDate = now,
            CreatedByUserId = userId
        };
        _db.BillingAppointments.Add(appointment);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return appointment.AppointmentId;
    }

    public async Task<int> UploadPatientSheetAsync(
        int tenantId,
        IFormFile file,
        string? userId,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0) return 0;
        int count = 0;
        using var stream = file.OpenReadStream();
        using var reader = new System.IO.StreamReader(stream);

        // Simple CSV parser for batch ingestion (supports patient list)
        string? headerLine = await reader.ReadLineAsync(ct);
        if (headerLine == null) return 0;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            var firstName = parts[0].Trim();
            var lastName = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            var patientNo = parts.Length > 2 ? parts[2].Trim() : string.Empty;

            await CreatePatientAndAppointmentAsync(tenantId, new CreatePatientAppointmentRequest
            {
                FirstName = firstName,
                LastName = lastName,
                PatientNo = patientNo,
                DateOfService = DateTime.UtcNow
            }, userId, ct);
            count++;
        }
        return count;
    }

    public async Task<int> SyncEmrAsync(
        int tenantId,
        DateOnly date,
        string? userId,
        CancellationToken ct = default)
    {
        // EMR sync bridge: check existing appointments for the date
        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var existingCount = await _db.BillingAppointments.CountAsync(
            x => x.TenantId == tenantId && x.OrgId == orgId && x.DOS >= start && x.DOS < end, ct);
        return existingCount;
    }

    public async Task<BillingPatientLookupsDto> GetPatientLookupsAsync(int tenantId, CancellationToken ct = default)
    {
        var offices = await _db.BillingOffices.AsNoTracking()
            .OrderBy(x => x.OfficeName)
            .Select(x => new { x.OfficeId, x.OfficeName })
            .ToListAsync(ct);

        var ptTypes = await _db.BillingPatientTypes.AsNoTracking()
            .OrderBy(x => x.PtType)
            .Select(x => new { x.PtTypeId, x.PtType })
            .ToListAsync(ct);

        var statuses = await RoznamchaStatusCatalog.Query(_db, "Billing")
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        return new BillingPatientLookupsDto
        {
            Offices = offices.Cast<object>().ToList(),
            PatientTypes = ptTypes.Cast<object>().ToList(),
            Statuses = statuses.Cast<object>().ToList()
        };
    }

    private async Task<int> ResolveOrganizationIdAsync(int tenantId, CancellationToken ct) =>
        await _db.Tenants.AsNoTracking().Where(x => x.Id == tenantId)
            .Select(x => x.OrganizationTreeId).SingleAsync(ct);

    private static int CalculateAge(DateTime dateOfBirth, DateTime today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > today.AddYears(-age)) age--;
        return Math.Max(age, 0);
    }
}

