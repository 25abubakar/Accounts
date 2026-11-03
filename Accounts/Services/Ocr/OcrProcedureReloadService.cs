using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Ocr;

public sealed class OcrProcedureReloadService
{
    private readonly ApplicationDbContext _db;
    private readonly IEobClaimPostingService _standardPosting;
    private readonly HealthNetClaimPostingService _healthNetPosting;

    public OcrProcedureReloadService(
        ApplicationDbContext db,
        IEobClaimPostingService standardPosting,
        HealthNetClaimPostingService healthNetPosting)
    {
        _db = db;
        _standardPosting = standardPosting;
        _healthNetPosting = healthNetPosting;
    }

    public async Task<OcrProcedureReloadResultDto> ReloadAsync(
        int documentId,
        int procedureId,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        var upload = await _db.OcrUploadedRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == documentId && x.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("OCR document was not found in the current tenant.");
        var header = await _db.OcrDocHeaders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EobAttId == documentId && x.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("OCR document header was not found in the current tenant.");
        var procedure = await _db.OcrProcedureDetails.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == procedureId && x.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("OCR procedure was not found in the current tenant.");
        if (!procedure.OcrPatientId.HasValue)
            throw new InvalidOperationException("The OCR procedure is not linked to a patient.");
        var patient = await _db.OcrPatientDetails.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == procedure.OcrPatientId.Value && x.OcrHeaderId == header.Id && x.OrgId == orgId, ct)
            ?? throw new InvalidOperationException("The OCR procedure does not belong to this document.");

        var parsedProcedure = new ParsedProcedure
        {
            RenderingProviderId = procedure.RenderingProviderId,
            DayMonth = procedure.DayMonth,
            DateOfService = procedure.DateOfService,
            POS = procedure.POS,
            NOS = procedure.NOS,
            ProcedureCode = procedure.ProcedureCode,
            Modifiers = procedure.Modifiers,
            Billed = procedure.Billed,
            Allowed = procedure.Allowed,
            Deductible = procedure.Deductible,
            CoInsurance = procedure.CoInsurance,
            NOS_Val = procedure.NOS_Val,
            ProviderPaid = procedure.ProviderPaid,
            SubNOS = procedure.SubNOS,
            ProviderDiscount = procedure.ProviderDiscount,
            OtherPlan = procedure.OtherPlan,
            OtherAdjustments = procedure.OtherAdjustments,
            Copay = procedure.Copay,
            NonCovered = procedure.NonCovered,
            PatientResponsibility = procedure.PatientResponsibility,
            Interest = procedure.Interest,
            GrpRows = await _db.OcrGrpAmtDetails.AsNoTracking()
                .Where(x => x.ProcCodeId == procedureId)
                .Select(x => new ParsedGrp { GRPCode = x.GRPCode, RcAmt = x.Rc_Amt, Remark = x.REM })
                .ToListAsync(ct),
            Remarks = await _db.OcrProRemDetails.AsNoTracking()
                .Where(x => x.OcrProcCodeId == procedureId && x.REM != null)
                .Select(x => x.REM!)
                .ToListAsync(ct)
        };
        var parsedPatient = new ParsedPatient
        {
            PatientName = patient.PatientName,
            MID = patient.MID,
            PatientNo = patient.PatientNo,
            ICN = patient.ICN,
            Billed = patient.Billed,
            Allowed = patient.Allowed,
            Deductible = patient.Deductible,
            CoInsurance = patient.CoInsurance,
            RcAmount = patient.RcAmount,
            ProviderPaid = patient.ProviderPaid,
            PatientResponsibility = patient.PatientResponsibility,
            AdjustmentTotal = patient.AdjustmentTotal,
            Interest = patient.Interest,
            LateFiling = patient.LateFiling,
            Net = patient.Net,
            ProviderDiscount = patient.ProviderDiscount,
            OtherPlanPayment = patient.OtherPlanPayment,
            OtherAdjustments = patient.OtherAdjustments,
            NonCovered = patient.NonCovered,
            Procedures = [parsedProcedure]
        };
        var parsed = new ParsedEobHeader
        {
            DocTitle = header.DocTitle,
            CheckEFT = header.Check_EFT ?? upload.Check_EFT,
            StatementDate = header.StatementDate ?? upload.Eob_Date,
            Attachment = upload.Attachment,
            Source = upload.Source,
            Patients = [parsedPatient]
        };

        if (string.Equals(upload.Source, "HEALTH NET Docs", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _healthNetPosting.PostAsync(parsed, documentId, orgId, userId, ct);
            return new OcrProcedureReloadResultDto
            {
                Success = result.PostedClaimCount > 0,
                PostedCount = result.PostedClaimCount,
                SkippedCount = result.SkippedClaimCount,
                Warnings = result.Warnings,
                Message = result.PostedClaimCount > 0
                    ? "Health Net procedure was matched and reposted successfully."
                    : result.Warnings.FirstOrDefault() ?? "No safe Health Net match was found; no payment was changed."
            };
        }

        var payer = upload.Source switch
        {
            "AVAILITY Docs" => "AVAILITY",
            "CCA Docs" => "CCA",
            "Lassale Docs" => "LASALLE",
            "Zelis Docs" => "ZELIS",
            _ => throw new InvalidOperationException("This OCR source does not support procedure reload.")
        };
        var standard = await _standardPosting.PostAsync(parsed, documentId, payer, orgId, userId, ct);
        return new OcrProcedureReloadResultDto
        {
            Success = standard.PostedCount > 0,
            PostedCount = standard.PostedCount,
            SkippedCount = standard.SkippedCount,
            Warnings = standard.Warnings,
            Message = standard.PostedCount > 0
                ? "Procedure was matched and reposted successfully."
                : standard.Warnings.FirstOrDefault() ?? "No safe claim match was found; no payment was changed."
        };
    }
}
