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
        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        return await SpListQuery.ExecAsync<BillingPaymentRowDto>(
            _db,
            "EXEC dbo.usp_Billing_Claims_List @TenantId, @OrgId, @DateFrom, @DateTo, @Tab, @StatusId, @CarrierId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Int("@OrgId", orgId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo),
            SpListQuery.StringNullable("@Tab", tab),
            SpListQuery.IntNullable("@StatusId", statusId),
            SpListQuery.IntNullable("@CarrierId", carrierId));
    }

    public async Task<IReadOnlyList<BillingEobRowDto>> ListEobsAsync(
        int tenantId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<BillingEobRowDto>(
            _db, "EXEC dbo.usp_Billing_Eob_List @TenantId, @DateFrom, @DateTo", ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));

    public async Task<IReadOnlyList<BillingClaimCodeDetailDto>> ListClaimCodeDetailsAsync(
        int tenantId, int claimId, int? appointmentId, CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<BillingClaimCodeDetailDto>(
            _db, "EXEC dbo.usp_Billing_ClaimCodeDetails_List @TenantId, @ClaimId, @AppointmentId", ct,
            SpListQuery.TenantId(tenantId), SpListQuery.Int("@ClaimId", claimId),
            SpListQuery.IntNullable("@AppointmentId", appointmentId));

    public async Task<BillingPaymentRowDto?> GetClaimByIdAsync(int tenantId, int claimId, CancellationToken ct = default)
    {
        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        var list = await SpListQuery.ExecAsync<BillingPaymentRowDto>(
            _db,
            "EXEC dbo.usp_Billing_Claims_List @TenantId, @OrgId, @DateFrom, @DateTo, @Tab, @StatusId, @CarrierId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Int("@OrgId", orgId),
            SpListQuery.DateNullable("@DateFrom", null),
            SpListQuery.DateNullable("@DateTo", null),
            SpListQuery.StringNullable("@Tab", "payment"),
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
        if (!request.AppointmentId.HasValue)
            throw new InvalidOperationException("An appointment is required to create a claim.");
        if (request.CptCodes.Count == 0)
            throw new InvalidOperationException("At least one CPT line is required.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        var appointment = await _db.BillingAppointments
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OrgId == orgId && x.AppointmentId == request.AppointmentId.Value, ct)
            ?? throw new InvalidOperationException("The selected appointment does not belong to this tenant and organization.");
        if (await _db.BillingClaims.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.AppointmentId == appointment.AppointmentId, ct))
            throw new InvalidOperationException("A claim already exists for the selected appointment.");

        var patientId = appointment.PatientId
            ?? throw new InvalidOperationException("The selected appointment is not linked to a patient.");
        var requestedProcedureIds = request.CptCodes.Where(x => x.ProcedureCodeId.HasValue)
            .Select(x => x.ProcedureCodeId!.Value).Distinct().ToArray();
        var procedureLookup = await _db.BillingProcedureCodes.AsNoTracking()
            .Where(x => requestedProcedureIds.Contains(x.ProcCodeId) && (x.TenantId == tenantId || x.TenantId == null))
            .ToDictionaryAsync(x => x.ProcCodeId, ct);
        var now = DateTime.UtcNow;
        var claim = new BillingClaim
        {
            TenantId = tenantId,
            OrgId = orgId,
            AppointmentId = appointment.AppointmentId,
            PatientId = patientId,
            CarrierId = request.CarrierId,
            PolicyNumber = request.PolicyNumber,
            TotalBilled = request.CptCodes.Sum(x => Math.Max(1, x.Unit) * (x.ClaimAmount ?? 0)),
            TotalAllowed = request.CptCodes.Sum(x => Math.Max(1, x.Unit) * (x.InsuranceRate ?? 0)),
            PatientResponsibility = request.PatientResponsibility ?? 0,
            StatusId = 19,
            // A pending claim is not submitted until the user explicitly
            // supplies a submission date or changes it to Submitted.
            SubmittedDate = request.SubmittedDate,
            Remarks = request.Remarks,
            CreatedDate = now,
            CreatedByUserId = userId
        };

        _db.BillingClaims.Add(claim);
        await _db.SaveChangesAsync(ct);

        appointment.IsClaimed = true;

        foreach (var cpt in request.CptCodes)
        {
            BillingProcedureCode? procedure = null;
            if (cpt.ProcedureCodeId.HasValue)
                procedureLookup.TryGetValue(cpt.ProcedureCodeId.Value, out procedure);
            var procedureText = (procedure?.Codes ?? cpt.ProcedureCode).Trim();
            if (procedureText.Length == 0)
                throw new InvalidOperationException("Every CPT line must have a valid procedure code.");
            var unit = Math.Max(1, cpt.Unit);
            var unitCharge = cpt.ClaimAmount ?? procedure?.StandardRate ?? 0;
            var unitInsurance = cpt.InsuranceRate ?? 0;
            var code = new BillingApptCode
            {
                TenantId = tenantId,
                ClaimId = claim.ClaimId,
                AppointmentId = appointment.AppointmentId,
                PatientId = patientId,
                OrgId = orgId,
                ProcedureCodeId = procedure?.ProcCodeId,
                ProcedureCode = procedureText,
                Unit = unit,
                TypeId = cpt.TypeId,
                ClaimAmount = unitCharge,
                TotalClaimAmount = unit * unitCharge,
                InsuranceRate = unitInsurance,
                InsuranceAmount = unit * unitInsurance,
                Modifiers = cpt.Modifiers?.Trim(),
                PolicyNumber = cpt.PolicyNumber ?? request.PolicyNumber,
                CarrierId = cpt.CarrierId ?? request.CarrierId,
                InsuranceCompanyId = cpt.InsuranceCompanyId,
                FillSource = "Manual",
                CreatedDate = now,
                CreatedByUserId = userId
            };
            _db.BillingApptCodes.Add(code);
            await _db.SaveChangesAsync(ct);
            AddGroupCodes(tenantId, orgId, claim.ClaimId, code.ApptCodeId, null, cpt.GroupCodes);
        }

        var log = new BillingClaimsLog
        {
            TenantId = tenantId,
            ClaimId = claim.ClaimId,
            StatusId = 1070,
            LogDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            Remarks = "Claim created",
            CreatedDate = now
        };
        _db.BillingClaimsLogs.Add(log);
        _db.BillingPatientAppointmentLogs.Add(new BillingPatientAppointmentLog
        {
            TenantId = tenantId, ApptId = appointment.AppointmentId, ProcessDate = now,
            StatusId = 19, Remarks = "Claim created", CreatedBy = userId, CreatedDate = now
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return claim.ClaimId;
    }

    public async Task<bool> UpdateClaimAsync(
        int tenantId,
        int claimId,
        CreateBillingClaimRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClaimId == claimId, ct);
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
            TenantId = tenantId,
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
        string? policyNumber = null,
        CancellationToken ct = default)
    {
        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClaimId == claimId, ct);
        if (claim == null) return false;

        claim.StatusId = statusId;
        if (claim.SubmittedDate == null && statusId is 1049 or 20 or 1059)
        {
            claim.SubmittedDate = DateTime.UtcNow;
        }
        if (!string.IsNullOrWhiteSpace(remarks))
        {
            claim.Remarks = remarks;
        }
        if (!string.IsNullOrWhiteSpace(policyNumber))
        {
            claim.PolicyNumber = policyNumber.Trim();
        }

        var log = new BillingClaimsLog
        {
            TenantId = tenantId,
            ClaimId = claim.ClaimId,
            StatusId = statusId,
            LogDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? $"Status changed to {statusId}" : remarks,
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingClaimsLogs.Add(log);
        if (claim.AppointmentId.HasValue)
            _db.BillingPatientAppointmentLogs.Add(new BillingPatientAppointmentLog
            {
                TenantId = tenantId, ApptId = claim.AppointmentId, ProcessDate = DateTime.UtcNow,
                StatusId = statusId, Remarks = remarks, CreatedBy = userId, CreatedDate = DateTime.UtcNow
            });

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
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClaimId == claimId, ct);
        if (claim == null) return false;

        var orgId = await ResolveOrganizationIdAsync(tenantId, ct);
        var apptCodeId = request.ApptCodeId ?? await _db.BillingApptCodes
            .Where(x => x.TenantId == tenantId && x.ClaimId == claimId)
            .OrderBy(x => x.ApptCodeId).Select(x => (int?)x.ApptCodeId).FirstOrDefaultAsync(ct);
        if (!apptCodeId.HasValue) return false;

        var code = await _db.BillingApptCodes.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ClaimId == claimId && x.ApptCodeId == apptCodeId.Value, ct);
        if (code == null) return false;
        var normalizedCheck = request.EobCheckNo?.Trim();
        var paymentRow = !string.IsNullOrWhiteSpace(normalizedCheck)
            ? await _db.BillingPaymentRows.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ApptCodeId == apptCodeId && x.EobCheckNo == normalizedCheck && x.IsClosed != true, ct)
            : null;
        var eobStatusId = request.PaidAmount > 0 ? 1059 : 1050;
        if (!string.IsNullOrWhiteSpace(request.EobCheckNo))
        {
            claim.EobCheckNo = request.EobCheckNo.Trim();
        }

        paymentRow ??= new BillingPaymentRow
        {
            TenantId = tenantId,
            ApptCodeId = apptCodeId,
            ClaimId = claimId,
            OrgId = orgId,
            CreatedDate = DateTime.UtcNow
        };
        paymentRow.EobCheckNo = normalizedCheck;
        paymentRow.PaidAmount = request.PaidAmount;
        paymentRow.PatientResponsibility = request.PatientResponsibility;
        paymentRow.Coinsurance = request.Coinsurance;
        paymentRow.Deductible = request.Deductible;
        paymentRow.AllowedAmount = request.AllowedAmount;
        paymentRow.LoadDate = (request.LoadDate ?? DateTime.UtcNow).ToString("O");
        paymentRow.EobStatusId = eobStatusId;
        paymentRow.Remark = request.Remarks;
        paymentRow.IsClosed = request.IsClosed;
        paymentRow.IsReProcess = request.IsReProcess;
        if (paymentRow.Id == 0) _db.BillingPaymentRows.Add(paymentRow);
        await _db.SaveChangesAsync(ct);
        var oldGroups = await _db.BillingClaimGroupCodes
            .Where(x => x.TenantId == tenantId && x.CodeAmtId == paymentRow.Id).ToListAsync(ct);
        _db.BillingClaimGroupCodes.RemoveRange(oldGroups);
        AddGroupCodes(tenantId, orgId, claimId, apptCodeId.Value, paymentRow.Id, request.GroupCodes);

        code.Paid = request.PaidAmount;
        code.PatientResponsibility = request.PatientResponsibility;
        code.CoInsurance = request.Coinsurance;
        code.Deductible = request.Deductible;
        code.EobAllowed = request.AllowedAmount;
        code.LineEobStatusId = eobStatusId;
        code.Balance = (code.InsuranceAmount ?? 0) - request.PaidAmount - (request.PatientResponsibility ?? 0);
        claim.EobStatusId = eobStatusId;
        if (!string.IsNullOrWhiteSpace(normalizedCheck)) claim.EobCheckNo = normalizedCheck;

        var log = new BillingClaimsLog
        {
            TenantId = tenantId,
            ClaimId = claimId,
            StatusId = eobStatusId,
            LogDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            Remarks = $"Payment posted: {request.PaidAmount} (Check: {request.EobCheckNo})",
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingClaimsLogs.Add(log);

        await _db.SaveChangesAsync(ct);
        claim.TotalPaid = await _db.BillingPaymentRows.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ClaimId == claimId && x.IsClosed != true)
            .SumAsync(x => x.PaidAmount ?? 0, ct);
        claim.PatientResponsibility = await _db.BillingApptCodes.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ClaimId == claimId)
            .SumAsync(x => x.PatientResponsibility ?? 0, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<int> ResolveOrganizationIdAsync(int tenantId, CancellationToken ct) =>
        await _db.Tenants.AsNoTracking().Where(x => x.Id == tenantId)
            .Select(x => x.OrganizationTreeId).SingleAsync(ct);

    private void AddGroupCodes(int tenantId, int orgId, int claimId, int apptCodeId, int? paymentId,
        IEnumerable<CreateBillingGroupCodeRequest> rows)
    {
        foreach (var row in rows.Where(x => !string.IsNullOrWhiteSpace(x.GroupCode)))
        {
            _db.BillingClaimGroupCodes.Add(new BillingClaimGroupCode
            {
                TenantId = tenantId, OrgId = orgId, AppClaimId = claimId, AppCodeId = apptCodeId,
                CodeAmtId = paymentId, GroupCode = NormalizeGroupCode(row.GroupCode),
                GroupAmount = row.GroupAmount, CreatedDate = DateTime.UtcNow
            });
        }
    }

    private static string NormalizeGroupCode(string value)
    {
        var code = value.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        if (!code.Contains('-') && code.Length > 2) code = $"{code[..2]}-{code[2..]}";
        return code;
    }

    public async Task<BillingClaimLookupsDto> GetClaimLookupsAsync(int tenantId, CancellationToken ct = default)
    {
        var carriers = await _db.BillingCarriers.AsNoTracking()
            .Where(x => x.TenantId == tenantId || x.TenantId == null)
            .OrderBy(x => x.CarrierName)
            .Select(x => new { x.CarrierId, x.CarrierName })
            .ToListAsync(ct);

        var codes = await _db.BillingProcedureCodes.AsNoTracking()
            .Where(x => x.TenantId == tenantId || x.TenantId == null)
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

    public async Task<IReadOnlyList<BillingClaimLogRowDto>> GetClaimLogsAsync(int tenantId, int claimId, CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingClaimLogRowDto>(
            _db,
            "EXEC dbo.usp_Billing_ClaimLogs_List @TenantId, @ClaimId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Int("ClaimId", claimId));
    }

    public async Task<IReadOnlyList<BillingClaimDocumentRowDto>> GetClaimDocumentsAsync(int tenantId, int claimId, CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingClaimDocumentRowDto>(
            _db,
            "EXEC dbo.usp_Billing_ClaimDocuments_List @TenantId, @ClaimId",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Int("ClaimId", claimId));
    }

    public async Task<BillingBatchActionResultDto> CorrectReprocessClaimsAsync(
        int tenantId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default)
    {
        var specialStatusIds = await _db.Database.SqlQueryRaw<int>(
            "SELECT StatusId FROM dbo.tblEobStatusNeglectRule WITH (NOLOCK) WHERE IsActive = 1 ORDER BY SortOrder"
        ).ToListAsync(ct);
        var specialSet = specialStatusIds.ToHashSet();

        var mixedDefault = await _db.Database.SqlQueryRaw<int?>(
            "SELECT TOP 1 MixedDefaultStatusId FROM dbo.tblEobStatusNeglectRule WITH (NOLOCK) WHERE IsActive = 1 AND MixedDefaultStatusId IS NOT NULL"
        ).FirstOrDefaultAsync(ct) ?? 1074;

        var process20Priorities = await _db.Database.SqlQueryRaw<int>(
            "SELECT StatusId FROM dbo.tblProcessStatus WITH (NOLOCK) WHERE ProcessId = 20 AND S_Value IS NOT NULL ORDER BY S_Value ASC"
        ).ToListAsync(ct);

        var query = _db.BillingClaims
            .Where(x => x.TenantId == tenantId);

        if (dateFrom.HasValue || dateTo.HasValue)
        {
            var df = dateFrom?.ToDateTime(TimeOnly.MinValue);
            var dtLimit = dateTo?.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(x => _db.BillingAppointments.Any(a => 
                a.AppointmentId == x.AppointmentId && a.TenantId == tenantId &&
                (!df.HasValue || a.DOS >= df.Value) &&
                (!dtLimit.HasValue || a.DOS <= dtLimit.Value)));
        }

        var candidateClaims = await query.Select(x => new { x.ClaimId, x.AppointmentId, x.EobStatusId }).ToListAsync(ct);
        if (candidateClaims.Count == 0)
        {
            return new BillingBatchActionResultDto { Success = true, AffectedCount = 0, Message = "No claims found for selected range." };
        }

        var claimIds = candidateClaims.Select(x => x.ClaimId).ToList();

        var amounts = await _db.BillingPaymentRows.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ClaimId.HasValue && claimIds.Contains(x.ClaimId.Value) && x.EobStatusId.HasValue)
            .Select(x => new { ClaimId = x.ClaimId!.Value, EobStatusId = x.EobStatusId!.Value })
            .ToListAsync(ct);

        var amountsByClaim = amounts.GroupBy(x => x.ClaimId).ToDictionary(g => g.Key, g => g.Select(a => a.EobStatusId).ToList());

        var updates = new List<(int ClaimId, int NewStatusId)>();

        foreach (var claim in candidateClaims)
        {
            if (!amountsByClaim.TryGetValue(claim.ClaimId, out var eodStatuses) || eodStatuses.Count == 0)
                continue;

            int targetStatusId;
            bool hasOnlySpecial = eodStatuses.All(s => specialSet.Contains(s));
            if (hasOnlySpecial && specialSet.Count > 0)
            {
                var distinct = eodStatuses.Distinct().ToList();
                targetStatusId = distinct.Count == 1 ? distinct[0] : mixedDefault;
            }
            else
            {
                int? matched = null;
                foreach (var priorityStatus in process20Priorities)
                {
                    if (eodStatuses.Contains(priorityStatus))
                    {
                        matched = priorityStatus;
                        break;
                    }
                }
                targetStatusId = matched ?? eodStatuses.First();
            }

            if (claim.EobStatusId != targetStatusId)
            {
                updates.Add((claim.ClaimId, targetStatusId));
            }
        }

        if (updates.Count > 0)
        {
            foreach (var chunk in updates.Chunk(500))
            {
                var ids = string.Join(",", chunk.Select(u => $"({u.ClaimId}, {u.NewStatusId})"));
                var sql = $@"
UPDATE c
SET c.EobStatusId = v.NewStatusId
FROM dbo.tblInsPTClaims c
JOIN (VALUES {ids}) AS v(ClaimId, NewStatusId) ON c.ClaimId = v.ClaimId
WHERE c.TenantId = {tenantId};";
                await _db.Database.ExecuteSqlRawAsync(sql, ct);
            }
        }

        return new BillingBatchActionResultDto
        {
            Success = true,
            AffectedCount = updates.Count,
            Message = $"Correction completed successfully. Updated {updates.Count} claim(s)."
        };
    }

    public async Task<BillingBatchActionResultDto> SettledClaimsPaymentAsync(
        int tenantId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default)
    {
        var fromDate = dateFrom?.ToString("yyyy-MM-dd");
        var toDate = dateTo?.ToString("yyyy-MM-dd");

        var sqlAmounts = $@"
UPDATE cmd
SET cmd.EobStatusId = 20
FROM dbo.tblClaimCodeAmountDetails cmd
JOIN dbo.tblRoznamcha r 
  ON (r.EobCheckNo = LTRIM(RTRIM(cmd.EobCheckNo)) OR r.InstrumentNo = LTRIM(RTRIM(cmd.EobCheckNo)))
JOIN dbo.tblInsPTClaims c ON c.ClaimId = cmd.ClaimId
LEFT JOIN dbo.tblAppointment a ON a.AppointmentId = c.AppointmentId
WHERE cmd.TenantId = {tenantId}
  AND cmd.EobCheckNo IS NOT NULL
  AND cmd.PaidAmount > 0
  AND (cmd.EobStatusId IS NULL OR cmd.EobStatusId <> 20)
  AND r.EnterStatusId = 20
  AND r.isSettled = 1
  {(fromDate != null ? $"AND (a.DOS >= '{fromDate}')" : "")}
  {(toDate != null ? $"AND (a.DOS <= '{toDate}')" : "")};";

        var affectedAmounts = await _db.Database.ExecuteSqlRawAsync(sqlAmounts, ct);

        var sqlClaims = $@"
UPDATE c
SET c.EobStatusId = 20
FROM dbo.tblInsPTClaims c
LEFT JOIN dbo.tblAppointment a ON a.AppointmentId = c.AppointmentId
WHERE c.TenantId = {tenantId}
  {(fromDate != null ? $"AND (a.DOS >= '{fromDate}')" : "")}
  {(toDate != null ? $"AND (a.DOS <= '{toDate}')" : "")}
  AND EXISTS (
      SELECT 1 FROM dbo.tblClaimCodeAmountDetails cmd
      WHERE cmd.ClaimId = c.ClaimId AND cmd.EobStatusId = 20
  )
  AND NOT EXISTS (
      SELECT 1 FROM dbo.tblClaimCodeAmountDetails cmd
      WHERE cmd.ClaimId = c.ClaimId AND cmd.PaidAmount > 0 AND (cmd.EobStatusId IS NULL OR cmd.EobStatusId <> 20)
  );";

        var affectedClaims = await _db.Database.ExecuteSqlRawAsync(sqlClaims, ct);

        return new BillingBatchActionResultDto
        {
            Success = true,
            AffectedCount = affectedAmounts,
            Message = $"Settled payment processed successfully. {affectedAmounts} remittance record(s) and {affectedClaims} claim(s) marked as Settled."
        };
    }

    public async Task<BillingBatchActionResultDto> BatchUpdateStatusAsync(
        int tenantId, IReadOnlyList<int> claimIds, int statusId, string? remarks, string? userId, DateTime? paymentDate = null, CancellationToken ct = default)
    {
        if (claimIds == null || claimIds.Count == 0)
            return new BillingBatchActionResultDto { Success = false, AffectedCount = 0, Message = "No claims provided." };

        var claims = await _db.BillingClaims
            .Where(x => x.TenantId == tenantId && claimIds.Contains(x.ClaimId))
            .ToListAsync(ct);

        if (claims.Count == 0)
            return new BillingBatchActionResultDto { Success = false, AffectedCount = 0, Message = "No matching claims found." };

        var now = DateTime.UtcNow;
        var logDateStr = now.ToString("yyyy-MM-dd HH:mm");

        foreach (var claim in claims)
        {
            claim.StatusId = statusId;
            if (statusId is 1049 or 20 or 1059)
            {
                claim.SubmittedDate ??= paymentDate ?? now;
            }
            if (!string.IsNullOrWhiteSpace(remarks))
            {
                claim.Remarks = remarks;
            }

            _db.BillingClaimsLogs.Add(new BillingClaimsLog
            {
                TenantId = tenantId,
                ClaimId = claim.ClaimId,
                StatusId = statusId,
                LogDate = logDateStr,
                Remarks = string.IsNullOrWhiteSpace(remarks) ? $"Status changed to {statusId}" : remarks,
                CreatedDate = now
            });
        }

        await _db.SaveChangesAsync(ct);

        return new BillingBatchActionResultDto
        {
            Success = true,
            AffectedCount = claims.Count,
            Message = $"Status updated to {statusId} for {claims.Count} claim(s)."
        };
    }

    public async Task<BillingBatchActionResultDto> BatchReprocessClaimsAsync(
        int tenantId, IReadOnlyList<int> claimIds, bool isReProcess, string? userId, CancellationToken ct = default)
    {
        if (claimIds == null || claimIds.Count == 0)
            return new BillingBatchActionResultDto { Success = false, AffectedCount = 0, Message = "No claims provided." };

        var claims = await _db.BillingClaims
            .Where(x => x.TenantId == tenantId && claimIds.Contains(x.ClaimId))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var logDateStr = now.ToString("yyyy-MM-dd HH:mm");

        foreach (var claim in claims)
        {
            claim.IsRep = isReProcess;
            _db.BillingClaimsLogs.Add(new BillingClaimsLog
            {
                TenantId = tenantId,
                ClaimId = claim.ClaimId,
                StatusId = claim.StatusId,
                LogDate = logDateStr,
                Remarks = isReProcess ? "Marked for Reprocess" : "Unmarked from Reprocess",
                CreatedDate = now
            });
        }

        var idList = string.Join(",", claimIds);
        await _db.Database.ExecuteSqlRawAsync(
            $"UPDATE dbo.tblClaimCodeAmountDetails SET IsReProcess = {(isReProcess ? 1 : 0)} WHERE TenantId = {tenantId} AND ClaimId IN ({idList});",
            ct);

        await _db.SaveChangesAsync(ct);

        return new BillingBatchActionResultDto
        {
            Success = true,
            AffectedCount = claims.Count,
            Message = $"Updated reprocess state for {claims.Count} claim(s)."
        };
    }

    public async Task<BillingBatchActionResultDto> BatchArClaimsAsync(
        int tenantId, IReadOnlyList<int> claimIds, bool isAr, string? userId, CancellationToken ct = default)
    {
        if (claimIds == null || claimIds.Count == 0)
            return new BillingBatchActionResultDto { Success = false, AffectedCount = 0, Message = "No claims provided." };

        var claims = await _db.BillingClaims
            .Where(x => x.TenantId == tenantId && claimIds.Contains(x.ClaimId))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var logDateStr = now.ToString("yyyy-MM-dd HH:mm");

        foreach (var claim in claims)
        {
            claim.IsAr = isAr;
            _db.BillingClaimsLogs.Add(new BillingClaimsLog
            {
                TenantId = tenantId,
                ClaimId = claim.ClaimId,
                StatusId = claim.StatusId,
                LogDate = logDateStr,
                Remarks = isAr ? "Moved to AR" : "Removed from AR",
                CreatedDate = now
            });
        }

        await _db.SaveChangesAsync(ct);

        return new BillingBatchActionResultDto
        {
            Success = true,
            AffectedCount = claims.Count,
            Message = $"Updated AR state for {claims.Count} claim(s)."
        };
    }
}

