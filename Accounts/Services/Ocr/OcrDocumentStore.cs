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

    private string GetStorageFolder(int orgId)
    {
        var folder = Path.Combine(_env.ContentRootPath, "App_Data", "OcrEob", orgId.ToString());
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return folder;
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
        var photosFolder = GetStorageFolder(orgId);
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var ext = Path.GetExtension(originalFileName);
        var fileName = $"{baseName}_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(photosFolder, fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes, ct);

        parsed.Attachment = fileName;

        try
        {
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
        catch
        {
            try { File.Delete(filePath); } catch (Exception cleanupError) { _logger.LogWarning(cleanupError, "Could not remove failed OCR upload file {FilePath}", filePath); }
            throw;
        }
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
        var photosFolder = GetStorageFolder(orgId);
        var safeBaseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var fileName = $"{safeBaseName}_{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(photosFolder, fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes, ct);

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var now = DateTime.UtcNow;
                var existingUpload = await _db.OcrUploadedRecords
                    .SingleOrDefaultAsync(x => x.Id == existingUploadId && x.OrgId == orgId, ct)
                    ?? throw new InvalidOperationException("The existing OCR upload was not found in the current tenant.");
                var header = await _db.OcrDocHeaders
                    .SingleOrDefaultAsync(x => x.Id == existingHeaderId && x.OrgId == orgId && x.EobAttId == existingUploadId, ct)
                    ?? throw new InvalidOperationException("The existing OCR header was not found in the current tenant.");

                parsed.Attachment = fileName;
                existingUpload.Attachment = fileName;
                existingUpload.Eob_Date = parsed.StatementDate;
                existingUpload.Check_EFT = parsed.CheckEFT;
                existingUpload.Tot_PaidAmt = parsed.DocumentTotals?.ProviderPaid ?? parsed.DocumentTotals?.CheckAmount;
                existingUpload.StatusId = parsed.DocumentTotals != null ? 1064 : 1065;
                existingUpload.Source = parsed.Source;
                existingUpload.CreatedByUserId = userId;

                header.Address = parsed.Address;
                header.Phone = parsed.Phone;
                header.Fax = parsed.Fax;
                header.DocTitle = parsed.DocTitle;
                header.InsuranceName = parsed.InsuranceName;
                header.InsuranceAddress = parsed.InsuranceAddress;
                header.ProviderName = parsed.ProviderName;
                header.ProviderAddress = parsed.ProviderAddress;
                header.ProviderId = parsed.ProviderId;
                header.NPI = parsed.NPI;
                header.Check_EFT = parsed.CheckEFT;
                header.StatementDate = parsed.StatementDate;
                header.TaxId = parsed.TaxId;
                header.PaymentWeek = parsed.PaymentWeek;
                header.Source = parsed.Source;

                var totals = await _db.OcrTotalClaimInfos
                    .SingleOrDefaultAsync(x => x.OcrHeaderId == existingHeaderId && x.OrgId == orgId, ct);
                if (parsed.DocumentTotals == null)
                {
                    if (totals != null) _db.OcrTotalClaimInfos.Remove(totals);
                }
                else
                {
                    totals ??= new OcrTotalClaimInfo
                    {
                        OcrHeaderId = existingHeaderId,
                        OrgId = orgId,
                        CreatedDate = now
                    };
                    if (totals.Id == 0) _db.OcrTotalClaimInfos.Add(totals);
                    totals.ClaimCount = parsed.DocumentTotals.ClaimCount;
                    totals.Billed = parsed.DocumentTotals.Billed;
                    totals.Allowed = parsed.DocumentTotals.Allowed;
                    totals.Deductible = parsed.DocumentTotals.Deductible;
                    totals.CoInsurance = parsed.DocumentTotals.CoInsurance;
                    totals.RC = parsed.DocumentTotals.RC;
                    totals.ProviderPaid = parsed.DocumentTotals.ProviderPaid;
                    totals.Adjustment = parsed.DocumentTotals.Adjustment;
                    totals.CheckAmount = parsed.DocumentTotals.CheckAmount;
                    totals.ProviderDiscount = parsed.DocumentTotals.ProviderDiscount;
                    totals.OtherPlanPayment = parsed.DocumentTotals.OtherPlanPayment;
                }

                // Health Net's legacy contract replaces the parsed child graph on
                // re-upload. This removes stale lines that are absent from the new PDF.
                if (string.Equals(parsed.Source, "HEALTH NET Docs", StringComparison.Ordinal))
                {
                    var oldPatients = await _db.OcrPatientDetails
                        .Where(x => x.OcrHeaderId == existingHeaderId && x.OrgId == orgId)
                        .ToListAsync(ct);
                    var oldPatientIds = oldPatients.Select(x => x.Id).ToList();
                    var oldProcedures = await _db.OcrProcedureDetails
                        .Where(x => x.OrgId == orgId && x.OcrPatientId.HasValue && oldPatientIds.Contains(x.OcrPatientId.Value))
                        .ToListAsync(ct);
                    var oldProcedureIds = oldProcedures.Select(x => x.Id).ToList();
                    _db.OcrGrpAmtDetails.RemoveRange(await _db.OcrGrpAmtDetails
                        .Where(x => (x.PatientId.HasValue && oldPatientIds.Contains(x.PatientId.Value)) ||
                                    (x.ProcCodeId.HasValue && oldProcedureIds.Contains(x.ProcCodeId.Value)))
                        .ToListAsync(ct));
                    _db.OcrProRemDetails.RemoveRange(await _db.OcrProRemDetails
                        .Where(x => (x.OcrPatientId.HasValue && oldPatientIds.Contains(x.OcrPatientId.Value)) ||
                                    (x.OcrProcCodeId.HasValue && oldProcedureIds.Contains(x.OcrProcCodeId.Value)))
                        .ToListAsync(ct));
                    _db.OcrProcedureDetails.RemoveRange(oldProcedures);
                    _db.OcrPatientDetails.RemoveRange(oldPatients);
                    await _db.SaveChangesAsync(ct);
                }

                foreach (var p in parsed.Patients)
                {
                    var patientQuery = _db.OcrPatientDetails.Where(x =>
                        x.OcrHeaderId == existingHeaderId && x.OrgId == orgId &&
                        x.PatientNo == p.PatientNo && x.ICN == p.ICN);
                    if (string.IsNullOrWhiteSpace(p.PatientNo) && string.IsNullOrWhiteSpace(p.ICN))
                        patientQuery = patientQuery.Where(x => x.PatientName == p.PatientName);

                    var patient = await patientQuery.FirstOrDefaultAsync(ct);
                    if (patient == null)
                    {
                        patient = new OcrPatientDetail
                        {
                            OcrHeaderId = existingHeaderId,
                            OrgId = orgId,
                            CreatedDate = now
                        };
                        _db.OcrPatientDetails.Add(patient);
                        await _db.SaveChangesAsync(ct);
                    }

                    patient.PatientName = p.PatientName;
                    patient.MID = p.MID;
                    patient.PatientNo = p.PatientNo;
                    patient.ICN = p.ICN;
                    patient.Billed = p.Billed;
                    patient.Allowed = p.Allowed;
                    patient.Deductible = p.Deductible;
                    patient.CoInsurance = p.CoInsurance;
                    patient.RcAmount = p.RcAmount;
                    patient.ProviderPaid = p.ProviderPaid;
                    patient.PatientResponsibility = p.PatientResponsibility;
                    patient.AdjustmentTotal = p.AdjustmentTotal;
                    patient.Interest = p.Interest;
                    patient.LateFiling = p.LateFiling;
                    patient.Net = p.Net;
                    patient.ProviderDiscount = p.ProviderDiscount;
                    patient.OtherPlanPayment = p.OtherPlanPayment;
                    patient.OtherAdjustments = p.OtherAdjustments;
                    patient.NonCovered = p.NonCovered;
                    patient.Source = parsed.Source;

                    foreach (var proc in p.Procedures)
                    {
                        var procedure = await _db.OcrProcedureDetails.FirstOrDefaultAsync(x =>
                            x.OcrPatientId == patient.Id && x.OrgId == orgId &&
                            x.ProcedureCode == proc.ProcedureCode && x.DateOfService == proc.DateOfService, ct);
                        if (procedure == null)
                        {
                            procedure = new OcrProcedureDetail
                            {
                                OcrPatientId = patient.Id,
                                OrgId = orgId,
                                CreatedDate = now
                            };
                            _db.OcrProcedureDetails.Add(procedure);
                            await _db.SaveChangesAsync(ct);
                        }

                        procedure.RenderingProviderId = proc.RenderingProviderId;
                        procedure.DayMonth = proc.DayMonth;
                        procedure.DateOfService = proc.DateOfService;
                        procedure.POS = proc.POS;
                        procedure.NOS = proc.NOS;
                        procedure.ProcedureCode = proc.ProcedureCode;
                        procedure.Modifiers = proc.Modifiers;
                        procedure.Billed = proc.Billed;
                        procedure.Allowed = proc.Allowed;
                        procedure.Deductible = proc.Deductible;
                        procedure.CoInsurance = proc.CoInsurance;
                        procedure.NOS_Val = proc.NOS_Val;
                        procedure.ProviderPaid = proc.ProviderPaid;
                        procedure.SubNOS = proc.SubNOS;
                        procedure.ProviderDiscount = proc.ProviderDiscount;
                        procedure.OtherPlan = proc.OtherPlan;
                        procedure.OtherAdjustments = proc.OtherAdjustments;
                        procedure.Copay = proc.Copay;
                        procedure.NonCovered = proc.NonCovered;
                        procedure.PatientResponsibility = proc.PatientResponsibility;
                        procedure.FillSource = GetFillSource(parsed.Source);

                        foreach (var grp in proc.GrpRows)
                        {
                            var exists = await _db.OcrGrpAmtDetails.AnyAsync(x =>
                                x.PatientId == patient.Id && x.ProcCodeId == procedure.Id &&
                                x.GRPCode == grp.GRPCode && x.Rc_Amt == grp.RcAmt && x.REM == grp.Remark, ct);
                            if (!exists)
                            {
                                _db.OcrGrpAmtDetails.Add(new OcrGrpAmtDetail
                                {
                                    PatientId = patient.Id,
                                    ProcCodeId = procedure.Id,
                                    GRPCode = grp.GRPCode,
                                    Rc_Amt = grp.RcAmt,
                                    REM = grp.Remark,
                                    CreatedDate = now
                                });
                            }
                        }

                        foreach (var remark in proc.Remarks.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                        {
                            var exists = await _db.OcrProRemDetails.AnyAsync(x =>
                                x.OcrPatientId == patient.Id && x.OcrProcCodeId == procedure.Id && x.REM == remark, ct);
                            if (!exists)
                            {
                                _db.OcrProRemDetails.Add(new OcrProRemDetail
                                {
                                    OcrPatientId = patient.Id,
                                    OcrProcCodeId = procedure.Id,
                                    REM = remark,
                                    CreatedDate = now
                                });
                            }
                        }
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return existingUploadId;
            });
        }
        catch
        {
            try { File.Delete(filePath); } catch (Exception cleanupError) { _logger.LogWarning(cleanupError, "Could not remove failed OCR upload file {FilePath}", filePath); }
            throw;
        }
    }

    public async Task<List<OcrDocumentListItem>> GetDocumentListAsync(
        string source,
        DateTime? dateFrom,
        DateTime? dateTo,
        int orgId,
        CancellationToken ct = default)
    {
        var query = _db.OcrDocumentViews.Where(x => x.OrgId == orgId);

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

    public async Task<OcrDocumentDetail?> GetDocumentDetailAsync(int id, int orgId, CancellationToken ct = default)
    {
        var upload = await _db.OcrUploadedRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OrgId == orgId, ct);
        if (upload == null) return null;

        var header = await _db.OcrDocHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EobAttId == upload.Id && x.OrgId == orgId, ct);
        if (header == null) return null;

        var providerAddress = header.ProviderAddress;
        if (string.Equals(upload.Source, "HEALTH NET Docs", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(header.ProviderId) &&
            Accounts.Services.Ocr.HealthNet.HealthNetNoisePolicy.IsStructuredNoise(providerAddress))
        {
            // Older parser versions could mistake the fax confidentiality footer for
            // the provider address. A provider identifier is stable across remittances,
            // so recover only from another clean header for the same tenant/provider.
            var addressCandidates = await _db.OcrDocHeaders.AsNoTracking()
                .Where(x => x.OrgId == orgId && x.Id != header.Id &&
                    x.ProviderId == header.ProviderId && x.ProviderAddress != null)
                .OrderByDescending(x => x.Id)
                .Select(x => x.ProviderAddress)
                .ToListAsync(ct);
            providerAddress = addressCandidates.FirstOrDefault(value =>
                !Accounts.Services.Ocr.HealthNet.HealthNetNoisePolicy.IsStructuredNoise(value));
        }

        var totals = await _db.OcrTotalClaimInfos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OcrHeaderId == header.Id && x.OrgId == orgId, ct);
        var patients = await _db.OcrPatientDetails.AsNoTracking()
            .Where(x => x.OcrHeaderId == header.Id && x.OrgId == orgId).ToListAsync(ct);

        var patientDtos = new List<OcrPatientDto>();
        foreach (var p in patients)
        {
            var procedures = await _db.OcrProcedureDetails.AsNoTracking()
                .Where(x => x.OcrPatientId == p.Id && x.OrgId == orgId).ToListAsync(ct);
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
            DocTitle = header.DocTitle,
            Address = header.Address,
            Phone = header.Phone,
            Fax = header.Fax,
            CheckEFT = upload.Check_EFT,
            EobDate = upload.Eob_Date ?? header.StatementDate,
            StatementDate = header.StatementDate ?? upload.Eob_Date,
            InsuranceName = header.InsuranceName,
            InsuranceAddress = header.InsuranceAddress,
            ProviderName = header.ProviderName,
            ProviderAddress = providerAddress,
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

    public async Task<(string Path, string FileName)?> GetAttachmentAsync(
        int id,
        int orgId,
        CancellationToken ct = default)
    {
        var attachment = await _db.OcrUploadedRecords.AsNoTracking()
            .Where(x => x.Id == id && x.OrgId == orgId)
            .Select(x => x.Attachment)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(attachment)) return null;

        var safeName = Path.GetFileName(attachment);
        if (!string.Equals(safeName, attachment, StringComparison.Ordinal)) return null;

        var privatePath = Path.Combine(GetStorageFolder(orgId), safeName);
        if (File.Exists(privatePath)) return (privatePath, safeName);

        // Backward-compatible read for files created before private OCR storage.
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var legacyPath = Path.Combine(webRoot, "Photos", safeName);
        return File.Exists(legacyPath) ? (legacyPath, safeName) : null;
    }

    public async Task UpdateProcedureCodesAsync(List<ProcedureCodeUpdateItem> items, int orgId, CancellationToken ct = default)
    {
        var requestedIds = items.Select(x => x.Id).Distinct().ToList();
        var procedures = await _db.OcrProcedureDetails
            .Where(x => requestedIds.Contains(x.Id) && x.OrgId == orgId)
            .ToDictionaryAsync(x => x.Id, ct);

        if (procedures.Count != requestedIds.Count)
            throw new KeyNotFoundException("One or more OCR procedure rows were not found in the current tenant.");

        foreach (var item in items)
        {
            var code = item.ProcedureCode?.Trim();
            if (code?.Length > 50)
                throw new InvalidOperationException("Procedure code cannot exceed 50 characters.");
            procedures[item.Id].ProcedureCode = string.IsNullOrWhiteSpace(code) ? null : code;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteCcaByCheckNoAsync(string checkNo, int orgId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var headers = await _db.OcrDocHeaders
            .Where(x => x.Check_EFT == checkNo && x.Source == "CCA Docs" && x.OrgId == orgId)
            .OrderByDescending(x => x.EobAttId)
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
                var upload = await _db.OcrUploadedRecords
                    .SingleOrDefaultAsync(x => x.Id == uploadId.Value && x.OrgId == orgId, ct);
                if (upload != null)
                {
                    _db.OcrUploadedRecords.Remove(upload);
                }
            }

            // 5. Clear posted claim amounts in billing tables
            var claims = await _db.BillingClaims
                .Where(x => x.EobCheckNo == checkNo && x.OrgId == orgId).ToListAsync(ct);
            var claimIds = claims.Select(x => x.ClaimId).ToList();
            foreach (var c in claims)
            {
                c.PatientResponsibility = null;
                c.EobCheckNo = null;
                c.Attachment = null;
                c.TotalPaid = null;
            }

            var claimDocuments = await _db.BillingClaimEobDocs
                .Where(x => x.EobCheckNo == checkNo && x.ClaimId.HasValue && claimIds.Contains(x.ClaimId.Value))
                .ToListAsync(ct);
            _db.BillingClaimEobDocs.RemoveRange(claimDocuments);

            var appointmentCodes = await _db.BillingApptCodes
                .Where(x => x.OrgId == orgId && x.ClaimId.HasValue && claimIds.Contains(x.ClaimId.Value))
                .ToListAsync(ct);
            foreach (var code in appointmentCodes)
            {
                code.Paid = null;
                code.LineEobStatusId = null;
                code.EobAllowed = null;
                code.CoInsurance = null;
                code.Deductible = null;
                code.FillSource = null;
                code.IsUpload = null;
            }

            var payments = await _db.BillingPaymentRows
                .Where(x => x.EobCheckNo == checkNo && x.OrgId == orgId).ToListAsync(ct);
            _db.BillingPaymentRows.RemoveRange(payments);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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
            x.Source == source &&
            x.OrgId == orgId, ct);

        if (header != null)
        {
            return (true, header.Id, header.EobAttId ?? 0);
        }

        return null;
    }

    public async Task SetStatusAsync(int uploadId, int statusId, int orgId, CancellationToken ct = default)
    {
        var upload = await _db.OcrUploadedRecords
            .SingleOrDefaultAsync(x => x.Id == uploadId && x.OrgId == orgId, ct);
        if (upload != null)
        {
            upload.StatusId = statusId;
            await _db.SaveChangesAsync(ct);
        }
    }
}
