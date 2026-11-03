using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

public sealed class ZelisFormat1EobParser : IZelisFormat1EobParser
{
    private readonly IPdfTextExtractor _extractor;
    public ZelisFormat1EobParser(IPdfTextExtractor extractor) => _extractor = extractor;

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes);
        var result = new ParsedEobHeader { Source = "Zelis Docs", DocTitle = "ZELIS EXPLANATION OF PAYMENT" };
        if (pages.Count == 0) return result;
        ParseHeader(pages[0], result);

        ParsedPatient? patient = null;
        foreach (var page in pages)
        {
            for (var i = 0; i < page.Lines.Count; i++)
            {
                var line = page.Lines[i];
                var text = Clean(line.Text);
                if (text.StartsWith("Patient:", StringComparison.OrdinalIgnoreCase))
                {
                    patient = new ParsedPatient { PatientName = Match(text, @"Patient:\s*(.+)$") };
                    result.Patients.Add(patient);
                    continue;
                }
                if (patient != null && text.StartsWith("Patient Acct", StringComparison.OrdinalIgnoreCase))
                {
                    patient.PatientNo = Match(text, @"Patient\s*Acct\s*#:\s*([^\s]+)");
                    patient.ICN = Match(text, @"Claim\s*#:\s*([^\s]+)");
                    continue;
                }
                if (patient != null && text.StartsWith("Insured ID:", StringComparison.OrdinalIgnoreCase))
                {
                    patient.MID = Match(text, @"Insured\s*ID:\s*([^\s]+)");
                    continue;
                }
                if (patient != null && text.StartsWith("Totals ", StringComparison.OrdinalIgnoreCase))
                {
                    ParsePatientTotals(line, patient);
                    continue;
                }
                if (patient != null && TryParseProcedure(line, out var procedure)) patient.Procedures.Add(procedure);
            }
        }

        result.Patients = MergeRepeatedClaims(result.Patients);
        result.DocumentTotals = BuildDocumentTotals(result, pages);
        if (result.Patients.Count == 0) result.Warnings.Add("Zelis claims were not found.");
        if (result.Patients.Any(x => x.Procedures.Count == 0)) result.Warnings.Add("One or more Zelis claims contain no service lines.");
        return result;
    }

    private static void ParseHeader(PdfPageLines page, ParsedEobHeader result)
    {
        var text = page.FullText;
        result.CheckEFT = Match(text, @"Payment\s*ID:\s*([^\s]+)");
        // Some Zelis PDFs render the heading as repeated glyphs. The labelled
        // Date line remains stable and is a safer format-independent anchor.
        result.StatementDate = page.Lines
            .Select(x => Clean(x.Text))
            .Where(x => x.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
            .Select(x => Match(x, @"Date:\s*(\d{1,2}/\d{1,2}/\d{2,4})"))
            .FirstOrDefault(x => x != null);
        result.StatementDateParsed = ServiceDateParser.Parse(result.StatementDate);
        result.ProviderId = Match(text, @"Payee\s*ID:\s*([^\s]+)");
        result.ProviderName = Match(text, @"Payee:\s*(.+?)(?:\r?\n|Payer:)");
        result.InsuranceName = Match(text, @"Payer:\s*(.+?)(?:\r?\n|Payer\s*Ck)");
        result.TaxId = result.ProviderId;
    }

    private static bool TryParseProcedure(TextLine line, out ParsedProcedure procedure)
    {
        procedure = null!;
        var words = line.Words;
        if (words.Count < 8 || !Regex.IsMatch(words[0].Text, @"^\d{1,2}/\d{1,2}/\d{2,4}-\d{1,2}/\d{1,2}/\d{2,4}$")) return false;
        var proc = words.FirstOrDefault(w => w.Left >= 95 && w.Left < 155 && Regex.IsMatch(w.Text, @"^[A-Z0-9]{5}$"));
        if (proc == null) return false;
        var dateRange = words[0].Text;
        var date = dateRange.Split('-', 2)[0];
        var modifier = words.FirstOrDefault(w => w.Left > proc.Right && w.Left < 155 && Regex.IsMatch(w.Text, @"^[A-Z0-9]{2}$"));
        procedure = new ParsedProcedure
        {
            DateOfService = dateRange,
            DateOfServiceParsed = ServiceDateParser.Parse(date),
            ProcedureCode = proc.Text,
            Modifiers = modifier?.Text,
            NOS = words.FirstOrDefault(w => w.Left >= 155 && w.Left < 200 && Decimal(w.Text).HasValue)?.Text,
            Billed = MoneyAt(words, 200, 265),
            ProviderDiscount = MoneyAt(words, 265, 320),
            OtherAdjustments = MoneyAt(words, 320, 370),
            Allowed = MoneyAt(words, 370, 435),
            PatientResponsibility = MoneyAt(words, 435, 485),
            ProviderPaid = MoneyAt(words, 485, 540)
        };
        var reasonText = string.Join(" ", words.Where(w => w.Left >= 540).Select(w => w.Text));
        AddReasonCodes(procedure, reasonText);
        return true;
    }

    private static void AddReasonCodes(ParsedProcedure procedure, string text)
    {
        foreach (Match match in Regex.Matches(text, @"\b(CO|OA|PR|PI)-?([A-Z0-9]+)\b", RegexOptions.IgnoreCase))
        {
            var code = $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value.ToUpperInvariant()}";
            if (procedure.GrpRows.Any(x => x.GRPCode == code)) continue;
            var amount = code.StartsWith("PR-", StringComparison.Ordinal) ? procedure.PatientResponsibility : procedure.ProviderDiscount;
            procedure.GrpRows.Add(new ParsedGrp { GRPCode = code, RcAmt = amount, Remark = text });
            if (code == "PR-2")
            {
                procedure.CoInsurance = procedure.PatientResponsibility;
            }
            else if (code == "PR-3") procedure.Copay = procedure.PatientResponsibility;
            else if (code == "PR-1") procedure.Deductible = procedure.PatientResponsibility;
        }
    }

    private static void ParsePatientTotals(TextLine line, ParsedPatient patient)
    {
        patient.Billed = MoneyAt(line.Words, 200, 265);
        patient.ProviderDiscount = MoneyAt(line.Words, 265, 320);
        patient.OtherAdjustments = MoneyAt(line.Words, 320, 370);
        patient.Allowed = MoneyAt(line.Words, 370, 435);
        patient.PatientResponsibility = MoneyAt(line.Words, 435, 485);
        patient.ProviderPaid = MoneyAt(line.Words, 485, 540);
        patient.Net = patient.ProviderPaid;
    }

    private static List<ParsedPatient> MergeRepeatedClaims(List<ParsedPatient> source)
    {
        var result = new List<ParsedPatient>();
        foreach (var item in source)
        {
            var existing = !string.IsNullOrWhiteSpace(item.ICN)
                ? result.FirstOrDefault(x => string.Equals(x.ICN, item.ICN, StringComparison.OrdinalIgnoreCase))
                : null;
            if (existing == null) { result.Add(item); continue; }
            foreach (var procedure in item.Procedures)
                if (!existing.Procedures.Any(x => x.ProcedureCode == procedure.ProcedureCode && x.DateOfService == procedure.DateOfService)) existing.Procedures.Add(procedure);
            existing.Billed ??= item.Billed;
            existing.Allowed ??= item.Allowed;
            existing.ProviderDiscount ??= item.ProviderDiscount;
            existing.OtherAdjustments ??= item.OtherAdjustments;
            existing.PatientResponsibility ??= item.PatientResponsibility;
            existing.ProviderPaid ??= item.ProviderPaid;
            existing.Net ??= item.Net;
        }
        return result;
    }

    private static ParsedDocumentTotals BuildDocumentTotals(ParsedEobHeader result, IReadOnlyList<PdfPageLines> pages)
    {
        var totalLine = pages[0].Lines.FirstOrDefault(x => x.Text.TrimStart().StartsWith("Total Payment:", StringComparison.OrdinalIgnoreCase));
        var check = totalLine == null ? null : Money(totalLine.Words.LastOrDefault(w => Money(w.Text).HasValue)?.Text);
        return new ParsedDocumentTotals
        {
            ClaimCount = result.Patients.Count,
            Billed = result.Patients.Sum(x => x.Billed ?? 0m),
            Allowed = result.Patients.Sum(x => x.Allowed ?? 0m),
            ProviderDiscount = result.Patients.Sum(x => x.ProviderDiscount ?? 0m),
            Adjustment = result.Patients.Sum(x => x.OtherAdjustments ?? 0m),
            RC = result.Patients.Sum(x => x.PatientResponsibility ?? 0m),
            ProviderPaid = check ?? result.Patients.Sum(x => x.ProviderPaid ?? 0m),
            CheckAmount = check
        };
    }

    private static decimal? MoneyAt(IReadOnlyList<WordBox> words, double min, double max) => Money(words.FirstOrDefault(w => w.Left >= min && w.Left < max && Money(w.Text).HasValue)?.Text);
    private static decimal? Money(string? value) { var text = value?.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal); return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null; }
    private static decimal? Decimal(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static string? Match(string text, string pattern) { var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value.Trim() : null; }
    private static string Clean(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
}
