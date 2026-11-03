using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

public sealed class LaSalleEobParser : ILaSalleEobParser
{
    private readonly IPdfTextExtractor _extractor;
    public LaSalleEobParser(IPdfTextExtractor extractor) => _extractor = extractor;

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes);
        var result = new ParsedEobHeader { Source = "Lassale Docs", DocTitle = "EXPLANATION OF BENEFITS" };
        if (pages.Count == 0) return result;
        ParseHeader(pages[0], result);

        ParsedPatient? patient = null;
        foreach (var page in pages)
        {
            for (var i = 0; i < page.Lines.Count; i++)
            {
                var line = page.Lines[i];
                var text = Clean(line.Text);
                if (IsMemberRow(line))
                {
                    patient = ParseMember(line);
                    if (patient != null) result.Patients.Add(patient);
                    continue;
                }
                if (patient != null && text.StartsWith("CLAIM NUMBER:", StringComparison.OrdinalIgnoreCase))
                {
                    PopulateClaimIdentity(patient, text);
                    continue;
                }
                if (patient != null && text.Contains("TOTALS:", StringComparison.OrdinalIgnoreCase) && text.StartsWith("TAT:", StringComparison.OrdinalIgnoreCase))
                {
                    ParsePatientTotals(line, patient);
                    continue;
                }
                if (patient != null && TryParseProcedure(line, out var procedure)) patient.Procedures.Add(procedure);
            }
        }

        result.DocumentTotals = ParseDocumentTotals(pages, result.Patients.Count);
        if (result.DocumentTotals != null)
        {
            result.DocumentTotals.Billed = result.Patients.Sum(x => x.Billed ?? 0m);
            result.DocumentTotals.Allowed = result.Patients.Sum(x => x.Allowed ?? 0m);
            result.DocumentTotals.Deductible = result.Patients.Sum(x => x.Deductible ?? 0m);
            result.DocumentTotals.CoInsurance = result.Patients.Sum(x => x.CoInsurance ?? 0m);
            result.DocumentTotals.RC = result.Patients.Sum(x => x.PatientResponsibility ?? 0m);
            result.DocumentTotals.ProviderDiscount = result.Patients.Sum(x => x.ProviderDiscount ?? 0m);
            result.DocumentTotals.Adjustment = result.Patients.Sum(x => x.OtherAdjustments ?? 0m);
        }
        if (result.Patients.Count == 0) result.Warnings.Add("LaSalle member claims were not found.");
        if (result.Patients.Any(x => x.Procedures.Count == 0)) result.Warnings.Add("One or more LaSalle claims contain no service lines.");
        if (result.DocumentTotals == null) result.Warnings.Add("LaSalle check total was not found.");
        return result;
    }

    private static void ParseHeader(PdfPageLines page, ParsedEobHeader result)
    {
        var lines = page.Lines;
        var vendor = lines.FirstOrDefault(x => x.Text.TrimStart().StartsWith("VENDOR ", StringComparison.OrdinalIgnoreCase));
        if (vendor != null)
        {
            var text = Clean(vendor.Text);
            result.ProviderName = Match(text, @"VENDOR\s+(.+?)\s+\(([A-Za-z0-9-]+)\)\s+CHECK");
            result.ProviderId = Match(text, @"VENDOR\s+.+?\s+\(([A-Za-z0-9-]+)\)\s+CHECK");
            result.TaxId = result.ProviderId;
            result.CheckEFT = Match(text, @"\bCHECK\s+([^\s]+)");
            result.StatementDate = Match(text, @"\bDATE\s+(\d{1,2}/\d{1,2}/\d{2,4})");
            result.StatementDateParsed = ServiceDateParser.Parse(result.StatementDate);
        }
        result.InsuranceName = lines.FirstOrDefault(x => x.Text.Trim().Equals("LaSalle Medical Associates", StringComparison.OrdinalIgnoreCase))?.Text.Trim()
            ?? "LaSalle Medical Associates";
        var accounting = IndexOf(lines, x => x.Text.Trim().Equals("Accounting Dept.", StringComparison.OrdinalIgnoreCase));
        if (accounting >= 0) result.InsuranceAddress = JoinLeftColumn(lines, accounting, 3, 300);

        var providerIndex = IndexOf(lines, x => x.Text.Contains("SIERRA FAMILY MEDICAL GROUP CORP", StringComparison.OrdinalIgnoreCase));
        if (providerIndex >= 0)
        {
            result.ProviderName ??= Clean(lines[providerIndex].Text);
            result.ProviderAddress = JoinLeftColumn(lines, providerIndex + 1, 2, 300);
        }
    }

    private static bool IsMemberRow(TextLine line)
    {
        var words = line.Words;
        if (words.Count < 4 || words[0].Left >= 40 ||
            !Regex.IsMatch(words[0].Text, @"^(?=.*\d)[A-Z0-9]{6,}$")) return false;

        // Member DOB has a stable column even when names and health plans vary.
        // This also excludes the VENDOR line, which contains the payment date.
        return words.Any(w => w.Left >= 190 && w.Left < 250 &&
            Regex.IsMatch(w.Text, @"^\d{1,2}/\d{1,2}/\d{4}$"));
    }

    private static ParsedPatient? ParseMember(TextLine line)
    {
        var dob = line.Words.FirstOrDefault(w => Regex.IsMatch(w.Text, @"^\d{1,2}/\d{1,2}/\d{4}$"));
        if (dob == null) return null;
        var name = string.Join(" ", line.Words.Where(w => w.Left >= 80 && w.Left < dob.Left).Select(w => w.Text));
        return new ParsedPatient { MID = line.Words[0].Text, PatientName = Clean(name) };
    }

    private static void PopulateClaimIdentity(ParsedPatient patient, string text)
    {
        patient.ICN = Match(text, @"CLAIM\s*NUMBER:\s*([^\s]+)");
        patient.PatientNo = Match(text, @"ACCOUNT\s*NUMBER:\s*([^\s]+)");
    }

    private static bool TryParseProcedure(TextLine line, out ParsedProcedure procedure)
    {
        procedure = null!;
        var words = line.Words;
        if (words.Count < 8 || words[0].Left > 40 || !Regex.IsMatch(words[0].Text, @"^\d+$")) return false;
        var proc = words.FirstOrDefault(w => w.Left >= 40 && w.Left < 78 && Regex.IsMatch(w.Text, @"^[A-Z0-9]{5}$"));
        var date = words.FirstOrDefault(w => w.Left >= 90 && w.Left < 150 && Regex.IsMatch(w.Text, @"^\d{1,2}/\d{1,2}/\d{2,4}$"));
        if (proc == null || date == null) return false;
        var modifier = words.FirstOrDefault(w => w.Left > proc.Right && w.Left < 90 && Regex.IsMatch(w.Text, @"^[A-Z0-9]{2}$"));
        procedure = new ParsedProcedure
        {
            SubNOS = words[0].Text,
            // The second "SEQ" column is the sequestration amount, not a row sequence.
            NOS = words.FirstOrDefault(w => w.Left >= 425 && w.Left < 465 && Money(w.Text).HasValue)?.Text,
            ProcedureCode = proc.Text,
            Modifiers = modifier?.Text,
            DateOfService = date.Text,
            DateOfServiceParsed = ServiceDateParser.Parse(date.Text),
            Billed = MoneyAt(words, 155, 205),
            Allowed = MoneyAt(words, 205, 255),
            OtherAdjustments = MoneyAt(words, 255, 300),
            Copay = MoneyAt(words, 300, 345),
            CoInsurance = MoneyAt(words, 345, 390),
            Deductible = MoneyAt(words, 390, 430),
            Interest = MoneyAt(words, 465, 515),
            ProviderPaid = MoneyAt(words, 515, 555)
        };
        procedure.NOS_Val = Money(procedure.NOS);
        var adjustmentCode = words.FirstOrDefault(w => w.Left >= 555 && Regex.IsMatch(w.Text, @"^\d+$"))?.Text;
        procedure.ProviderDiscount = (procedure.Billed ?? 0m) - (procedure.Allowed ?? 0m);
        if (!string.IsNullOrWhiteSpace(adjustmentCode))
            procedure.GrpRows.Add(new ParsedGrp { GRPCode = $"CO-{adjustmentCode}", RcAmt = procedure.ProviderDiscount, Remark = adjustmentCode });
        return true;
    }

    private static void ParsePatientTotals(TextLine line, ParsedPatient patient)
    {
        patient.Billed = MoneyAt(line.Words, 155, 205);
        patient.Allowed = MoneyAt(line.Words, 205, 255);
        patient.OtherAdjustments = MoneyAt(line.Words, 255, 300);
        var copay = MoneyAt(line.Words, 300, 345) ?? 0m;
        patient.CoInsurance = MoneyAt(line.Words, 345, 390);
        patient.Deductible = MoneyAt(line.Words, 390, 430);
        patient.Interest = MoneyAt(line.Words, 465, 515);
        patient.ProviderPaid = MoneyAt(line.Words, 515, 555);
        patient.Net = patient.ProviderPaid;
        patient.ProviderDiscount = (patient.Billed ?? 0m) - (patient.Allowed ?? 0m);
        patient.PatientResponsibility = copay + (patient.CoInsurance ?? 0m) + (patient.Deductible ?? 0m);
    }

    private static ParsedDocumentTotals? ParseDocumentTotals(IReadOnlyList<PdfPageLines> pages, int claimCount)
    {
        var line = pages.SelectMany(x => x.Lines).FirstOrDefault(x => x.Text.TrimStart().StartsWith("TOTAL FOR CHECK:", StringComparison.OrdinalIgnoreCase));
        if (line == null) return null;
        var text = Clean(line.Text);
        var amount = Match(text, @"TOTAL\s*FOR\s*CHECK:\s*[^\s]+\s+\$?([\d,.-]+)");
        var value = Money(amount);
        return new ParsedDocumentTotals { ClaimCount = claimCount, ProviderPaid = value, CheckAmount = value };
    }

    private static decimal? MoneyAt(IReadOnlyList<WordBox> words, double min, double max) => Money(words.FirstOrDefault(w => w.Left >= min && w.Left < max && Money(w.Text).HasValue)?.Text);
    private static decimal? Money(string? value) { var text = value?.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal); return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null; }
    private static decimal? Decimal(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static string? Match(string text, string pattern) { var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value.Trim() : null; }
    private static string Clean(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
    private static int IndexOf(IReadOnlyList<TextLine> lines, Func<TextLine, bool> predicate) { for (var i = 0; i < lines.Count; i++) if (predicate(lines[i])) return i; return -1; }
    private static string? JoinLeftColumn(IReadOnlyList<TextLine> lines, int start, int count, double maxLeft)
    {
        var parts = lines.Skip(start).Take(count)
            .Select(line => Clean(string.Join(" ", line.Words.Where(w => w.Left < maxLeft).Select(w => w.Text))))
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var joined = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
