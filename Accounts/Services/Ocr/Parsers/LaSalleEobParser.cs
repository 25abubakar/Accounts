using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

public sealed class LaSalleEobParser : ILaSalleEobParser
{
    private readonly IPdfTextExtractor _extractor;

    public LaSalleEobParser(IPdfTextExtractor extractor)
    {
        _extractor = extractor;
    }

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes, yTolerance: 3.0, xGap: 0.75);
        var header = new ParsedEobHeader
        {
            Source = "Lassale Docs"
        };

        if (pages.Count == 0) return header;

        var firstPage = pages[0];
        ParseHeader(firstPage, header);

        var patients = new List<ParsedPatient>();
        ParsedPatient? currentPatient = null;
        bool stopParsing = false;

        foreach (var page in pages)
        {
            if (stopParsing) break;

            for (int i = 0; i < page.Lines.Count; i++)
            {
                var text = page.Lines[i].Text.Trim();

                if (text.Contains("TOTAL FOR CHECK:", StringComparison.OrdinalIgnoreCase))
                {
                    header.DocumentTotals = TryParseTotals(page.Lines, i);
                    stopParsing = true;
                    break;
                }

                // Member header: MEMBER ID, MEMBER NAME, DOB or VENDOR
                if (text.Contains("MEMBER ID", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("VENDOR", StringComparison.OrdinalIgnoreCase))
                {
                    currentPatient = ParseMemberLine(page.Lines, i);
                    if (currentPatient != null)
                    {
                        patients.Add(currentPatient);
                    }
                    continue;
                }

                if (currentPatient != null)
                {
                    var proc = TryParseProcedure(text);
                    if (proc != null)
                    {
                        currentPatient.Procedures.Add(proc);
                    }
                }
            }
        }

        header.Patients = patients;
        return header;
    }

    private static void ParseHeader(PdfPageLines page, ParsedEobHeader header)
    {
        // Insurance zone: Top > 0.85 * Height, Left < 0.35 * Width
        var insLines = page.Lines
            .Where(l => l.Top > 0.85 * page.Height && l.Left < 0.35 * page.Width && !string.IsNullOrWhiteSpace(l.Text))
            .OrderByDescending(l => l.Top)
            .ToList();

        if (insLines.Count > 0)
        {
            header.InsuranceName = insLines[0].Text.Trim();
            // Address: lines 1-3
            if (insLines.Count > 1)
            {
                header.InsuranceAddress = string.Join(", ", insLines.Skip(1).Take(3).Select(l => l.Text.Trim()));
            }
        }

        // Provider zone: Top 0.70-0.85 * Height, Left < 0.35 * Width
        var provLines = page.Lines
            .Where(l => l.Top >= 0.70 * page.Height && l.Top <= 0.85 * page.Height && l.Left < 0.35 * page.Width && !string.IsNullOrWhiteSpace(l.Text))
            .OrderByDescending(l => l.Top)
            .ToList();

        if (provLines.Count > 0)
        {
            header.ProviderName = provLines[0].Text.Trim();
            if (provLines.Count > 1)
            {
                header.ProviderAddress = string.Join(", ", provLines.Skip(1).Select(l => l.Text.Trim()));
            }
        }

        var fullText = page.FullText;
        var checkMatch = Regex.Match(fullText, @"(?:Check\s*(?:#|Number|EFT)|Check/EFT)[:\s]*([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        if (checkMatch.Success) header.CheckEFT = checkMatch.Groups[1].Value.Trim();

        var dateMatch = Regex.Match(fullText, @"(?:Date|Statement\s*Date|Issue\s*Date)[:\s]*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase);
        if (dateMatch.Success)
        {
            header.StatementDate = dateMatch.Groups[1].Value.Trim();
            header.StatementDateParsed = ServiceDateParser.Parse(header.StatementDate);
        }
    }

    private static ParsedPatient? ParseMemberLine(IReadOnlyList<TextLine> lines, int i)
    {
        var text = lines[i].Text;
        var p = new ParsedPatient();

        var midMatch = Regex.Match(text, @"(?:MEMBER\s*ID|MID)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (midMatch.Success) p.MID = midMatch.Groups[1].Value.Trim();

        var nameMatch = Regex.Match(text, @"(?:MEMBER\s*NAME|NAME)[:\s]*([A-Za-z\s,.-]+?)(?:\s+(?:DOB|MID|VENDOR)|$)", RegexOptions.IgnoreCase);
        if (nameMatch.Success) p.PatientName = nameMatch.Groups[1].Value.Trim();

        var acntMatch = Regex.Match(text, @"(?:ACCOUNT|PATIENT\s*NO|ACNT)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (acntMatch.Success) p.PatientNo = acntMatch.Groups[1].Value.Trim();

        var icnMatch = Regex.Match(text, @"(?:CLAIM\s*#|ICN)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (icnMatch.Success) p.ICN = icnMatch.Groups[1].Value.Trim();

        if (string.IsNullOrWhiteSpace(p.PatientName))
        {
            p.PatientName = "Unknown LaSalle Member";
        }

        return p;
    }

    private static ParsedProcedure? TryParseProcedure(string text)
    {
        var dateMatch = Regex.Match(text, @"\b(\d{1,2}/\d{1,2}/\d{2,4})\b");
        if (!dateMatch.Success) return null;

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var procCodeMatch = Regex.Match(text, @"\b([A-Z0-9]{5})\b");

        var decimals = new List<decimal>();
        foreach (var t in tokens)
        {
            var cleaned = t.Replace("$", "").Replace(",", "").Trim('(', ')', '-');
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                decimals.Add(val);
            }
        }

        if (decimals.Count == 0) return null;

        var proc = new ParsedProcedure
        {
            DateOfService = dateMatch.Groups[1].Value,
            DateOfServiceParsed = ServiceDateParser.Parse(dateMatch.Groups[1].Value),
            ProcedureCode = procCodeMatch.Success ? procCodeMatch.Groups[1].Value : "UNKNOWN"
        };

        if (decimals.Count > 0) proc.Billed = decimals[0];
        if (decimals.Count > 1) proc.Allowed = decimals[1];
        if (decimals.Count > 2) proc.Deductible = decimals[2];
        if (decimals.Count > 3) proc.CoInsurance = decimals[3];
        if (decimals.Count > 4) proc.ProviderPaid = decimals[4];

        return proc;
    }

    private static ParsedDocumentTotals? TryParseTotals(IReadOnlyList<TextLine> lines, int i)
    {
        var targetLine = lines[i].Text;
        var tokens = targetLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var decimals = new List<decimal>();
        foreach (var t in tokens)
        {
            var cleaned = t.Replace("$", "").Replace(",", "").Trim('(', ')', '-');
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                decimals.Add(val);
            }
        }

        if (decimals.Count == 0) return null;

        var totals = new ParsedDocumentTotals();
        if (decimals.Count > 0) totals.Billed = decimals[0];
        if (decimals.Count > 1) totals.Allowed = decimals[1];
        if (decimals.Count > 2) totals.Deductible = decimals[2];
        if (decimals.Count > 3) totals.CoInsurance = decimals[3];
        if (decimals.Count > 4) totals.ProviderPaid = decimals[4];
        if (decimals.Count > 5) totals.CheckAmount = decimals[5];

        return totals;
    }
}
