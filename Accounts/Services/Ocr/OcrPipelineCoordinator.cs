using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;
using Microsoft.Extensions.Logging;

namespace Accounts.Services.Ocr;

public sealed class OcrPipelineCoordinator
{
    private readonly IAvailityEobParser _availityParser;
    private readonly ICcaEobParser _ccaParser;
    private readonly ILaSalleEobParser _laSalleParser;
    private readonly IAllCareEobParser _allCareParser;
    private readonly IZelisFormat1EobParser _zelisParser;
    private readonly IHealthNetDocumentParser _healthNetParser;
    private readonly IOcrDocumentStore _store;
    private readonly IEobReceiptService _receipt;
    private readonly IEobClaimPostingService _posting;
    private readonly HealthNetClaimPostingService _healthNetPosting;
    private readonly ILogger<OcrPipelineCoordinator> _logger;

    public OcrPipelineCoordinator(
        IAvailityEobParser availityParser,
        ICcaEobParser ccaParser,
        ILaSalleEobParser laSalleParser,
        IAllCareEobParser allCareParser,
        IZelisFormat1EobParser zelisParser,
        IHealthNetDocumentParser healthNetParser,
        IOcrDocumentStore store,
        IEobReceiptService receipt,
        IEobClaimPostingService posting,
        HealthNetClaimPostingService healthNetPosting,
        ILogger<OcrPipelineCoordinator> logger)
    {
        _availityParser = availityParser;
        _ccaParser = ccaParser;
        _laSalleParser = laSalleParser;
        _allCareParser = allCareParser;
        _zelisParser = zelisParser;
        _healthNetParser = healthNetParser;
        _store = store;
        _receipt = receipt;
        _posting = posting;
        _healthNetPosting = healthNetPosting;
        _logger = logger;
    }

