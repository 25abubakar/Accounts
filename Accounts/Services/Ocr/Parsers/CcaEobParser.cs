using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

public sealed class CcaEobParser : ICcaEobParser
{
    private readonly IPdfTextExtractor _extractor;

    public CcaEobParser(IPdfTextExtractor extractor)
    {
        _extractor = extractor;
    }

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes, yTolerance: 3.0, xGap: 0.75);
        var header = new ParsedEobHeader
        {
            Source = "CCA Docs"
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

                // Stop conditions
                if (text.Contains("Please Note:", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Explanations", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Document Total", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Net Payment Amount:Payment Adjustments:", StringComparison.OrdinalIgnoreCase))
                {
                    if (text.Contains("Document Total", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("Net Payment Amount", StringComparison.OrdinalIgnoreCase))
                    {
                        header.DocumentTotals = TryParseTotals(page.Lines, i);
                    }
                    stopParsing = true;
                    break;
                }

                // Patient / Claim line containing "Tax ID:"
                if (text.Contains("Tax ID:", StringComparison.OrdinalIgnoreCase))
                {
                    currentPatient = ParsePatient(page.Lines, i);
                    if (currentPatient != null)
                    {
                        patients.Add(currentPatient);
                    }
                    continue;
                }

                // If active patient, look for procedure lines
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
        // Insurance zone: Top > 0.85 * Height, Left < 0.35 * Width, drop lines with "$$"
        var insLines = page.Lines
            .Where(l => l.Top > 0.85 * page.Height && l.Left < 0.35 * page.Width && !l.Text.Contains("$$") && !string.IsNullOrWhiteSpace(l.Text))
            .OrderByDescending(l => l.Top)
            .ToList();

        if (insLines.Count > 0)
        {
            header.InsuranceName = insLines[0].Text.Trim();
            if (insLines.Count > 1)
            {
                header.InsuranceAddress = string.Join(", ", insLines.Skip(1).Select(l => l.Text.Trim()));
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

        // Check/EFT & Date
        var fullText = page.FullText;
        var checkMatch = Regex.Match(fullText, @"(?:Check\s*(?:#|Number|EFT)|Check/EFT)[:\s]*([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        if (checkMatch.Success) header.CheckEFT = checkMatch.Groups[1].Value.Trim();

        var dateMatch = Regex.Match(fullText, @"(?:Statement\s*Date|Date|Issue\s*Date)[:\s]*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase);
        if (dateMatch.Success)
        {
            header.StatementDate = dateMatch.Groups[1].Value.Trim();
            header.StatementDateParsed = ServiceDateParser.Parse(header.StatementDate);
        }
    }

    private static ParsedPatient? ParsePatient(IReadOnlyList<TextLine> lines, int i)
    {
        var text = lines[i].Text;
        var p = new ParsedPatient();

        // Regex for Tax ID and adjacent identifiers
        var taxMatch = Regex.Match(text, @"Tax\s*ID[:\s]*([^\s]+)", RegexOptions.IgnoreCase);
        var nameMatch = Regex.Match(text, @"(?:Name|Patient)[:\s]*([A-Za-z\s,.-]+?)(?:\s+(?:Tax|MID|ACNT|ID)|$)", RegexOptions.IgnoreCase);
        if (nameMatch.Success) p.PatientName = nameMatch.Groups[1].Value.Trim();

        var midMatch = Regex.Match(text, @"(?:MID|Member\s*ID)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (midMatch.Success) p.MID = midMatch.Groups[1].Value.Trim();

        var acntMatch = Regex.Match(text, @"(?:ACNT|Account|Pat\s*#)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (acntMatch.Success) p.PatientNo = acntMatch.Groups[1].Value.Trim();

        var icnMatch = Regex.Match(text, @"(?:ICN|Claim\s*#)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (icnMatch.Success) p.ICN = icnMatch.Groups[1].Value.Trim();

        if (string.IsNullOrWhiteSpace(p.PatientName))
        {
            p.PatientName = "Unknown CCA Patient";
        }

        return p;
    }

    private static ParsedProcedure? TryParseProcedure(string text)
    {
        // Service line: date (MM/dd/yy), procedure code (e.g. 99213), amounts
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

        // Copay if detected
        var copayMatch = Regex.Match(text, @"Copay[:\s]*\$?(\d+(?:\.\d{2})?)", RegexOptions.IgnoreCase);
        if (copayMatch.Success && decimal.TryParse(copayMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var copayVal))
        {
            proc.Copay = copayVal;
        }

        return proc;
    }

    private static ParsedDocumentTotals? TryParseTotals(IReadOnlyList<TextLine> lines, int i)
    {
        var targetLine = lines[i].Text;
        if (i + 1 < lines.Count && !targetLine.Any(char.IsDigit))
        {
            targetLine = lines[i + 1].Text;
        }

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
        if (decimals.Count > 4) totals.RC = decimals[4];
        if (decimals.Count > 5) totals.ProviderPaid = decimals[5];
        if (decimals.Count > 6) totals.Adjustment = decimals[6];
        if (decimals.Count > 7) totals.CheckAmount = decimals[7];

        return totals;
    }
}

