using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class BillingClaimsService : IBillingClaimsService
{
    private readonly ApplicationDbContext _db;

    public BillingClaimsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BillingPaymentRowDto>> ListClaimsAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string tab = "claims",
        int? statusId = null,
        int? carrierId = null,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingPaymentRowDto>(
            _db,
            "EXEC dbo.usp_Billing_Claims_List @TenantId, @DateFrom, @DateTo, @Tab, @StatusId, @CarrierId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo),
            SpListQuery.StringNullable("@Tab", tab),
            SpListQuery.IntNullable("@StatusId", statusId),
            SpListQuery.IntNullable("@CarrierId", carrierId));
    }

    public async Task<BillingPaymentRowDto?> GetClaimByIdAsync(int tenantId, int claimId, CancellationToken ct = default)
    {
        var list = await SpListQuery.ExecAsync<BillingPaymentRowDto>(
            _db,
            "EXEC dbo.usp_Billing_Claims_List @TenantId, @DateFrom, @DateTo, @Tab, @StatusId, @CarrierId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", null),
            SpListQuery.DateNullable("@DateTo", null),
            SpListQuery.StringNullable("@Tab", "claims"),
            SpListQuery.IntNullable("@StatusId", null),
            SpListQuery.IntNullable("@CarrierId", null));

        return list.FirstOrDefault(x => x.Id == claimId);
    }

    public async Task<int> CreateClaimAsync(
        int tenantId,
        CreateBillingClaimRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        var claim = new BillingClaim
        {
            OrgId = tenantId,
            AppointmentId = request.AppointmentId,
            PatientId = request.PatientId,
            CarrierId = request.CarrierId,
            PolicyNumber = request.PolicyNumber,
            TotalBilled = request.TotalBilled ?? request.CptCodes.Sum(x => x.ClaimAmount ?? 0),
            TotalAllowed = request.TotalAllowed ?? request.CptCodes.Sum(x => x.InsuranceAmount ?? 0),
            PatientResponsibility = request.PatientResponsibility ?? 0,
            SubmittedDate = request.SubmittedDate ?? DateTime.UtcNow,
            Remarks = request.Remarks,
            CreatedDate = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _db.BillingClaims.Add(claim);
        await _db.SaveChangesAsync(ct);

        if (request.AppointmentId.HasValue)
        {
            var appt = await _db.BillingAppointments.FirstOrDefaultAsync(x => x.AppointmentId == request.AppointmentId.Value, ct);
            if (appt != null)
            {
                appt.IsClaimed = true;
            }
        }

        foreach (var cpt in request.CptCodes)
        {
            var code = new BillingApptCode
            {
                ClaimId = claim.ClaimId,
                AppointmentId = request.AppointmentId,
                PatientId = request.PatientId,
                OrgId = tenantId,
                ProcedureCode = cpt.ProcedureCode,
                ClaimAmount = cpt.ClaimAmount,
                InsuranceAmount = cpt.InsuranceAmount,
                Modifiers = cpt.Modifiers,
                CreatedDate = DateTime.UtcNow,
                CreatedByUserId = userId
            };
            _db.BillingApptCodes.Add(code);
        }

        var log = new BillingClaimsLog
        {
            ClaimId = claim.ClaimId,
            StatusId = claim.StatusId,
            LogDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            Remarks = "Claim created",
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingClaimsLogs.Add(log);

        await _db.SaveChangesAsync(ct);
        return claim.ClaimId;
    }

    public async Task<bool> UpdateClaimAsync(
        int tenantId,
        int claimId,
        CreateBillingClaimRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.ClaimId == claimId, ct);
        if (claim == null) return false;

        claim.CarrierId = request.CarrierId ?? claim.CarrierId;
        claim.PolicyNumber = request.PolicyNumber ?? claim.PolicyNumber;
        if (request.TotalBilled.HasValue) claim.TotalBilled = request.TotalBilled.Value;
        if (request.TotalAllowed.HasValue) claim.TotalAllowed = request.TotalAllowed.Value;
        if (request.PatientResponsibility.HasValue) claim.PatientResponsibility = request.PatientResponsibility.Value;
        if (request.SubmittedDate.HasValue) claim.SubmittedDate = request.SubmittedDate.Value;
        if (!string.IsNullOrWhiteSpace(request.Remarks)) claim.Remarks = request.Remarks;

        var log = new BillingClaimsLog
        {
            ClaimId = claim.ClaimId,
            StatusId = claim.StatusId,
            LogDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            Remarks = "Claim updated",
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingClaimsLogs.Add(log);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateClaimStatusAsync(
        int tenantId,
        int claimId,
        int statusId,
        string? remarks,
        string? userId,
        CancellationToken ct = default)
    {
        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.ClaimId == claimId, ct);
        if (claim == null) return false;

        claim.StatusId = statusId;
        if (!string.IsNullOrWhiteSpace(remarks))
        {
            claim.Remarks = remarks;
        }

        var log = new BillingClaimsLog
        {
            ClaimId = claim.ClaimId,
            StatusId = statusId,
            LogDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? $"Status changed to {statusId}" : remarks,
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingClaimsLogs.Add(log);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> PostClaimPaymentAsync(
        int tenantId,
        int claimId,
        PostClaimPaymentRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.ClaimId == claimId, ct);
        if (claim == null) return false;

        claim.TotalPaid = (claim.TotalPaid ?? 0) + request.PaidAmount;
        if (request.PatientResponsibility.HasValue)
        {
            claim.PatientResponsibility = request.PatientResponsibility.Value;
        }
        if (!string.IsNullOrWhiteSpace(request.EobCheckNo))
        {
            claim.EobCheckNo = request.EobCheckNo.Trim();
        }

        var paymentRow = new BillingPaymentRow
        {
            ClaimId = claimId,
            OrgId = tenantId,
            EobCheckNo = request.EobCheckNo,
            PaidAmount = request.PaidAmount,
            PatientResponsibility = request.PatientResponsibility,
            Remark = request.Remarks,
            IsClosed = request.IsClosed,
            IsReProcess = request.IsReProcess,
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingPaymentRows.Add(paymentRow);

        var log = new BillingClaimsLog
        {
            ClaimId = claimId,
            StatusId = claim.StatusId,
            LogDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            Remarks = $"Payment posted: {request.PaidAmount} (Check: {request.EobCheckNo})",
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingClaimsLogs.Add(log);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BillingClaimLookupsDto> GetClaimLookupsAsync(int tenantId, CancellationToken ct = default)
    {
        var carriers = await _db.BillingCarriers.AsNoTracking()
            .OrderBy(x => x.CarrierName)
            .Select(x => new { x.CarrierId, x.CarrierName })
            .ToListAsync(ct);

        var codes = await _db.BillingProcedureCodes.AsNoTracking()
            .OrderBy(x => x.Codes)
            .Select(x => new { x.ProcCodeId, x.Codes, x.Description, x.StandardRate })
            .ToListAsync(ct);

        var statuses = await RoznamchaStatusCatalog.Query(_db, "Billing")
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.ColorCode })
            .ToListAsync(ct);

        var offices = await _db.BillingOffices.AsNoTracking()
            .OrderBy(x => x.OfficeName)
            .Select(x => new { x.OfficeId, x.OfficeName })
            .ToListAsync(ct);

        var ptTypes = await _db.BillingPatientTypes.AsNoTracking()
            .OrderBy(x => x.PtType)
            .Select(x => new { x.PtTypeId, x.PtType })
            .ToListAsync(ct);

        return new BillingClaimLookupsDto
        {
            Carriers = carriers.Cast<object>().ToList(),
            ProcedureCodes = codes.Cast<object>().ToList(),
            Statuses = statuses.Cast<object>().ToList(),
            Offices = offices.Cast<object>().ToList(),
            PatientTypes = ptTypes.Cast<object>().ToList()
        };
    }

    public async Task<IReadOnlyList<object>> GetClaimLogsAsync(int tenantId, int claimId, CancellationToken ct = default)
    {
        var logs = await _db.BillingClaimsLogs.AsNoTracking()
            .Where(x => x.ClaimId == claimId)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new { x.Id, x.ClaimId, x.StatusId, x.LogDate, x.Remarks, x.CreatedDate })
            .ToListAsync(ct);

        return logs.Cast<object>().ToList();
    }
}

