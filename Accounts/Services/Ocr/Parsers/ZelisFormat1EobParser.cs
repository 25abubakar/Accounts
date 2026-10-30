using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

public sealed class ZelisFormat1EobParser : IZelisFormat1EobParser
{
    private readonly IPdfTextExtractor _extractor;

    public ZelisFormat1EobParser(IPdfTextExtractor extractor)
    {
        _extractor = extractor;
    }

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes, yTolerance: 3.0, xGap: 0.75);
        var header = new ParsedEobHeader
        {
            Source = "Zelis Docs"
        };

        if (pages.Count == 0) return header;

        var firstPage = pages[0];
        ParseHeader(firstPage, header);

        var patients = new List<ParsedPatient>();
        ParsedPatient? currentPatient = null;

        foreach (var page in pages)
        {
            for (int i = 0; i < page.Lines.Count; i++)
            {
                var text = page.Lines[i].Text.Trim();

                if (text.Contains("PAYMENT SUMMARY", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("TOTALS:", StringComparison.OrdinalIgnoreCase))
                {
                    header.DocumentTotals = TryParseTotals(page.Lines, i);
                    continue;
                }

                // Check for patient / claim line
                // In Zelis: Patient Name, Account Number, Claim # / ICN
                if (text.Contains("Patient:", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Claim Number:", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Member ID:", StringComparison.OrdinalIgnoreCase))
                {
                    // If previous patient had no procedures, drop it
                    if (currentPatient != null && currentPatient.Procedures.Count == 0)
                    {
                        patients.Remove(currentPatient);
                    }

                    currentPatient = ParsePatient(page.Lines, i);
                    if (currentPatient != null)
                    {
                        patients.Add(currentPatient);
                    }
                    continue;
                }

                if (currentPatient != null)
                {
                    // Zelis service line starts with date range e.g. "01/15/26-01/15/26" or "01/15/2026 - 01/15/2026"
                    var proc = TryParseProcedure(page.Lines, ref i);
                    if (proc != null)
                    {
                        currentPatient.Procedures.Add(proc);
                    }
                }
            }
        }

        // Rule: Open patient at end of file with 0 procedures is dropped
        if (patients.Count > 0 && patients[^1].Procedures.Count == 0)
        {
            patients.RemoveAt(patients.Count - 1);
        }

        header.Patients = patients;
        return header;
    }

    private static void ParseHeader(PdfPageLines page, ParsedEobHeader header)
    {
        var fullText = page.FullText;

        var checkMatch = Regex.Match(fullText, @"(?:Payment\s*Reference\s*Number|Check\s*Number|Check/EFT|Trace\s*#)[:\s]*([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        if (checkMatch.Success) header.CheckEFT = checkMatch.Groups[1].Value.Trim();

        var dateMatch = Regex.Match(fullText, @"(?:Payment\s*Date|Issue\s*Date|Date)[:\s]*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase);
        if (dateMatch.Success)
        {
            header.StatementDate = dateMatch.Groups[1].Value.Trim();
            header.StatementDateParsed = ServiceDateParser.Parse(header.StatementDate);
        }

        var provMatch = Regex.Match(fullText, @"(?:Provider\s*Name|Payee)[:\s]*([A-Za-z\s,.-]+?)(?:\s+(?:NPI|Tax|Date)|$)", RegexOptions.IgnoreCase);
        if (provMatch.Success) header.ProviderName = provMatch.Groups[1].Value.Trim();

        var npiMatch = Regex.Match(fullText, @"NPI[:\s]*(\d{10})", RegexOptions.IgnoreCase);
        if (npiMatch.Success) header.NPI = npiMatch.Groups[1].Value.Trim();

        var taxMatch = Regex.Match(fullText, @"(?:Tax\s*ID|TIN)[:\s]*([A-Za-z0-9-]+)", RegexOptions.IgnoreCase);
        if (taxMatch.Success) header.TaxId = taxMatch.Groups[1].Value.Trim();

        header.DocTitle = "ZELIS REMITTANCE ADVICE";
    }

    private static ParsedPatient? ParsePatient(IReadOnlyList<TextLine> lines, int i)
    {
        var text = lines[i].Text;
        var p = new ParsedPatient();

        var nameMatch = Regex.Match(text, @"(?:Patient|Member)[:\s]*([A-Za-z\s,.-]+?)(?:\s+(?:Claim|ID|Account)|$)", RegexOptions.IgnoreCase);
        if (nameMatch.Success) p.PatientName = nameMatch.Groups[1].Value.Trim();

        var midMatch = Regex.Match(text, @"(?:Member\s*ID|MID)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (midMatch.Success) p.MID = midMatch.Groups[1].Value.Trim();

        var acntMatch = Regex.Match(text, @"(?:Account|Acct|Patient\s*#)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (acntMatch.Success) p.PatientNo = acntMatch.Groups[1].Value.Trim();

        var icnMatch = Regex.Match(text, @"(?:Claim\s*(?:#|Number)|ICN)[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (icnMatch.Success) p.ICN = icnMatch.Groups[1].Value.Trim();

        if (string.IsNullOrWhiteSpace(p.PatientName))
        {
            p.PatientName = "Unknown Zelis Patient";
        }

        return p;
    }

    private static ParsedProcedure? TryParseProcedure(IReadOnlyList<TextLine> lines, ref int i)
    {
        var text = lines[i].Text;
        // Match date range like "01/15/26-01/15/26" or "01/15/2026-01/15/2026"
        var rangeMatch = Regex.Match(text, @"\b(\d{1,2}/\d{1,2}/\d{2,4})\s*[-–]\s*(\d{1,2}/\d{1,2}/\d{2,4})\b");
        if (!rangeMatch.Success) return null;

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
            DateOfService = rangeMatch.Groups[1].Value,
            DateOfServiceParsed = ServiceDateParser.Parse(rangeMatch.Groups[1].Value),
            ProcedureCode = procCodeMatch.Success ? procCodeMatch.Groups[1].Value : "UNKNOWN"
        };

        if (decimals.Count > 0) proc.Billed = decimals[0];
        if (decimals.Count > 1) proc.Allowed = decimals[1];
        if (decimals.Count > 2) proc.Deductible = decimals[2];
        if (decimals.Count > 3) proc.CoInsurance = decimals[3];
        if (decimals.Count > 4) proc.ProviderPaid = decimals[4];
        if (decimals.Count > 5) proc.PatientResponsibility = decimals[5];

        // Reason codes in next lines
        while (i + 1 < lines.Count)
        {
            var next = lines[i + 1].Text.Trim();
            // Look for reason codes (GRP) like PR-2, CO-45
            var grpMatches = Regex.Matches(next, @"\b([A-Z]{2}-[A-Z0-9]+)\b");
            if (grpMatches.Count > 0)
            {
                i++;
                foreach (Match m in grpMatches)
                {
                    var code = m.Groups[1].Value;
                    proc.GrpRows.Add(new ParsedGrp
                    {
                        GRPCode = code,
                        Remark = next
                    });

                    // PR2 GRP rule: move amount to coins, clear patient responsibility
                    if (code.Equals("PR-2", StringComparison.OrdinalIgnoreCase) || code.Equals("PR2", StringComparison.OrdinalIgnoreCase))
                    {
                        if (proc.PatientResponsibility.HasValue && proc.PatientResponsibility > 0)
                        {
                            proc.CoInsurance = (proc.CoInsurance ?? 0) + proc.PatientResponsibility.Value;
                            proc.PatientResponsibility = 0;
                        }
                    }
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

