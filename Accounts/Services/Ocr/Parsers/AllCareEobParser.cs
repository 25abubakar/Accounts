using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

/// <summary>
/// AllCare remittances are issued through Availity and use the same anchored
/// remittance layout. One coordinate parser prevents the implementations from
/// drifting when optional columns or claim lengths vary between documents.
/// </summary>
public sealed class AllCareEobParser : IAllCareEobParser
{
    private readonly IPdfTextExtractor _extractor;

    public AllCareEobParser(IPdfTextExtractor extractor) => _extractor = extractor;

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var result = new AvailityEobParser(_extractor).Parse(pdfBytes);
        result.Source = "All Care";
        result.DocTitle = "ALLCARE REMITTANCE NOTICE";
        return result;
    }
}
