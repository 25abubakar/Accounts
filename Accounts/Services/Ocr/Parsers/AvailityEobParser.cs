using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

public sealed class AvailityEobParser : IAvailityEobParser
{
    private readonly IPdfTextExtractor _extractor;
    private static readonly HashSet<string> ValidPos = ["11", "20", "21", "22", "23", "24", "49", "50", "72", "81", "99"];

    public AvailityEobParser(IPdfTextExtractor extractor)
    {
        _extractor = extractor;
    }

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes, yTolerance: 3.5, xGap: 0.75);
        var header = new ParsedEobHeader
        {
            Source = "AVAILITY Docs"
        };

        if (pages.Count == 0) return header;

        var firstPage = pages[0];
        ParseHeaderFields(firstPage, header);

        ParsedPatient? currentPatient = null;
        var patients = new List<ParsedPatient>();

        foreach (var page in pages)
        {
            for (int i = 0; i < page.Lines.Count; i++)
            {
                var line = page.Lines[i];
                var text = line.Text.Trim();

                // Check for totals line
                if (text.Contains("TOTALS:") || text.StartsWith("TOTALS", StringComparison.OrdinalIgnoreCase))
                {
                    header.DocumentTotals = ParseTotalsLine(page.Lines, i);
                    continue;
                }

                // Check for patient line
                if (text.StartsWith("NAME:", StringComparison.OrdinalIgnoreCase) || text.Contains("NAME:"))
                {
                    var p = ParsePatientLine(text);
                    if (p != null)
                    {
                        currentPatient = p;
                        patients.Add(currentPatient);
                    }
                    continue;
                }

                // If we have an active patient, try matching procedure line
                if (currentPatient != null)
                {
                    var proc = TryParseProcedureLine(text);
                    if (proc != null)
                    {
                        // Check following lines for GRP / REM details if any
                        CaptureGrpAndRemarks(page.Lines, ref i, proc);
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

        // Phone & Fax
        var phoneMatch = Regex.Match(fullText, @"(?:Phone|Telephone)[:\s]+(\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4})", RegexOptions.IgnoreCase);
        if (phoneMatch.Success) header.Phone = phoneMatch.Groups[1].Value.Trim();

        var faxMatch = Regex.Match(fullText, @"Fax[:\s]+(\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4})", RegexOptions.IgnoreCase);
        if (faxMatch.Success) header.Fax = faxMatch.Groups[1].Value.Trim();

        // Check / EFT
        var checkMatch = Regex.Match(fullText, @"(?:Check/EFT|Check\s*#|Check\s*Number|EFT\s*#)[:\s]*([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        if (checkMatch.Success) header.CheckEFT = checkMatch.Groups[1].Value.Trim();

        // Statement Date
        var dateMatch = Regex.Match(fullText, @"(?:Statement\s*Date|Date)[:\s]*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase);
        if (dateMatch.Success)
        {
            header.StatementDate = dateMatch.Groups[1].Value.Trim();
            header.StatementDateParsed = ServiceDateParser.Parse(header.StatementDate);
        }

        // Provider ID & NPI
        var provIdMatch = Regex.Match(fullText, @"Provider\s*ID[:\s]*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (provIdMatch.Success) header.ProviderId = provIdMatch.Groups[1].Value.Trim();

        var npiMatch = Regex.Match(fullText, @"NPI[:\s]*(\d{10})", RegexOptions.IgnoreCase);
        if (npiMatch.Success) header.NPI = npiMatch.Groups[1].Value.Trim();

        // Tax ID & Payment Week
        var taxMatch = Regex.Match(fullText, @"Tax\s*ID[:\s]*([A-Za-z0-9-]+)", RegexOptions.IgnoreCase);
        if (taxMatch.Success) header.TaxId = taxMatch.Groups[1].Value.Trim();

        var weekMatch = Regex.Match(fullText, @"Payment\s*Week[:\s]*([^\r\n]+)", RegexOptions.IgnoreCase);
        if (weekMatch.Success) header.PaymentWeek = weekMatch.Groups[1].Value.Trim();

        // Coordinate-based zones:
        // Title: Top > 0.85 * Height and Left > 0.65 * Width
        var titleLine = page.Lines.FirstOrDefault(l => l.Top > 0.85 * page.Height && l.Left > 0.65 * page.Width);
        if (titleLine != null) header.DocTitle = titleLine.Text.Trim();
        else if (fullText.Contains("EXPLANATION OF BENEFITS", StringComparison.OrdinalIgnoreCase))
            header.DocTitle = "EXPLANATION OF BENEFITS";

        // Insurance zone: Left < 0.45 * Width, Top > 0.70 * Height
        var insLines = page.Lines
            .Where(l => l.Left < 0.45 * page.Width && l.Top > 0.70 * page.Height && !string.IsNullOrWhiteSpace(l.Text))
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

        // Provider zone: Left > Width / 2.5, Top > 0.70 * Height
        var provLines = page.Lines
            .Where(l => l.Left > (page.Width / 2.5) && l.Top > 0.70 * page.Height && !string.IsNullOrWhiteSpace(l.Text))
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
    }

    private static ParsedPatient? ParsePatientLine(string text)
    {
        // Example: NAME: DOE, JOHN MID: 123456789 ACNT: ACC998 ICN: 9988776655
        var p = new ParsedPatient();
        var match = Regex.Match(text, @"NAME:\s*(?<name>[^,]+,\s*[^\s]+|[^\s]+(?:\s+[^\s]+)?)(?:\s+MID:\s*(?<mid>[^\s]+))?(?:\s+ACNT:\s*(?<acnt>[^\s]+))?(?:\s+ICN:\s*(?<icn>[^\s]+))?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            p.PatientName = match.Groups["name"].Value.Trim();
            if (match.Groups["mid"].Success) p.MID = match.Groups["mid"].Value.Trim();
            if (match.Groups["acnt"].Success) p.PatientNo = match.Groups["acnt"].Value.Trim();
            if (match.Groups["icn"].Success) p.ICN = match.Groups["icn"].Value.Trim();
            return p;
        }

        return null;
    }

    private static ParsedProcedure? TryParseProcedureLine(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 8) return null;

        // Check if one of first few tokens is POS
        int posIdx = -1;
        for (int i = 0; i < Math.Min(tokens.Length, 4); i++)
        {
            if (ValidPos.Contains(tokens[i]))
            {
                posIdx = i;
                break;
            }
        }

        if (posIdx == -1) return null;

        var proc = new ParsedProcedure
        {
            POS = tokens[posIdx]
        };

        // Try to identify Date, ProcCode, Modifiers, NOS, and numeric amounts
        // In Availity: [RenderingProvider] [POS] [NOS] [ProcCode] [Modifiers] [DateOfService] [Billed] [Allowed] [Deductible] [Coins] [Paid]
        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            // Date of service?
            if (t.Contains('/') || t.Length == 6 || t.Length == 8)
            {
                var dt = ServiceDateParser.Parse(t);
                if (dt.HasValue && proc.DateOfService == null)
                {
                    proc.DateOfService = t;
                    proc.DateOfServiceParsed = dt;
                    continue;
                }
            }

            // Procedure code? 5 alphanumeric
            if (proc.ProcedureCode == null && Regex.IsMatch(t, @"^[A-Z0-9]{5}$") && !decimal.TryParse(t, out _))
            {
                proc.ProcedureCode = t;
                continue;
            }
        }

        // Parse decimal amounts from right-to-left or remaining tokens
        var decimals = new List<decimal>();
        foreach (var t in tokens)
        {
            var cleaned = t.Replace("$", "").Replace(",", "").Trim('(', ')', '-');
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                decimals.Add(val);
            }
        }

        if (decimals.Count >= 2)
        {
            proc.Billed = decimals[0];
            if (decimals.Count > 1) proc.Allowed = decimals[1];
            if (decimals.Count > 2) proc.Deductible = decimals[2];
            if (decimals.Count > 3) proc.CoInsurance = decimals[3];
            if (decimals.Count > 4) proc.ProviderPaid = decimals[4];
        }

        return proc;
    }

    private static void CaptureGrpAndRemarks(IReadOnlyList<TextLine> lines, ref int i, ParsedProcedure proc)
    {
        var grpSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check if current or subsequent lines have GRP code like CO-45, PR-1
        while (i + 1 < lines.Count)
        {
            var nextText = lines[i + 1].Text.Trim();
            var grpMatches = Regex.Matches(nextText, @"\b([A-Z]{2}-[A-Z0-9]+)\b");
            if (grpMatches.Count > 0)
            {
                i++;
                foreach (Match m in grpMatches)
                {
                    var grpCode = m.Groups[1].Value;
                    if (grpSet.Add(grpCode))
                    {
                        proc.GrpRows.Add(new ParsedGrp
                        {
                            GRPCode = grpCode,
                            Remark = nextText
                        });
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    private static ParsedDocumentTotals? ParseTotalsLine(IReadOnlyList<TextLine> lines, int i)
    {
        var targetLine = lines[i].Text;
        if (i + 2 < lines.Count && !targetLine.Any(char.IsDigit))
        {
            targetLine = lines[i + 2].Text;
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
        if (decimals.Count > 8) totals.ProviderDiscount = decimals[8];
        if (decimals.Count > 9) totals.OtherPlanPayment = decimals[9];

        return totals;
    }
}
