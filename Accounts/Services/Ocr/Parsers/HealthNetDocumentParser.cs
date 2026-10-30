using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

internal sealed class RegexHealthNetDocumentParser : IHealthNetDocumentParser
{
    private readonly IPdfTextExtractor _extractor;

    public RegexHealthNetDocumentParser(IPdfTextExtractor extractor)
    {
        _extractor = extractor;
    }

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes, yTolerance: 3.0, xGap: 0.75);
        var header = new ParsedEobHeader
        {
            Source = "HEALTH NET Docs"
        };

        if (pages.Count == 0) return header;

        // Process pages
        var patients = new List<ParsedPatient>();
        ParsedPatient? currentPatient = null;

        for (int pIdx = 0; pIdx < pages.Count; pIdx++)
        {
            var page = pages[pIdx];

            // Scanned page check: < 10 words OR < 50 chars -> skip
            var fullText = page.FullText;
            if (page.WordCount < 10 || fullText.Length < 50)
            {
                header.Warnings.Add($"Page {pIdx + 1} appears to be a scanned image or empty (word count {page.WordCount}, text length {fullText.Length}) and was skipped.");
                continue;
            }

            if (pIdx == 0)
            {
                ParseHeaderFields(page, header);
            }

            for (int i = 0; i < page.Lines.Count; i++)
            {
                var text = page.Lines[i].Text.Trim();

                // Totals
                if (text.Contains("CHECK TOTAL", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("TOTAL PAYMENT", StringComparison.OrdinalIgnoreCase))
                {
                    header.DocumentTotals = TryParseTotals(page.Lines, i);
                    continue;
                }

                // Health Net Patient header: usually has "Claim Number:", "Subscriber ID:", "Patient Name:"
                if (text.Contains("Patient Name:", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Claim Number:", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Subscriber ID:", StringComparison.OrdinalIgnoreCase))
                {
                    currentPatient = ParsePatient(page.Lines, i);
                    if (currentPatient != null)
                    {
                        patients.Add(currentPatient);
                    }
                    continue;
                }

                // Service lines
                if (currentPatient != null)
                {
                    var proc = TryParseProcedure(page.Lines, ref i);
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

    private static void ParseHeaderFields(PdfPageLines page, ParsedEobHeader header)
    {
        var fullText = page.FullText;

        var checkMatch = Regex.Match(fullText, @"(?:Check\s*(?:#|Number)|EFT\s*#|Trace\s*#)[:\s]*([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        if (checkMatch.Success) header.CheckEFT = checkMatch.Groups[1].Value.Trim();

        var dateMatch = Regex.Match(fullText, @"(?:Issue\s*Date|Check\s*Date|Date)[:\s]*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase);
        if (dateMatch.Success)
        {
            header.StatementDate = dateMatch.Groups[1].Value.Trim();
            header.StatementDateParsed = ServiceDateParser.Parse(header.StatementDate);
        }

        var provMatch = Regex.Match(fullText, @"(?:Payee\s*Name|Provider\s*Name)[:\s]*([A-Za-z\s,.-]+?)(?:\s+(?:NPI|Tax|Date)|$)", RegexOptions.IgnoreCase);
        if (provMatch.Success) header.ProviderName = provMatch.Groups[1].Value.Trim();

        var npiMatch = Regex.Match(fullText, @"NPI[:\s]*(\d{10})", RegexOptions.IgnoreCase);
        if (npiMatch.Success) header.NPI = npiMatch.Groups[1].Value.Trim();

        var taxMatch = Regex.Match(fullText, @"(?:Tax\s*ID|TIN)[:\s]*([A-Za-z0-9-]+)", RegexOptions.IgnoreCase);
        if (taxMatch.Success) header.TaxId = taxMatch.Groups[1].Value.Trim();

        header.InsuranceName = "Health Net";
        header.DocTitle = "HEALTH NET EXPLANATION OF PAYMENT";
    }

    private static ParsedPatient? ParsePatient(IReadOnlyList<TextLine> lines, int i)
    {
        var text = lines[i].Text;
        var p = new ParsedPatient();

        var nameMatch = Regex.Match(text, @"Patient\s*Name[:\s]*([A-Za-z\s,.-]+?)(?:\s+(?:Claim|ID|Subscriber|Account)|$)", RegexOptions.IgnoreCase);
        if (nameMatch.Success) p.PatientName = nameMatch.Groups[1].Value.Trim();

        var midMatch = Regex.Match(text, @"(?:Subscriber\s*ID|MID|Member\s*ID)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (midMatch.Success) p.MID = midMatch.Groups[1].Value.Trim();

        var acntMatch = Regex.Match(text, @"(?:Patient\s*Account|Account\s*#|Acct)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (acntMatch.Success) p.PatientNo = acntMatch.Groups[1].Value.Trim();

        var icnMatch = Regex.Match(text, @"(?:Claim\s*(?:#|Number)|ICN)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (icnMatch.Success) p.ICN = icnMatch.Groups[1].Value.Trim();

        if (string.IsNullOrWhiteSpace(p.PatientName))
        {
            p.PatientName = "Unknown Health Net Patient";
        }

        return p;
    }

    private static ParsedProcedure? TryParseProcedure(IReadOnlyList<TextLine> lines, ref int i)
    {
        var text = lines[i].Text;
        // Service line date
        var dateMatch = Regex.Match(text, @"\b(\d{1,2}/\d{1,2}/\d{2,4})\b");
        if (!dateMatch.Success) return null;

        var procCodeMatch = Regex.Match(text, @"\b([A-Z0-9]{5})\b");

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
        if (decimals.Count > 4) proc.Copay = decimals[4];
        if (decimals.Count > 5) proc.ProviderPaid = decimals[5];

        // Explanations / Remark lines
        while (i + 1 < lines.Count)
        {
            var next = lines[i + 1].Text.Trim();
            // Explanation code pattern or remark
            var expMatch = Regex.Matches(next, @"\b([A-Z0-9]{2,4})\b");
            if (next.StartsWith("Reason", StringComparison.OrdinalIgnoreCase) ||
                next.StartsWith("Remark", StringComparison.OrdinalIgnoreCase) ||
                next.Contains("Explanation", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                proc.Remarks.Add(next);
                foreach (Match m in expMatch)
                {
                    proc.GrpRows.Add(new ParsedGrp
                    {
                        GRPCode = m.Groups[1].Value,
                        Remark = next
                    });
                }
            }
            else
            {
                break;
            }
        }

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

