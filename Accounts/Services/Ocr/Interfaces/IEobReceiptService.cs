using Accounts.DTOs;

namespace Accounts.Services.Ocr.Interfaces;

/// <summary>
/// Creates or updates the billing receipt (tblRoznamcha row) after a new check is saved.
/// </summary>
public interface IEobReceiptService
{
    /// <summary>
    /// Standard receipt for Availity, CCA, LaSalle, Zelis.
    /// Looks up tblRoznamcha by InstrumentNo = checkNumber.
    ///   If exists → sets EobCheckNo only.
    ///   If missing → resolves/creates the insurance account then inserts the receipt row and optional document-process row.
    /// Called only when checkAmount > 0.
    /// </summary>
    Task WriteReceiptAsync(
        string checkNumber,
        string insuranceName,
        decimal checkAmount,
        DateTime statementDate,
        string attachmentFileName,
        int orgId,
        string? userId,
        CancellationToken ct = default);

    /// <summary>
    /// Health Net receipt.
    /// Idempotency key = org + R_TypeId=2 + instrument/EOB check.
    /// Updates an existing receipt rather than inserting a second one.
    /// Falls back to "Health Net" when insuranceName is blank.
    /// Called only when checkAmount > 0 and checkNumber is not blank.
    /// </summary>
    Task WriteHealthNetReceiptAsync(
        string checkNumber,
        string? insuranceName,
        decimal checkAmount,
        DateTime statementDate,
        string attachmentFileName,
        int orgId,
        string? userId,
        CancellationToken ct = default);
}

