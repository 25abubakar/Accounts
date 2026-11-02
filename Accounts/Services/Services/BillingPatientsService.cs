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
        return await SpListQuery.ExecAsync<BillingPatientRowDto>(
            _db,
            "EXEC dbo.usp_Billing_PatientMaster_List @TenantId, @DateFrom, @DateTo, @OfficeId",
            ct,
            SpListQuery.TenantId(tenantId),
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
        var list = await SpListQuery.ExecAsync<BillingPatientRowDto>(
            _db,
            "EXEC dbo.usp_Billing_PatientMaster_List @TenantId, @DateFrom, @DateTo, @OfficeId",
            ct,
            SpListQuery.TenantId(tenantId),
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
        var person = new BillingPerson
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingPersons.Add(person);
        await _db.SaveChangesAsync(ct);

        var patientNo = !string.IsNullOrWhiteSpace(request.PatientNo)
            ? request.PatientNo.Trim()
            : $"PT-{DateTime.UtcNow:yyyyMMdd}-{person.PersonId}";

        var patient = new BillingPatient
        {
            OrgId = tenantId,
            PersonId = person.PersonId,
            PatientNo = patientNo,
            PatientName = $"{request.FirstName} {request.LastName}".Trim(),
            OfficeId = request.OfficeId,
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingPatients.Add(patient);
        await _db.SaveChangesAsync(ct);

        var appointment = new BillingAppointment
        {
            OrgId = tenantId,
            PatientId = patient.PatientId,
            OfficeId = request.OfficeId,
            PtTypeId = request.PtTypeId,
            AppointmentDate = request.DateOfService.ToString("yyyy-MM-dd"),
            ProviderName = request.ProviderName,
            InsuranceName = request.InsuranceName,
            IsClaimed = false,
            CreatedDate = DateTime.UtcNow,
            CreatedByUserId = userId
        };
        _db.BillingAppointments.Add(appointment);
        await _db.SaveChangesAsync(ct);

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

            var person = new BillingPerson
            {
                FirstName = firstName,
                LastName = lastName,
                CreatedDate = DateTime.UtcNow
            };
            _db.BillingPersons.Add(person);
            await _db.SaveChangesAsync(ct);

            var patient = new BillingPatient
            {
                OrgId = tenantId,
                PersonId = person.PersonId,
                PatientNo = string.IsNullOrWhiteSpace(patientNo) ? $"PT-{person.PersonId}" : patientNo,
                PatientName = $"{firstName} {lastName}".Trim(),
                CreatedDate = DateTime.UtcNow
            };
            _db.BillingPatients.Add(patient);
            await _db.SaveChangesAsync(ct);

            var appointment = new BillingAppointment
            {
                OrgId = tenantId,
                PatientId = patient.PatientId,
                AppointmentDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                IsClaimed = false,
                CreatedDate = DateTime.UtcNow,
                CreatedByUserId = userId
            };
            _db.BillingAppointments.Add(appointment);
            count++;
        }

        await _db.SaveChangesAsync(ct);
        return count;
    }

    public async Task<int> SyncEmrAsync(
        int tenantId,
        DateOnly date,
        string? userId,
        CancellationToken ct = default)
    {
        // EMR sync bridge: check existing appointments for the date
        var dateStr = date.ToString("yyyy-MM-dd");
        var existingCount = await _db.BillingAppointments.CountAsync(x => x.OrgId == tenantId && x.AppointmentDate == dateStr, ct);
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
}

