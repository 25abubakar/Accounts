using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Ocr.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Accounts.Services.Ocr;

public sealed class EobClaimPostingService : IEobClaimPostingService
{
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<EobClaimPostingService> _logger;

    public EobClaimPostingService(
        ApplicationDbContext db,
        HttpClient http,
        IConfiguration config,
        ILogger<EobClaimPostingService> logger)
    {
        _db = db;
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<PostingResult> PostAsync(
        ParsedEobHeader parsed,
        int savedUploadId,
        string payer,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        var result = new PostingResult();
        if (parsed.Patients.Count == 0) return result;

        var carrierId = await ResolveCarrierIdAsync(parsed.InsuranceName, orgId, ct);

        foreach (var p in parsed.Patients)
        {
            if (string.IsNullOrWhiteSpace(p.PatientName) && string.IsNullOrWhiteSpace(p.PatientNo))
            {
                result.SkippedCount++;
                continue;
            }

            try
            {
                var patient = await ResolveOrCreatePatientAsync(p, orgId, userId, ct);
                var patientResp = ResolvePatientResponsibility(p, payer);

                foreach (var proc in p.Procedures)
                {
                    await PostProcedureLineAsync(parsed, p, proc, patient, carrierId, savedUploadId, patientResp, payer, orgId, userId, result, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post an OCR claim for tenant {OrgId}", orgId);
                result.Warnings.Add($"Failed posting patient {p.PatientName}: {ex.Message}");
                result.SkippedCount++;
            }
        }

        return result;
    }

    private static decimal? ResolvePatientResponsibility(ParsedPatient p, string payer)
    {
        switch (payer.ToUpperInvariant())
        {
            case "AVAILITY":
                // Availity: last GRP with PR
                foreach (var proc in p.Procedures.AsEnumerable().Reverse())
                {
                    var lastPr = proc.GrpRows.LastOrDefault(g => g.GRPCode != null && g.GRPCode.StartsWith("PR", StringComparison.OrdinalIgnoreCase));
                    if (lastPr?.RcAmt != null) return lastPr.RcAmt;
                }
                return p.PatientResponsibility;

            case "CCA":
            case "LASALLE":
                // Copay
                var copay = p.Procedures.FirstOrDefault(x => x.Copay.HasValue && x.Copay.Value > 0)?.Copay;
                return copay ?? p.PatientResponsibility;

            case "ZELIS":
                // Zelis: patient obligation / responsibility
                return p.PatientResponsibility ?? p.Procedures.FirstOrDefault(x => x.PatientResponsibility.HasValue)?.PatientResponsibility;

            default:
                return p.PatientResponsibility;
        }
    }

    private static string CleanMid(string? mid, string payer)
    {
        if (string.IsNullOrWhiteSpace(mid)) return "";
        var cleaned = mid.Replace("-", "").Replace(" ", "").Trim();
        return cleaned;
    }

    private async Task<BillingPatient> ResolveOrCreatePatientAsync(ParsedPatient p, int orgId, string? userId, CancellationToken ct)
    {
        var cleanMidVal = CleanMid(p.MID, "");
        var pat = await _db.BillingPatients.FirstOrDefaultAsync(x =>
            x.OrgId == orgId &&
            ((p.PatientNo != null && x.PatientNo == p.PatientNo) ||
             (cleanMidVal != "" && x.PatientNo == cleanMidVal)), ct);

        if (pat != null) return pat;

        // Parse Name into First / Last
        string firstName = "Unknown";
        string lastName = "Patient";
        if (!string.IsNullOrWhiteSpace(p.PatientName))
        {
            var rawName = p.PatientName.Trim();
            if (rawName.Contains(','))
            {
                var parts = rawName.Split(',', 2, StringSplitOptions.TrimEntries);
                lastName = parts[0];
                firstName = parts.Length > 1 ? parts[1] : "";
            }
            else
            {
                var parts = rawName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    firstName = parts[0];
                    lastName = string.Join(" ", parts.Skip(1));
                }
                else if (parts.Length == 1)
                {
                    firstName = parts[0];
                    lastName = parts[0];
                }
            }
        }

        var person = new BillingPerson
        {
            FirstName = firstName,
            LastName = lastName,
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingPersons.Add(person);
        await _db.SaveChangesAsync(ct);

        pat = new BillingPatient
        {
            OrgId = orgId,
            PatientNo = p.PatientNo ?? cleanMidVal,
            PatientName = $"{firstName} {lastName}".Trim(),
            PersonId = person.PersonId,
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingPatients.Add(pat);
        await _db.SaveChangesAsync(ct);

        return pat;
    }

    private async Task<int?> ResolveCarrierIdAsync(string? insuranceName, int orgId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(insuranceName)) return null;

        var name = insuranceName.Trim();
        var carrier = await _db.BillingCarriers.FirstOrDefaultAsync(x =>
            x.OrgId == orgId && x.CarrierName == name, ct);

        if (carrier != null) return carrier.CarrierId;

        // Base insurance
        var baseIns = await _db.BillingBaseInsurances.FirstOrDefaultAsync(x => x.InsuranceName == name, ct);
        if (baseIns == null)
        {
            baseIns = new BillingBaseInsurance { InsuranceName = name };
            _db.BillingBaseInsurances.Add(baseIns);
            await _db.SaveChangesAsync(ct);
        }

        // Insurance New
        var insNew = await _db.BillingInsuranceNews.FirstOrDefaultAsync(x => x.BaseInsuranceId == baseIns.Id, ct);
        if (insNew == null)
        {
            insNew = new BillingInsuranceNew
            {
                BaseInsuranceId = baseIns.Id,
                Name = name
            };
            _db.BillingInsuranceNews.Add(insNew);
            await _db.SaveChangesAsync(ct);
        }

        carrier = new BillingCarrier
        {
            CarrierName = name,
            OrgId = orgId,
            InsuranceGroupId = 1,
            BaseInsuranceId = baseIns.Id,
            InsuranceNewId = insNew.Id,
            CreatedDate = DateTime.UtcNow
        };
        _db.BillingCarriers.Add(carrier);
        await _db.SaveChangesAsync(ct);

        return carrier.CarrierId;
    }

    private async Task PostProcedureLineAsync(
        ParsedEobHeader header,
        ParsedPatient p,
        ParsedProcedure proc,
        BillingPatient patient,
        int? carrierId,
        int savedUploadId,
        decimal? patientResp,
        string payer,
        int orgId,
        string? userId,
        PostingResult result,
        CancellationToken ct)
    {
        var dosStr = proc.DateOfService ?? header.StatementDate ?? DateTime.UtcNow.ToString("MM/dd/yyyy");

        // 1. Appointment
        var appt = await _db.BillingAppointments.FirstOrDefaultAsync(x =>
            x.PatientId == patient.PatientId &&
            x.AppointmentDate == dosStr &&
            x.OrgId == orgId, ct);

        if (appt == null)
        {
            appt = new BillingAppointment
            {
                PatientId = patient.PatientId,
                OrgId = orgId,
                AppointmentDate = dosStr,
                StatusId = 1045,
                IsClaimed = true,
                IsCreatedFromEob = true,
                CreatedDate = DateTime.UtcNow,
                CreatedByUserId = userId
            };
            _db.BillingAppointments.Add(appt);
            await _db.SaveChangesAsync(ct);
        }

        // 2. Claim
        var paid = proc.ProviderPaid ?? 0;
        var claimStatusId = paid > 0 ? 1059 : 1049;

        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x =>
            x.AppointmentId == appt.AppointmentId &&
            x.PatientId == patient.PatientId &&
            x.EobCheckNo == header.CheckEFT &&
            x.OrgId == orgId, ct);

        if (claim == null)
        {
            claim = new BillingClaim
            {
                AppointmentId = appt.AppointmentId,
                PatientId = patient.PatientId,
                OrgId = orgId,
                EobCheckNo = header.CheckEFT,
                Attachment = header.Attachment,
                TotalPaid = paid,
                PatientResponsibility = patientResp,
                TotalBilled = proc.Billed,
                TotalAllowed = proc.Allowed,
                StatusId = claimStatusId,
                IsCreatedFromEob = true,
                SubmittedDate = DateTime.UtcNow,
                CreatedDate = DateTime.UtcNow,
                CreatedByUserId = userId
            };
            _db.BillingClaims.Add(claim);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            claim.TotalPaid = p.ProviderPaid ?? paid;
            claim.TotalBilled = p.Billed ?? proc.Billed;
            claim.TotalAllowed = p.Allowed ?? proc.Allowed;
            claim.PatientResponsibility = patientResp;
            claim.Attachment = header.Attachment;
            claim.StatusId = claimStatusId;
            await _db.SaveChangesAsync(ct);
        }

        // 3. Procedure code lookup/insert
        int? procCodeId = null;
        if (!string.IsNullOrWhiteSpace(proc.ProcedureCode))
        {
            var pCode = await _db.BillingProcedureCodes.FirstOrDefaultAsync(x => x.Codes == proc.ProcedureCode, ct);
            if (pCode == null)
            {
                pCode = new BillingProcedureCode
                {
                    Codes = proc.ProcedureCode,
                    Description = proc.ProcedureCode
                };
                _db.BillingProcedureCodes.Add(pCode);
                await _db.SaveChangesAsync(ct);
            }
            procCodeId = pCode.ProcCodeId;
        }

        // 4. Line EOB Status
        var coins = proc.CoInsurance ?? 0;
        int lineEobStatus;
        if (paid > 0 && coins > 0) lineEobStatus = 1062;
        else if (coins > 0 && paid <= 0) lineEobStatus = 1062;
        else if (paid <= 0 && coins <= 0) lineEobStatus = 1050;
        else lineEobStatus = 1059;

        // 5. ApptCode
        var apptCode = await _db.BillingApptCodes.FirstOrDefaultAsync(x =>
            x.AppointmentId == appt.AppointmentId &&
            x.ClaimId == claim.ClaimId &&
            x.PatientId == patient.PatientId &&
            x.OrgId == orgId &&
            x.ProcedureCode == proc.ProcedureCode &&
            x.DateOfService == dosStr, ct);

        if (apptCode == null)
        {
            apptCode = new BillingApptCode
            {
                AppointmentId = appt.AppointmentId,
                ClaimId = claim.ClaimId,
                PatientId = patient.PatientId,
                OrgId = orgId,
                ProcedureCodeId = procCodeId,
                ProcedureCode = proc.ProcedureCode,
                DateOfService = dosStr,
                ClaimAmount = proc.Billed,
                Modifiers = proc.Modifiers,
                IsCreatedFromEobs = true,
                CreatedDate = DateTime.UtcNow,
                CreatedByUserId = userId
            };
            _db.BillingApptCodes.Add(apptCode);
        }

        apptCode.EobAllowed = proc.Allowed;
        apptCode.Paid = proc.ProviderPaid;
        apptCode.CoInsurance = proc.CoInsurance;
        apptCode.Deductible = proc.Deductible;
        apptCode.PatientResponsibility = patientResp;
        apptCode.FillSource = header.Source ?? payer;
        apptCode.LineEobStatusId = lineEobStatus;
        apptCode.CarrierId = carrierId;
        apptCode.IsUpload = true;
        await _db.SaveChangesAsync(ct);

        // 6. Payment Row (tblClaimCodeAmountDetails)
        var payment = await _db.BillingPaymentRows.FirstOrDefaultAsync(x =>
            x.ApptCodeId == apptCode.ApptCodeId &&
            x.EobCheckNo == header.CheckEFT &&
            x.OrgId == orgId, ct);
        if (payment == null)
        {
            payment = new BillingPaymentRow
            {
                ApptCodeId = apptCode.ApptCodeId,
                ClaimId = claim.ClaimId,
                OrgId = orgId,
                EobCheckNo = header.CheckEFT,
                CreatedDate = DateTime.UtcNow
            };
            _db.BillingPaymentRows.Add(payment);
        }
        payment.PaidAmount = proc.ProviderPaid;
        payment.PatientResponsibility = patientResp;

        // 7. Claim EOB Doc
        var docExists = await _db.BillingClaimEobDocs.AnyAsync(x =>
            x.ClaimId == claim.ClaimId && x.EobCheckNo == header.CheckEFT, ct);
        if (!docExists)
        {
            _db.BillingClaimEobDocs.Add(new BillingClaimEobDoc
            {
                ClaimId = claim.ClaimId,
                EobCheckNo = header.CheckEFT,
                Attachment = header.Attachment,
                UploadDate = header.StatementDate,
                CreatedDate = DateTime.UtcNow
            });
        }

        // 8. Claims Log
        var logDate = DateTime.UtcNow.ToString("MM/dd/yyyy");
        var logExists = await _db.BillingClaimsLogs.AnyAsync(x =>
            x.ClaimId == claim.ClaimId && x.StatusId == claimStatusId && x.LogDate == logDate, ct);
        if (!logExists)
        {
            _db.BillingClaimsLogs.Add(new BillingClaimsLog
            {
                ClaimId = claim.ClaimId,
                StatusId = claimStatusId,
                LogDate = logDate,
                CreatedDate = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);

        // Triggers
        if (lineEobStatus == 1062)
        {
            if (payer.Equals("AVAILITY", StringComparison.OrdinalIgnoreCase))
            {
                await SendToBillingAsync(patient.PatientNo ?? "", dosStr, proc.ProcedureCode, payer, orgId, ct);
            }
            else
            {
                await SendToBillingAsync(patient.PatientNo ?? "", dosStr, proc.ProcedureCode, payer, orgId, ct);
            }
        }

        if (payer.Equals("AVAILITY", StringComparison.OrdinalIgnoreCase) && proc.ProcedureCode != null && proc.ProviderPaid.HasValue)
        {
            await AmountSendToBillingAsync(patient.PatientNo ?? "", "", dosStr, [(proc.ProcedureCode, proc.ProviderPaid.Value)], ct);
        }

        result.PostedCount++;
    }

    public async Task SendToBillingAsync(
        string patientNo,
        string? dos,
        string? procedureCode,
        string payer,
        int orgId,
        CancellationToken ct = default)
    {
        var url = _config["Ocr:SendToBillingUrl"] ?? "https://billingprocess.laltechnologies.com:2025/Triggers/EOBTrigger";
        try
        {
            var payload = new
            {
                PatientNo = patientNo,
                DOS = dos,
                ProcedureCode = procedureCode,
                Payer = payer,
                OrgId = orgId
            };
            await _http.PostAsJsonAsync(url, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send to billing trigger: {Url}", url);
        }
    }

    public async Task AmountSendToBillingAsync(
        string patientNo,
        string? policyNo,
        string? dos,
        IEnumerable<(string code, decimal paid)> codes,
        CancellationToken ct = default)
    {
        var url = _config["Ocr:BillingAmountUrl"] ?? "https://billingprocess.laltechnologies.com:2025/Triggers/LogClaimsEOB";
        try
        {
            var payload = new
            {
                PatientNo = patientNo,
                PolicyNo = policyNo,
                DOS = dos,
                Codes = codes.Select(c => new { Code = c.code, Paid = c.paid }).ToList()
            };
            await _http.PostAsJsonAsync(url, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send amount to billing: {Url}", url);
        }
    }
}

