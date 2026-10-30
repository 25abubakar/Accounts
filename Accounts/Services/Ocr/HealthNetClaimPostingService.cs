using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Ocr.Parsers;
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
                _logger.LogError(ex, "Error posting a Health Net claim for tenant {OrgId}", orgId);
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
        // Health Net is intentionally strict: account number is the only patient
        // identity. MID/name fallback can post PHI and money to the wrong person.
        if (string.IsNullOrWhiteSpace(p.PatientNo)) return null;

        var tenantMatches = await _db.BillingPatients
            .Where(x => x.OrgId == orgId && x.PatientNo == p.PatientNo)
            .ToListAsync(ct);
        var tenantMatch = await ResolveUniqueByServiceDateAsync(tenantMatches, p, orgId, ct);
        if (tenantMatch != null || tenantMatches.Count > 0) return tenantMatch;

        // Legacy unassigned patients are considered only when the active tenant
        // has no match. Patients belonging to another tenant are never considered.
        var unassignedMatches = await _db.BillingPatients
            .Where(x => x.OrgId == null && x.PatientNo == p.PatientNo)
            .ToListAsync(ct);
        return await ResolveUniqueByServiceDateAsync(unassignedMatches, p, null, ct);
    }

    private async Task<BillingPatient?> ResolveUniqueByServiceDateAsync(
        List<BillingPatient> candidates,
        ParsedPatient parsed,
        int? appointmentOrgId,
        CancellationToken ct)
    {
        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0) return null;

        var serviceDates = parsed.Procedures
            .Select(x => x.DateOfServiceParsed ?? ServiceDateParser.Parse(x.DateOfService))
            .Where(x => x.HasValue)
            .Select(x => x!.Value.Date)
            .ToHashSet();
        if (serviceDates.Count == 0) return null;

        var candidateIds = candidates.Select(x => x.PatientId).ToList();
        var appointments = await _db.BillingAppointments.AsNoTracking()
            .Where(x => x.PatientId.HasValue && candidateIds.Contains(x.PatientId.Value) && x.OrgId == appointmentOrgId)
            .Select(x => new { x.PatientId, x.AppointmentDate })
            .ToListAsync(ct);
        var matchingIds = appointments
            .Where(x => ServiceDateParser.Parse(x.AppointmentDate) is { } date && serviceDates.Contains(date.Date))
            .Select(x => x.PatientId)
            .Distinct()
            .ToList();
        return matchingIds.Count == 1
            ? candidates.Single(x => x.PatientId == matchingIds[0])
            : null;
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
                        x.OrgId == orgId &&
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
        var claim = await _db.BillingClaims
            .FirstOrDefaultAsync(x => x.ClaimId == bestMatch.ClaimId && x.OrgId == orgId, ct);
        var paid = proc.ProviderPaid ?? 0;
        var claimStatusId = paid > 0 ? 1059 : 1049;

        if (claim != null)
        {
            claim.TotalPaid = p.ProviderPaid ?? paid;
            claim.TotalBilled = p.Billed ?? proc.Billed;
            claim.TotalAllowed = p.Allowed ?? proc.Allowed;
            claim.PatientResponsibility = p.PatientResponsibility;
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
            x.EobCheckNo == header.CheckEFT &&
            x.OrgId == orgId, ct);

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
            .Where(x => x.Type == "HealthNet" && x.OrgId == orgId)
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