    public async Task<OcrUploadResult> ProcessAvailityAsync(
        byte[] pdfBytes,
        string fileName,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        try
        {
            var parsed = _availityParser.Parse(pdfBytes);
            EnsureCheckIdentity(parsed, fileName);
            var checkNo = parsed.CheckEFT ?? "";
            var existing = await _store.FindExistingCheckAsync(checkNo, "AVAILITY Docs", orgId, ct);

            int uploadId;
            bool isAlreadyExist = false;

            if (existing != null && existing.Value.Exists)
            {
                isAlreadyExist = true;
                uploadId = await _store.UpsertDocumentAsync(pdfBytes, fileName, parsed, existing.Value.HeaderId, existing.Value.UploadId, orgId, userId, ct);
            }
            else
            {
                uploadId = await _store.SaveNewDocumentAsync(pdfBytes, fileName, parsed, orgId, userId, ct);
                var amount = parsed.DocumentTotals?.ProviderPaid ?? parsed.DocumentTotals?.CheckAmount ?? 0;
                if (amount > 0 && !string.IsNullOrWhiteSpace(checkNo))
                {
                    await _receipt.WriteReceiptAsync(
                        checkNo,
                        parsed.InsuranceName ?? "Availity",
                        amount,
                        parsed.StatementDateParsed ?? DateTime.UtcNow,
                        parsed.Attachment ?? fileName,
                        orgId,
                        userId,
                        ct);
                }
            }

            var postResult = await _posting.PostAsync(parsed, uploadId, "AVAILITY", orgId, userId, ct);

            return new OcrUploadResult
            {
                Success = true,
                IsAlreadyExist = isAlreadyExist,
                SavedDocumentId = uploadId,
                CheckEFT = checkNo,
                FileName = fileName,
                PostedClaimCount = postResult.PostedCount,
                SkippedClaimCount = postResult.SkippedCount,
                Warnings = postResult.Warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Availity PDF {FileName}", fileName);
            return new OcrUploadResult
            {
                Success = false,
                FileName = fileName,
                Error = ex.Message
            };
        }
    }

    public async Task<OcrUploadResult> ProcessCcaAsync(
        byte[] pdfBytes,
        string fileName,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        try
        {
            var parsed = _ccaParser.Parse(pdfBytes);
            EnsureCheckIdentity(parsed, fileName);
            var checkNo = parsed.CheckEFT ?? "";
            var existing = await _store.FindExistingCheckAsync(checkNo, "CCA Docs", orgId, ct);

            // Rule: CCA refuses duplicate check
            if (existing != null && existing.Value.Exists)
            {
                return new OcrUploadResult
                {
                    Success = false,
                    IsAlreadyExist = true,
                    FileName = fileName,
                    CheckEFT = checkNo,
                    Error = $"Check {checkNo} already exists in CCA records. Duplicate not allowed."
                };
            }

            var uploadId = await _store.SaveNewDocumentAsync(pdfBytes, fileName, parsed, orgId, userId, ct);
            var amount = parsed.DocumentTotals?.ProviderPaid ?? parsed.DocumentTotals?.CheckAmount ?? 0;
            if (amount > 0 && !string.IsNullOrWhiteSpace(checkNo))
            {
                await _receipt.WriteReceiptAsync(
                    checkNo,
                    parsed.InsuranceName ?? "CCA",
                    amount,
                    parsed.StatementDateParsed ?? DateTime.UtcNow,
                    parsed.Attachment ?? fileName,
                    orgId,
                    userId,
                    ct);
            }

            var postResult = await _posting.PostAsync(parsed, uploadId, "CCA", orgId, userId, ct);

            return new OcrUploadResult
            {
                Success = true,
                IsAlreadyExist = false,
                SavedDocumentId = uploadId,
                CheckEFT = checkNo,
                FileName = fileName,
                PostedClaimCount = postResult.PostedCount,
                SkippedClaimCount = postResult.SkippedCount,
                Warnings = postResult.Warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process CCA PDF {FileName}", fileName);
            return new OcrUploadResult
            {
                Success = false,
                FileName = fileName,
                Error = ex.Message
            };
        }
    }

    public async Task<OcrUploadResult> ProcessLaSalleAsync(
        byte[] pdfBytes,
        string fileName,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        try
        {
            var parsed = _laSalleParser.Parse(pdfBytes);
            EnsureCheckIdentity(parsed, fileName);
            var checkNo = parsed.CheckEFT ?? "";
            var existing = await _store.FindExistingCheckAsync(checkNo, "Lassale Docs", orgId, ct);

            int uploadId;
            bool isAlreadyExist = false;

            if (existing != null && existing.Value.Exists)
            {
                isAlreadyExist = true;
                uploadId = await _store.UpsertDocumentAsync(pdfBytes, fileName, parsed, existing.Value.HeaderId, existing.Value.UploadId, orgId, userId, ct);
            }
            else
            {
                uploadId = await _store.SaveNewDocumentAsync(pdfBytes, fileName, parsed, orgId, userId, ct);
                var amount = parsed.DocumentTotals?.ProviderPaid ?? parsed.DocumentTotals?.CheckAmount ?? 0;
                if (amount > 0 && !string.IsNullOrWhiteSpace(checkNo))
                {
                    await _receipt.WriteReceiptAsync(
                        checkNo,
                        parsed.InsuranceName ?? "LaSalle",
                        amount,
                        parsed.StatementDateParsed ?? DateTime.UtcNow,
                        parsed.Attachment ?? fileName,
                        orgId,
                        userId,
                        ct);
                }
            }

            var postResult = await _posting.PostAsync(parsed, uploadId, "LASALLE", orgId, userId, ct);

            return new OcrUploadResult
            {
                Success = true,
                IsAlreadyExist = isAlreadyExist,
                SavedDocumentId = uploadId,
                CheckEFT = checkNo,
                FileName = fileName,
                PostedClaimCount = postResult.PostedCount,
                SkippedClaimCount = postResult.SkippedCount,
                Warnings = postResult.Warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process LaSalle PDF {FileName}", fileName);
            return new OcrUploadResult
            {
                Success = false,
                FileName = fileName,
                Error = ex.Message
            };
        }
    }

    public Task<AllCareParseResult> ProcessAllCareAsync(
        byte[] pdfBytes,
        string fileName,
        CancellationToken ct = default)
    {
        try
        {
            // Rule: All Care only reads the PDF and returns parse. Zero DB writes!
            var parsed = _allCareParser.Parse(pdfBytes);
            return Task.FromResult(new AllCareParseResult
            {
                Success = true,
                FileName = fileName,
                ParsedData = parsed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse All Care PDF {FileName}", fileName);
            return Task.FromResult(new AllCareParseResult
            {
                Success = false,
                FileName = fileName,
                Error = ex.Message
            });
        }
    }

    public async Task<OcrUploadResult> ProcessZelisAsync(
        byte[] pdfBytes,
        string fileName,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        try
        {
            var parsed = _zelisParser.Parse(pdfBytes);
            EnsureCheckIdentity(parsed, fileName);
            var checkNo = parsed.CheckEFT ?? "";
            var existing = await _store.FindExistingCheckAsync(checkNo, "Zelis Docs", orgId, ct);

            int uploadId;
            bool isAlreadyExist = false;

            if (existing != null && existing.Value.Exists)
            {
                isAlreadyExist = true;
                uploadId = await _store.UpsertDocumentAsync(pdfBytes, fileName, parsed, existing.Value.HeaderId, existing.Value.UploadId, orgId, userId, ct);
            }
            else
            {
                uploadId = await _store.SaveNewDocumentAsync(pdfBytes, fileName, parsed, orgId, userId, ct);
                var amount = parsed.DocumentTotals?.ProviderPaid ?? parsed.DocumentTotals?.CheckAmount ?? 0;
                if (amount > 0 && !string.IsNullOrWhiteSpace(checkNo))
                {
                    await _receipt.WriteReceiptAsync(
                        checkNo,
                        parsed.InsuranceName ?? "Zelis",
                        amount,
                        parsed.StatementDateParsed ?? DateTime.UtcNow,
                        parsed.Attachment ?? fileName,
                        orgId,
                        userId,
                        ct);
                }
            }

            var postResult = await _posting.PostAsync(parsed, uploadId, "ZELIS", orgId, userId, ct);

            return new OcrUploadResult
            {
                Success = true,
                IsAlreadyExist = isAlreadyExist,
                SavedDocumentId = uploadId,
                CheckEFT = checkNo,
                FileName = fileName,
                PostedClaimCount = postResult.PostedCount,
                SkippedClaimCount = postResult.SkippedCount,
                Warnings = postResult.Warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Zelis PDF {FileName}", fileName);
            return new OcrUploadResult
            {
                Success = false,
                FileName = fileName,
                Error = ex.Message
            };
        }
    }

    public async Task<HealthNetFileResult> ProcessHealthNetAsync(
        byte[] pdfBytes,
        string fileName,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        try
        {
            var parsed = _healthNetParser.Parse(pdfBytes);
            EnsureCheckIdentity(parsed, fileName);
            if (!parsed.Patients.SelectMany(x => x.Procedures).Any())
                throw new InvalidDataException("Health Net PDF requires review; no data was saved or posted.");
            var checkNo = parsed.CheckEFT ?? "";

            // Health Net re-uploads reuse the tenant/source/check record rather than
            // duplicating the OCR header and financial posting identity.
            var existing = await _store.FindExistingCheckAsync(checkNo, "HEALTH NET Docs", orgId, ct);
            var uploadId = existing.HasValue && existing.Value.Exists
                ? await _store.UpsertDocumentAsync(
                    pdfBytes,
                    fileName,
                    parsed,
                    existing.Value.HeaderId,
                    existing.Value.UploadId,
                    orgId,
                    userId,
                    ct)
                : await _store.SaveNewDocumentAsync(pdfBytes, fileName, parsed, orgId, userId, ct);

            // Receipt
            var amount = parsed.DocumentTotals?.ProviderPaid ?? parsed.DocumentTotals?.CheckAmount ?? 0;
            if (amount > 0 && !string.IsNullOrWhiteSpace(checkNo))
            {
                await _receipt.WriteHealthNetReceiptAsync(
                    checkNo,
                    parsed.InsuranceName,
                    amount,
                    parsed.StatementDateParsed ?? DateTime.UtcNow,
                    parsed.Attachment ?? fileName,
                    orgId,
                    userId,
                    ct);
            }

            // 2. Transaction 2: Health Net Claim Posting
            // If posting fails, OCR save is preserved!
            try
            {
                var postResult = await _healthNetPosting.PostAsync(parsed, uploadId, orgId, userId, ct);
                postResult.FileName = fileName;
                postResult.SavedDocumentId = uploadId;
                return postResult;
            }
            catch (Exception postEx)
            {
                _logger.LogError(postEx, "Health Net posting failed for {FileName}, but OCR was saved as Id {UploadId}", fileName, uploadId);
                return new HealthNetFileResult
                {
                    FileName = fileName,
                    SavedDocumentId = uploadId,
                    Status = "Failed",
                    Error = $"Claim posting failed: {postEx.Message}. Document saved as ID {uploadId}.",
                    Warnings = [$"Document was successfully uploaded (ID {uploadId}), but claims posting encountered an error: {postEx.Message}"]
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Health Net PDF {FileName}", fileName);
            return new HealthNetFileResult
            {
                FileName = fileName,
                Status = "Failed",
                Error = ex.Message
            };
        }
    }

    private static void EnsureCheckIdentity(ParsedEobHeader parsed, string fileName)
    {
        if (string.IsNullOrWhiteSpace(parsed.CheckEFT))
            throw new InvalidDataException($"The payer check/EFT number could not be read from '{Path.GetFileName(fileName)}'. No data was saved.");
    }
}

