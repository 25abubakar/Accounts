using Accounts.DTOs;

namespace Accounts.Services.Ocr.Interfaces;

public interface IAvailityEobParser
{
    /// <summary>
    /// Parse an Availity EOB PDF. Returns the parsed header (with patients/procedures embedded).
    /// Throws ParseException only on catastrophic failure; sets Warnings for recoverable issues.
    /// Status 1064 when DocumentTotals != null, else 1065.
    /// </summary>
    ParsedEobHeader Parse(byte[] pdfBytes);
}

public interface ICcaEobParser
{
    ParsedEobHeader Parse(byte[] pdfBytes);
}

public interface ILaSalleEobParser
{
    ParsedEobHeader Parse(byte[] pdfBytes);
}

public interface IAllCareEobParser
{
    /// <summary>Parse only — returns the result but does NOT interact with the database.</summary>
    ParsedEobHeader Parse(byte[] pdfBytes);
}

public interface IZelisFormat1EobParser
{
    ParsedEobHeader Parse(byte[] pdfBytes);
}

public interface IHealthNetDocumentParser
{
    ParsedEobHeader Parse(byte[] pdfBytes);
}

