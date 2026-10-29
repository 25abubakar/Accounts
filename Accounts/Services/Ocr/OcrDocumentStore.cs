using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Ocr.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Accounts.Services.Ocr;

public sealed class OcrDocumentStore : IOcrDocumentStore
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OcrDocumentStore> _logger;

    public OcrDocumentStore(ApplicationDbContext db, IWebHostEnvironment env, ILogger<OcrDocumentStore> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    private string GetPhotosFolder()
    {
        var root = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var photos = Path.Combine(root, "Photos");
        if (!Directory.Exists(photos))
        {
            Directory.CreateDirectory(photos);
        }
        return photos;
    }

    private static string GetFillSource(string? source) => source switch
    {
        "AVAILITY Docs" => "AVAILITY Docs",
        "CCA Docs" => "CCADocs",
        "Lassale Docs" => "Lassale Docs",
        "Zelis Docs" => "Zelis Docs",
        "HEALTH NET Docs" => "HEALTH NET Docs",
        _ => source ?? "UNKNOWN"
    };

    public async Task<int> SaveNewDocumentAsync(
        byte[] pdfBytes,
        string originalFileName,
        ParsedEobHeader parsed,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        var photosFolder = GetPhotosFolder();
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var ext = Path.GetExtension(originalFileName);
        var fileName = $"{baseName}_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(photosFolder, fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes, ct);

        parsed.Attachment = fileName;

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var upload = new OcrUploadedRecord
            {
                OrgId = orgId,
                Eob_Date = parsed.StatementDate,
                Attachment = fileName,
                Check_EFT = parsed.CheckEFT,
                Tot_PaidAmt = parsed.DocumentTotals?.ProviderPaid ?? parsed.DocumentTotals?.CheckAmount,
                StatusId = parsed.DocumentTotals != null ? 1064 : 1065,
                Source = parsed.Source,
                CreatedDate = DateTime.UtcNow,
                CreatedByUserId = userId
            };
            _db.OcrUploadedRecords.Add(upload);
            await _db.SaveChangesAsync(ct);

            var header = new OcrDocHeader
            {
                EobAttId = upload.Id,
                OrgId = orgId,
                Address = parsed.Address,
                Phone = parsed.Phone,
                Fax = parsed.Fax,
                DocTitle = parsed.DocTitle,
                InsuranceName = parsed.InsuranceName,
                InsuranceAddress = parsed.InsuranceAddress,
                ProviderName = parsed.ProviderName,
                ProviderAddress = parsed.ProviderAddress,
                ProviderId = parsed.ProviderId,
                NPI = parsed.NPI,
                Check_EFT = parsed.CheckEFT,
                StatementDate = parsed.StatementDate,
                TaxId = parsed.TaxId,
                PaymentWeek = parsed.PaymentWeek,
                Source = parsed.Source,
                CreatedDate = DateTime.UtcNow
            };
            _db.OcrDocHeaders.Add(header);
            await _db.SaveChangesAsync(ct);

            if (parsed.DocumentTotals != null)
            {
                var totals = new OcrTotalClaimInfo
                {
                    OcrHeaderId = header.Id, // Must be HeaderId
                    OrgId = orgId,
                    ClaimCount = parsed.DocumentTotals.ClaimCount,
                    Billed = parsed.DocumentTotals.Billed,
                    Allowed = parsed.DocumentTotals.Allowed,
                    Deductible = parsed.DocumentTotals.Deductible,
                    CoInsurance = parsed.DocumentTotals.CoInsurance,
                    RC = parsed.DocumentTotals.RC,
                    ProviderPaid = parsed.DocumentTotals.ProviderPaid,
                    Adjustment = parsed.DocumentTotals.Adjustment,
                    CheckAmount = parsed.DocumentTotals.CheckAmount,
                    ProviderDiscount = parsed.DocumentTotals.ProviderDiscount,
                    OtherPlanPayment = parsed.DocumentTotals.OtherPlanPayment,
                    CreatedDate = DateTime.UtcNow
                };
                _db.OcrTotalClaimInfos.Add(totals);
            }

            foreach (var p in parsed.Patients)
            {
                var patient = new OcrPatientDetail
                {
                    OcrHeaderId = header.Id,
                    OrgId = orgId,
                    PatientName = p.PatientName,
                    MID = p.MID,
                    PatientNo = p.PatientNo,
                    ICN = p.ICN,
                    Billed = p.Billed,
                    Allowed = p.Allowed,
                    Deductible = p.Deductible,
                    CoInsurance = p.CoInsurance,
                    RcAmount = p.RcAmount,
                    ProviderPaid = p.ProviderPaid,
                    PatientResponsibility = p.PatientResponsibility,
                    AdjustmentTotal = p.AdjustmentTotal,
                    Interest = p.Interest,
                    LateFiling = p.LateFiling,
                    Net = p.Net,
                    ProviderDiscount = p.ProviderDiscount,
                    OtherPlanPayment = p.OtherPlanPayment,
                    OtherAdjustments = p.OtherAdjustments,
                    NonCovered = p.NonCovered,
                    Source = parsed.Source,
                    CreatedDate = DateTime.UtcNow
                };
                _db.OcrPatientDetails.Add(patient);
                await _db.SaveChangesAsync(ct);

                foreach (var proc in p.Procedures)
                {
                    var procedure = new OcrProcedureDetail
                    {
                        OcrPatientId = patient.Id,
                        OrgId = orgId,
                        RenderingProviderId = proc.RenderingProviderId,
                        DayMonth = proc.DayMonth,
                        DateOfService = proc.DateOfService,
                        POS = proc.POS,
                        NOS = proc.NOS,
                        ProcedureCode = proc.ProcedureCode,
                        Modifiers = proc.Modifiers,
                        Billed = proc.Billed,
                        Allowed = proc.Allowed,
                        Deductible = proc.Deductible,
                        CoInsurance = proc.CoInsurance,
                        NOS_Val = proc.NOS_Val,
                        ProviderPaid = proc.ProviderPaid,
                        SubNOS = proc.SubNOS,
                        ProviderDiscount = proc.ProviderDiscount,
                        OtherPlan = proc.OtherPlan,
                        OtherAdjustments = proc.OtherAdjustments,
                        Copay = proc.Copay,
                        NonCovered = proc.NonCovered,
                        PatientResponsibility = proc.PatientResponsibility,
                        FillSource = GetFillSource(parsed.Source),
                        CreatedDate = DateTime.UtcNow
                    };
                    _db.OcrProcedureDetails.Add(procedure);
                    await _db.SaveChangesAsync(ct);

                    foreach (var grp in proc.GrpRows)
                    {
                        var g = new OcrGrpAmtDetail
                        {
                            PatientId = patient.Id,
                            ProcCodeId = procedure.Id,
                            GRPCode = grp.GRPCode,
                            Rc_Amt = grp.RcAmt,
                            REM = grp.Remark,
                            CreatedDate = DateTime.UtcNow
                        };
                        _db.OcrGrpAmtDetails.Add(g);
                    }

                    foreach (var rem in proc.Remarks)
                    {
                        var r = new OcrProRemDetail
                        {
                            OcrPatientId = patient.Id,
                            OcrProcCodeId = procedure.Id,
                            REM = rem,
                            CreatedDate = DateTime.UtcNow
                        };
                        _db.OcrProRemDetails.Add(r);
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return upload.Id;
        });
    }

    public async Task<int> UpsertDocumentAsync(
        byte[] pdfBytes,
        string originalFileName,
        ParsedEobHeader parsed,
        int existingHeaderId,
        int existingUploadId,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        var existingUpload = await _db.OcrUploadedRecords.FindAsync([existingUploadId], ct);
        if (existingUpload != null && string.IsNullOrEmpty(existingUpload.Attachment))
        {
            var photosFolder = GetPhotosFolder();
            var baseName = Path.GetFileNameWithoutExtension(originalFileName);
            var ext = Path.GetExtension(originalFileName);
            var fileName = $"{baseName}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(photosFolder, fileName);
            await File.WriteAllBytesAsync(filePath, pdfBytes, ct);

            existingUpload.Attachment = fileName;
            await _db.SaveChangesAsync(ct);
        }

        var header = await _db.OcrDocHeaders.FindAsync([existingHeaderId], ct);
        if (header != null)
        {
            header.StatementDate = parsed.StatementDate ?? header.StatementDate;
            header.ProviderName = parsed.ProviderName ?? header.ProviderName;
            header.InsuranceName = parsed.InsuranceName ?? header.InsuranceName;
            await _db.SaveChangesAsync(ct);
        }

        // Upsert patients
        foreach (var p in parsed.Patients)
        {
            var pat = await _db.OcrPatientDetails.FirstOrDefaultAsync(x =>
                x.OcrHeaderId == existingHeaderId &&
                x.PatientNo == p.PatientNo &&
                x.ICN == p.ICN, ct);

            if (pat == null)
            {
                pat = new OcrPatientDetail
                {
                    OcrHeaderId = existingHeaderId,
                    OrgId = orgId,
                    PatientName = p.PatientName,
                    MID = p.MID,
                    PatientNo = p.PatientNo,
                    ICN = p.ICN,
                    Billed = p.Billed,
                    Allowed = p.Allowed,
                    Deductible = p.Deductible,
                    CoInsurance = p.CoInsurance,
                    RcAmount = p.RcAmount,
                    ProviderPaid = p.ProviderPaid,
                    PatientResponsibility = p.PatientResponsibility,
                    Source = parsed.Source,
                    CreatedDate = DateTime.UtcNow
                };
                _db.OcrPatientDetails.Add(pat);
                await _db.SaveChangesAsync(ct);
            }

            foreach (var proc in p.Procedures)
            {
                var procedure = await _db.OcrProcedureDetails.FirstOrDefaultAsync(x =>
                    x.OcrPatientId == pat.Id &&
                    x.ProcedureCode == proc.ProcedureCode &&
                    x.DateOfService == proc.DateOfService, ct);

                if (procedure == null)
                {
                    procedure = new OcrProcedureDetail
                    {
                        OcrPatientId = pat.Id,
                        OrgId = orgId,
                        RenderingProviderId = proc.RenderingProviderId,
                        DayMonth = proc.DayMonth,
                        DateOfService = proc.DateOfService,
                        POS = proc.POS,
                        NOS = proc.NOS,
                        ProcedureCode = proc.ProcedureCode,
                        Modifiers = proc.Modifiers,
                        Billed = proc.Billed,
                        Allowed = proc.Allowed,
                        Deductible = proc.Deductible,
                        CoInsurance = proc.CoInsurance,
                        ProviderPaid = proc.ProviderPaid,
                        Copay = proc.Copay,
                        FillSource = GetFillSource(parsed.Source),
                        CreatedDate = DateTime.UtcNow
                    };
                    _db.OcrProcedureDetails.Add(procedure);
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        return existingUploadId;
    }

    public async Task<List<OcrDocumentListItem>> GetDocumentListAsync(
        string source,
        DateTime? dateFrom,
        DateTime? dateTo,
        int orgId,
        CancellationToken ct = default)
    {
        var query = _db.OcrDocumentViews.AsQueryable();

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(x => x.Source == source);
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(x => x.CreatedDate >= dateFrom.Value.Date);
        }

        if (dateTo.HasValue)
        {
            var endOfDay = dateTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.CreatedDate <= endOfDay);
        }

        return await query
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new OcrDocumentListItem
            {
                Id = x.Id,
                CheckEFT = x.Check_EFT,
                EobDate = x.Eob_Date,
                CreatedDate = x.CreatedDate,
                StatusId = x.StatusId,
                StatusName = x.StatusName,
                ColorCode = x.ColorCode,
                FontColor = x.FontColor,
                Attachment = x.Attachment,
                TotalPaid = x.Tot_PaidAmt,
                Source = x.Source
            })
            .ToListAsync(ct);
    }

    public async Task<OcrDocumentDetail?> GetDocumentDetailAsync(int id, CancellationToken ct = default)
    {
        var upload = await _db.OcrUploadedRecords.FindAsync([id], ct);
        if (upload == null) return null;

        var header = await _db.OcrDocHeaders.FirstOrDefaultAsync(x => x.EobAttId == upload.Id, ct);
        if (header == null) return null;

        var totals = await _db.OcrTotalClaimInfos.FirstOrDefaultAsync(x => x.OcrHeaderId == header.Id, ct);
        var patients = await _db.OcrPatientDetails.Where(x => x.OcrHeaderId == header.Id).ToListAsync(ct);

        var patientDtos = new List<OcrPatientDto>();
        foreach (var p in patients)
        {
            var procedures = await _db.OcrProcedureDetails.Where(x => x.OcrPatientId == p.Id).ToListAsync(ct);
            var procDtos = new List<OcrProcedureDto>();

            foreach (var pr in procedures)
            {
                var grps = await _db.OcrGrpAmtDetails.Where(x => x.ProcCodeId == pr.Id).ToListAsync(ct);
                var rems = await _db.OcrProRemDetails.Where(x => x.OcrProcCodeId == pr.Id).ToListAsync(ct);

                procDtos.Add(new OcrProcedureDto
                {
                    Id = pr.Id,
                    ProcedureCode = pr.ProcedureCode,
                    DateOfService = pr.DateOfService,
                    POS = pr.POS,
                    NOS = pr.NOS,
                    Modifiers = pr.Modifiers,
                    Billed = pr.Billed,
                    Allowed = pr.Allowed,
                    Deductible = pr.Deductible,
                    CoInsurance = pr.CoInsurance,
                    ProviderPaid = pr.ProviderPaid,
                    PatientResponsibility = pr.PatientResponsibility,
                    Copay = pr.Copay,
                    ProviderDiscount = pr.ProviderDiscount,
                    OtherPlan = pr.OtherPlan,
                    OtherAdjustments = pr.OtherAdjustments,
                    NonCovered = pr.NonCovered,
                    GrpRows = grps.Select(g => new OcrGrpDto
                    {
                        Id = g.Id,
                        GRPCode = g.GRPCode,
                        RcAmt = g.Rc_Amt,
                        Remark = g.REM
                    }).ToList(),
                    Remarks = rems.Select(r => r.REM ?? "").Where(r => !string.IsNullOrEmpty(r)).ToList()
                });
            }

            patientDtos.Add(new OcrPatientDto
            {
                Id = p.Id,
                PatientName = p.PatientName,
                MID = p.MID,
                PatientNo = p.PatientNo,
                ICN = p.ICN,
                Billed = p.Billed,
                Allowed = p.Allowed,
                Deductible = p.Deductible,
                CoInsurance = p.CoInsurance,
                RcAmount = p.RcAmount,
                ProviderPaid = p.ProviderPaid,
                PatientResponsibility = p.PatientResponsibility,
                ProviderDiscount = p.ProviderDiscount,
                OtherPlanPayment = p.OtherPlanPayment,
                NonCovered = p.NonCovered,
                Procedures = procDtos
            });
        }

        return new OcrDocumentDetail
        {
            Id = upload.Id,
            CheckEFT = upload.Check_EFT,
            EobDate = upload.Eob_Date,
            InsuranceName = header.InsuranceName,
            InsuranceAddress = header.InsuranceAddress,
            ProviderName = header.ProviderName,
            ProviderAddress = header.ProviderAddress,
            ProviderId = header.ProviderId,
            NPI = header.NPI,
            TaxId = header.TaxId,
            PaymentWeek = header.PaymentWeek,
            Attachment = upload.Attachment,
            Source = upload.Source,
            DocumentTotals = totals == null ? null : new OcrDocTotalsDto
            {
                ClaimCount = totals.ClaimCount,
                Billed = totals.Billed,
                Allowed = totals.Allowed,
                Deductible = totals.Deductible,
                CoInsurance = totals.CoInsurance,
                RC = totals.RC,
                ProviderPaid = totals.ProviderPaid,
                Adjustment = totals.Adjustment,
                CheckAmount = totals.CheckAmount,
                ProviderDiscount = totals.ProviderDiscount,
                OtherPlanPayment = totals.OtherPlanPayment
            },
            Patients = patientDtos
        };
    }

    public async Task UpdateProcedureCodesAsync(List<ProcedureCodeUpdateItem> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            var proc = await _db.OcrProcedureDetails.FindAsync([item.Id], ct);
            if (proc != null)
            {
                proc.ProcedureCode = item.ProcedureCode;
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteCcaByCheckNoAsync(string checkNo, int orgId, CancellationToken ct = default)
    {
        var headers = await _db.OcrDocHeaders
            .Where(x => x.Check_EFT == checkNo && x.Source == "CCA Docs")
            .ToListAsync(ct);

        foreach (var header in headers)
        {
            var uploadId = header.EobAttId;

            // 1. Delete totals
            var totals = await _db.OcrTotalClaimInfos.Where(x => x.OcrHeaderId == header.Id).ToListAsync(ct);
            _db.OcrTotalClaimInfos.RemoveRange(totals);

            // 2. Patients & procedures
            var patients = await _db.OcrPatientDetails.Where(x => x.OcrHeaderId == header.Id).ToListAsync(ct);
            foreach (var p in patients)
            {
                var procs = await _db.OcrProcedureDetails.Where(x => x.OcrPatientId == p.Id).ToListAsync(ct);
                foreach (var pr in procs)
                {
                    var grps = await _db.OcrGrpAmtDetails.Where(x => x.ProcCodeId == pr.Id).ToListAsync(ct);
                    _db.OcrGrpAmtDetails.RemoveRange(grps);

                    var rems = await _db.OcrProRemDetails.Where(x => x.OcrProcCodeId == pr.Id).ToListAsync(ct);
                    _db.OcrProRemDetails.RemoveRange(rems);
                }
                _db.OcrProcedureDetails.RemoveRange(procs);

                // Also clear any remaining by patient ID
                var extraGrps = await _db.OcrGrpAmtDetails.Where(x => x.PatientId == p.Id).ToListAsync(ct);
                _db.OcrGrpAmtDetails.RemoveRange(extraGrps);
                var extraRems = await _db.OcrProRemDetails.Where(x => x.OcrPatientId == p.Id).ToListAsync(ct);
                _db.OcrProRemDetails.RemoveRange(extraRems);
            }
            _db.OcrPatientDetails.RemoveRange(patients);

            // 3. Delete header
            _db.OcrDocHeaders.Remove(header);

            // 4. Delete upload record
            if (uploadId.HasValue)
            {
                var upload = await _db.OcrUploadedRecords.FindAsync([uploadId.Value], ct);
                if (upload != null)
                {
                    _db.OcrUploadedRecords.Remove(upload);
                }
            }

            // 5. Clear posted claim amounts in billing tables
            var claims = await _db.BillingClaims.Where(x => x.EobCheckNo == checkNo).ToListAsync(ct);
            foreach (var c in claims)
            {
                c.PatientResponsibility = null;
                c.EobCheckNo = null;
                c.TotalPaid = null;
            }

            var payments = await _db.BillingPaymentRows.Where(x => x.EobCheckNo == checkNo).ToListAsync(ct);
            _db.BillingPaymentRows.RemoveRange(payments);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<(bool Exists, int HeaderId, int UploadId)?> FindExistingCheckAsync(
        string checkEFT,
        string source,
        int orgId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(checkEFT)) return null;

        var header = await _db.OcrDocHeaders.FirstOrDefaultAsync(x =>
            x.Check_EFT == checkEFT &&
            x.Source == source, ct);

        if (header != null)
        {
            return (true, header.Id, header.EobAttId ?? 0);
        }

        return null;
    }

    public async Task SetStatusAsync(int uploadId, int statusId, CancellationToken ct = default)
    {
        var upload = await _db.OcrUploadedRecords.FindAsync([uploadId], ct);
        if (upload != null)
        {
            upload.StatusId = statusId;
            await _db.SaveChangesAsync(ct);
        }
    }
}
