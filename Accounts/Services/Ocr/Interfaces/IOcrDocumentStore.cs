using Accounts.DTOs;

namespace Accounts.Services.Ocr.Interfaces;

/// <summary>
/// Saves/upserts OCR rows and reads them back for the grid and detail modal.
/// </summary>
public interface IOcrDocumentStore
{
    /// <summary>Save PDF file, insert upload record, header, patients, procedures, GRP, remarks, totals.</summary>
    Task<int> SaveNewDocumentAsync(
        byte[] pdfBytes,
        string originalFileName,
        ParsedEobHeader parsed,
        int orgId,
        string? userId,
        CancellationToken ct = default);

    /// <summary>
    /// Re-upload: reuse the existing header; upsert patients by header+account+ICN,
    /// procedures by patient+code+DOS, GRP/remarks by natural keys.
    /// Returns updated upload-record id.
    /// </summary>
    Task<int> UpsertDocumentAsync(
        byte[] pdfBytes,
        string originalFileName,
        ParsedEobHeader parsed,
        int existingHeaderId,
        int existingUploadId,
        int orgId,
        string? userId,
        CancellationToken ct = default);

    /// <summary>Grid list filtered by source and date range.</summary>
    Task<List<OcrDocumentListItem>> GetDocumentListAsync(
        string source,
        DateTime? dateFrom,
        DateTime? dateTo,
        int orgId,
        CancellationToken ct = default);

    /// <summary>Full detail: header + patients + procedures + GRP + remarks.</summary>
    Task<OcrDocumentDetail?> GetDocumentDetailAsync(int id, CancellationToken ct = default);

    /// <summary>Update ProcedureCode column only on the listed procedure rows.</summary>
    Task UpdateProcedureCodesAsync(List<ProcedureCodeUpdateItem> items, CancellationToken ct = default);

    /// <summary>
    /// CCA delete: cascade-remove OCR rows, clear claim EOB fields, remove payment rows.
    /// Does NOT delete the PDF file, the receipt, or patients/appointments.
    /// </summary>
    Task DeleteCcaByCheckNoAsync(string checkNo, int orgId, CancellationToken ct = default);

    /// <summary>Check whether a check number already exists in tblOCRDocHeaderInfo for this org.</summary>
    Task<(bool Exists, int HeaderId, int UploadId)?> FindExistingCheckAsync(
        string checkEFT,
        string source,
        int orgId,
        CancellationToken ct = default);

    /// <summary>Set the status id on an upload record.</summary>
    Task SetStatusAsync(int uploadId, int statusId, CancellationToken ct = default);
}

