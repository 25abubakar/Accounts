using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Accounts.Services.Ocr;

public sealed class HealthNetClaimPostingService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<HealthNetClaimPostingService> _logger;

    public HealthNetClaimPostingService(ApplicationDbContext db, ILogger<HealthNetClaimPostingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthNetFileResult> PostAsync(
        ParsedEobHeader parsed,
        int uploadId,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        var result = new HealthNetFileResult
        {
            FileName = parsed.Attachment,
            SavedDocumentId = uploadId
        };

        if (parsed.Patients.Count == 0)
        {
            result.Status = "Successful";
            return result;
        }

        int totalAttempted = 0;

        foreach (var p in parsed.Patients)
        {
            try
            {
                // Rule: NEVER creates patient or appointment
                var patient = await ResolvePatientAsync(p, orgId, ct);
                if (patient == null)
                {
                    result.Warnings.Add($"Patient '{p.PatientName}' (Account: {p.PatientNo}, MID: {p.MID}) not found in database. Skipped.");
                    result.SkippedClaimCount += Math.Max(1, p.Procedures.Count);
                    continue;
                }

                foreach (var proc in p.Procedures)
                {
                    totalAttempted++;
                    var matched = await PostLineAsync(parsed, p, proc, patient, orgId, userId, result, ct);
                    if (matched)
                    {
                        result.PostedClaimCount++;
                    }
                    else
                    {
                        result.SkippedClaimCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error posting Health Net patient {PatientName}", p.PatientName);
                result.Warnings.Add($"Error on patient {p.PatientName}: {ex.Message}");
                result.SkippedClaimCount++;
            }
        }

        // Save new explanation code rules into tblEOBGrpCodeRules
        await SaveGrpCodeRulesAsync(parsed, orgId, ct);

        if (result.PostedClaimCount > 0 && result.SkippedClaimCount == 0)
        {
            result.Status = "Successful";
        }
        else if (result.PostedClaimCount > 0 && result.SkippedClaimCount > 0)
        {
            result.Status = "Partial";
        }
        else
        {
            result.Status = result.PostedClaimCount == 0 && totalAttempted > 0 ? "Failed" : "Successful";
        }

        return result;
    }

    private async Task<BillingPatient?> ResolvePatientAsync(ParsedPatient p, int orgId, CancellationToken ct)
    {
        // 1. Match by Account / PatientNo in current org
        if (!string.IsNullOrWhiteSpace(p.PatientNo))
        {
            var byOrg = await _db.BillingPatients.FirstOrDefaultAsync(x => x.OrgId == orgId && x.PatientNo == p.PatientNo, ct);
            if (byOrg != null) return byOrg;

            var anyOrg = await _db.BillingPatients.FirstOrDefaultAsync(x => x.PatientNo == p.PatientNo, ct);
            if (anyOrg != null) return anyOrg;
        }

        // 2. Match by MID
        if (!string.IsNullOrWhiteSpace(p.MID))
        {
            var byMid = await _db.BillingPatients.FirstOrDefaultAsync(x => x.OrgId == orgId && x.PatientNo == p.MID, ct);
            if (byMid != null) return byMid;

            var anyMid = await _db.BillingPatients.FirstOrDefaultAsync(x => x.PatientNo == p.MID, ct);
            if (anyMid != null) return anyMid;
        }

        // 3. Match by name
        if (!string.IsNullOrWhiteSpace(p.PatientName))
        {
            var byName = await _db.BillingPatients.FirstOrDefaultAsync(x => x.OrgId == orgId && x.PatientName == p.PatientName, ct);
            if (byName != null) return byName;
        }

        return null;
    }

    private async Task<bool> PostLineAsync(
        ParsedEobHeader header,
        ParsedPatient p,
        ParsedProcedure proc,
        BillingPatient patient,
        int orgId,
        string? userId,
        HealthNetFileResult fileResult,
        CancellationToken ct)
    {
        var dos = proc.DateOfService ?? header.StatementDate;

        // Line matching scoring: base 50 + 30 policy + 15 modifier
        var candidateCodes = await _db.BillingApptCodes
            .Where(x => x.PatientId == patient.PatientId &&
                        (x.OrgId == orgId || x.OrgId == null) &&
                        x.ProcedureCode == proc.ProcedureCode)
            .ToListAsync(ct);

        if (candidateCodes.Count == 0)
        {
            fileResult.Warnings.Add($"No appointment code found for patient {patient.PatientName}, DOS {dos}, code {proc.ProcedureCode}.");
            return false;
        }

        BillingApptCode? bestMatch = null;
        int highestScore = -1;
        bool isAmbiguous = false;

        foreach (var c in candidateCodes)
        {
            int score = 50; // base code match
            if (dos != null && c.DateOfService == dos) score += 30;
            if (proc.Modifiers != null && c.Modifiers == proc.Modifiers) score += 15;

            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = c;
                isAmbiguous = false;
            }
            else if (score == highestScore)
            {
                isAmbiguous = true;
            }
        }

        if (isAmbiguous || bestMatch == null)
        {
            fileResult.Warnings.Add($"Ambiguous match for procedure {proc.ProcedureCode} on patient {patient.PatientName}. Skipped.");
            return false;
        }

        // Claim update
        var claim = await _db.BillingClaims.FirstOrDefaultAsync(x => x.ClaimId == bestMatch.ClaimId, ct);
        var paid = proc.ProviderPaid ?? 0;
        var claimStatusId = paid > 0 ? 1059 : 1049;

        if (claim != null)
        {
            claim.TotalPaid = (claim.TotalPaid ?? 0) + paid;
            claim.EobCheckNo = header.CheckEFT;
            claim.StatusId = claimStatusId;
        }

        // Line EOB Status
        var coins = proc.CoInsurance ?? 0;
        int lineEobStatus;
        if (paid > 0 && coins > 0) lineEobStatus = 1062;
        else if (coins > 0 && paid <= 0) lineEobStatus = 1062;
        else if (paid <= 0 && coins <= 0) lineEobStatus = 1050;
        else lineEobStatus = 1059;

        bestMatch.Paid = proc.ProviderPaid;
        bestMatch.CoInsurance = proc.CoInsurance;
        bestMatch.Deductible = proc.Deductible;
        bestMatch.LineEobStatusId = lineEobStatus;

        // Dedup payment rows by ApptCodeId + CheckNo
        var existingPayment = await _db.BillingPaymentRows.FirstOrDefaultAsync(x =>
            x.ApptCodeId == bestMatch.ApptCodeId &&
            x.EobCheckNo == header.CheckEFT, ct);

        if (existingPayment == null)
        {
            var payment = new BillingPaymentRow
            {
                ApptCodeId = bestMatch.ApptCodeId,
                ClaimId = bestMatch.ClaimId,
                OrgId = orgId,
                EobCheckNo = header.CheckEFT,
                PaidAmount = proc.ProviderPaid,
                PatientResponsibility = proc.PatientResponsibility,
                CreatedDate = DateTime.UtcNow
            };
            _db.BillingPaymentRows.Add(payment);
        }
        else
        {
            existingPayment.PaidAmount = proc.ProviderPaid;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task SaveGrpCodeRulesAsync(ParsedEobHeader header, int orgId, CancellationToken ct)
    {
        var existingKeys = await _db.EobGrpCodeRules
            .Where(x => x.Type == "HealthNet")
            .Select(x => x.ExplanationCodeKey)
            .ToListAsync(ct);

        var keySet = new HashSet<string>(existingKeys.Where(k => k != null)!, StringComparer.OrdinalIgnoreCase);

        foreach (var p in header.Patients)
        {
            foreach (var proc in p.Procedures)
            {
                foreach (var grp in proc.GrpRows)
                {
                    if (string.IsNullOrWhiteSpace(grp.GRPCode)) continue;

                    // Normalize CO45 -> CO-45
                    var normalized = NormalizeGrpCode(grp.GRPCode);
                    if (keySet.Add(normalized))
                    {
                        var rule = new EobGrpCodeRule
                        {
                            OrgId = orgId,
                            ExplanationCodeKey = normalized,
                            Description = grp.Remark ?? normalized,
                            Type = "HealthNet",
                            CreatedDate = DateTime.UtcNow
                        };
                        _db.EobGrpCodeRules.Add(rule);
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string NormalizeGrpCode(string code)
    {
        var trimmed = code.Trim();
        // If like CO45, convert to CO-45
        var match = Regex.Match(trimmed, @"^([A-Za-z]{2})(\d+)$");
        if (match.Success)
        {
            return $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}";
        }
        return trimmed.ToUpperInvariant();
    }
}
