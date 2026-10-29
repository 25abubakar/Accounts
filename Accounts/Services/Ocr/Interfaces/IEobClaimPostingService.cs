using Accounts.DTOs;

namespace Accounts.Services.Ocr.Interfaces;

/// <summary>
/// Shared claim posting used by Availity, CCA, LaSalle, and Zelis.
/// Runs once per procedure line when the OCR patient has both name and account number.
/// </summary>
public interface IEobClaimPostingService
{
    /// <summary>
    /// Post all patients/procedures from the parsed header.
    /// Returns (postedCount, skippedCount, warnings).
    /// </summary>
    Task<PostingResult> PostAsync(
        ParsedEobHeader parsed,
        int savedUploadId,
        string payer,   // "AVAILITY"|"CCA"|"LASALLE"|"ZELIS"
        int orgId,
        string? userId,
        CancellationToken ct = default);

    /// <summary>
    /// Fire-and-forget export to external billing system (SendToBilling).
    /// Availity does not await this; others may await.
    /// </summary>
    Task SendToBillingAsync(
        string patientNo,
        string? dos,
        string? procedureCode,
        string payer,
        int orgId,
        CancellationToken ct = default);

    /// <summary>Availity-only amount notification (AmountSendToBilling).</summary>
    Task AmountSendToBillingAsync(
        string patientNo,
        string? policyNo,
        string? dos,
        IEnumerable<(string code, decimal paid)> codes,
        CancellationToken ct = default);
}

public sealed class PostingResult
{
    public int PostedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Warnings { get; set; } = [];
}

