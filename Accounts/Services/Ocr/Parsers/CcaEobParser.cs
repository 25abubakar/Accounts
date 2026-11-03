using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

public sealed class CcaEobParser : ICcaEobParser
{
    private readonly IPdfTextExtractor _extractor;
    public CcaEobParser(IPdfTextExtractor extractor) => _extractor = extractor;

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes);
        var result = new ParsedEobHeader { Source = "CCA Docs", DocTitle = "CCA REMITTANCE ADVICE" };
        if (pages.Count == 0) return result;
        ParseHeader(pages[0], result);

        ParsedPatient? patient = null;
        ParsedProcedure? procedure = null;
        foreach (var page in pages)
        {
            for (var i = 0; i < page.Lines.Count; i++)
            {
                var line = page.Lines[i];
                var text = Clean(line.Text);
                if (text.StartsWith("Explanations", StringComparison.OrdinalIgnoreCase)) break;

                if (text.StartsWith("Claim Number:", StringComparison.OrdinalIgnoreCase))
                {
                    patient = ParsePatient(page.Lines, i);
                    procedure = null;
                    if (patient != null) result.Patients.Add(patient);
                    continue;
                }

                if (patient != null && text.StartsWith("Claim Total:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseClaimTotals(line, patient);
                    continue;
                }

                if (patient != null && TryParseProcedure(line, out var parsed))
                {
                    procedure = parsed;
                    patient.Procedures.Add(parsed);
                    continue;
                }

                if (procedure != null && !text.StartsWith("Service Date", StringComparison.OrdinalIgnoreCase))
                    AddReasonCodes(procedure, text);
            }
        }

        result.DocumentTotals = ParseDocumentTotals(pages);
        Validate(result);
        return result;
    }

    private static void ParseHeader(PdfPageLines page, ParsedEobHeader result)
    {
        var lines = page.Lines;
        var insuranceIndex = IndexOf(lines, x => x.Text.Contains("Central California Alliance for Health", StringComparison.OrdinalIgnoreCase));
        if (insuranceIndex >= 0)
        {
            result.InsuranceName = Clean(lines[insuranceIndex].Text);
            result.InsuranceAddress = JoinFollowing(lines, insuranceIndex + 1, 2);
        }

        var verifiedIndex = IndexOf(lines, x => x.Text.StartsWith("Your name", StringComparison.OrdinalIgnoreCase));
        if (verifiedIndex >= 4)
        {
            result.ProviderName = Clean(lines[verifiedIndex - 4].Text);
            result.ProviderAddress = JoinFollowing(lines, verifiedIndex - 3, 3);
        }

        var text = page.FullText;
        result.TaxId = Match(text, @"Tax\s*ID:\s*([A-Za-z0-9-]+)");
        result.CheckEFT = Match(text, @"EPC\s*Draft\s*#:\s*([A-Za-z0-9-]+)");
        result.PaymentWeek = Match(text, @"Payment\s*Week:\s*([^\s]+)");
        result.StatementDate = Match(text, @"Payment\s*Date:\s*(\d{1,2}/\d{1,2}/\d{2,4})");
        result.StatementDateParsed = ServiceDateParser.Parse(result.StatementDate);
    }

    private static ParsedPatient? ParsePatient(IReadOnlyList<TextLine> lines, int index)
    {
        var claimText = Clean(lines[index].Text);
        var identityText = index + 1 < lines.Count ? Clean(lines[index + 1].Text) : "";
        var accountText = index + 2 < lines.Count ? Clean(lines[index + 2].Text) : "";
        var claim = Match(claimText, @"Claim\s*Number:\s*([^\s]+)");
        var name = Match(identityText, @"Patient\s*Name:\s*(.+?)(?:\s+Subscriber\s*Name:|$)");
        if (claim == null && name == null) return null;
        return new ParsedPatient
        {
            PatientName = name,
            ICN = claim,
            PatientNo = Match(accountText, @"Patient\s*Acct\s*#:\s*([^\s]+)"),
            MID = Match(accountText, @"Subscriber\s*ID:\s*([^\s]+)")
        };
    }

    private static bool TryParseProcedure(TextLine line, out ParsedProcedure procedure)
    {
        procedure = null!;
        var words = line.Words;
        if (words.Count < 8 || !Regex.IsMatch(words[0].Text, @"^\d{1,2}/\d{1,2}/\d{2,4}$")) return false;
        var procWord = words.FirstOrDefault(w => w.Left >= 90 && w.Left < 125 && Regex.IsMatch(w.Text, @"^[A-Z0-9]{5}$"));
        if (procWord == null) return false;

        var modifier = words.FirstOrDefault(w => w.Left >= 115 && w.Left < 145 && w.Text.StartsWith('('));
        var units = words.FirstOrDefault(w => w.Left >= 140 && w.Left < 168 && Decimal(w.Text).HasValue);
        procedure = new ParsedProcedure
        {
            DateOfService = words[0].Text,
            DateOfServiceParsed = ServiceDateParser.Parse(words[0].Text),
            ProcedureCode = procWord.Text,
            Modifiers = modifier?.Text.Trim('(', ')'),
            NOS = units?.Text,
            NOS_Val = units == null ? null : Decimal(units.Text),
            Billed = MoneyAt(words, 225, 270),
            Allowed = MoneyAt(words, 270, 315),
            ProviderDiscount = MoneyAt(words, 315, 350),
            OtherPlan = MoneyAt(words, 350, 395),
            OtherAdjustments = MoneyAt(words, 395, 430),
            CoInsurance = MoneyAt(words, 430, 458),
            Copay = MoneyAt(words, 458, 488),
            Deductible = MoneyAt(words, 488, 518),
            NonCovered = MoneyAt(words, 518, 550),
            ProviderPaid = MoneyAt(words, 550, 612)
        };
        AddExplanationCodes(procedure, words.Where(w => w.Left >= 168 && w.Left < 225).Select(w => w.Text));
        return true;
    }

    private static void ParseClaimTotals(TextLine line, ParsedPatient patient)
    {
        patient.Billed = MoneyAt(line.Words, 225, 270);
        patient.Allowed = MoneyAt(line.Words, 270, 315);
        patient.ProviderDiscount = MoneyAt(line.Words, 315, 350);
        patient.OtherPlanPayment = MoneyAt(line.Words, 350, 395);
        patient.OtherAdjustments = MoneyAt(line.Words, 395, 430);
        patient.CoInsurance = MoneyAt(line.Words, 430, 458);
        var copay = MoneyAt(line.Words, 458, 488) ?? 0m;
        patient.Deductible = MoneyAt(line.Words, 488, 518);
        patient.NonCovered = MoneyAt(line.Words, 518, 550);
        patient.ProviderPaid = MoneyAt(line.Words, 550, 612);
        patient.PatientResponsibility = (patient.CoInsurance ?? 0m) + copay + (patient.Deductible ?? 0m) + (patient.NonCovered ?? 0m);
        patient.Net = patient.ProviderPaid;
    }

    private static ParsedDocumentTotals? ParseDocumentTotals(IReadOnlyList<PdfPageLines> pages)
    {
        foreach (var page in pages)
        {
            for (var i = 0; i < page.Lines.Count; i++)
            {
                var line = page.Lines[i];
                if (line.Words.Count < 7 || line.Words[0].Left < 200) continue;
                var money = line.Words.Where(w => Money(w.Text).HasValue).ToList();
                if (money.Count != 7) continue;
                if (!page.Lines.Skip(Math.Max(0, i - 4)).Take(5).Any(x => x.Text.Contains("Statement Summary", StringComparison.OrdinalIgnoreCase))) continue;
                return new ParsedDocumentTotals
                {
                    ClaimCount = null,
                    Billed = Money(money[0].Text),
                    Allowed = Money(money[1].Text),
                    ProviderDiscount = Money(money[2].Text),
                    OtherPlanPayment = Money(money[3].Text),
                    Adjustment = Money(money[4].Text),
                    RC = Money(money[5].Text),
                    ProviderPaid = Money(money[6].Text),
                    CheckAmount = Money(money[6].Text)
                };
            }
        }
        return null;
    }

    private static void AddReasonCodes(ParsedProcedure procedure, string text)
    {
        // Adjustment group codes always have a numeric reason code. Requiring the
        // suffix prevents ordinary footer prose from becoming false CO/PR codes.
        foreach (Match match in Regex.Matches(text, @"(?:^|[^A-Z])(CO|OA|PR|PI)-?(\d{1,4})(?=(?:CO|OA|PR|PI)|[^A-Z0-9]|$)", RegexOptions.IgnoreCase))
        {
            var code = $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value.ToUpperInvariant()}";
            if (procedure.GrpRows.All(x => !string.Equals(x.GRPCode, code, StringComparison.OrdinalIgnoreCase)))
                procedure.GrpRows.Add(new ParsedGrp { GRPCode = code, Remark = text });
        }
    }

    private static void AddExplanationCodes(ParsedProcedure procedure, IEnumerable<string> values)
    {
        var text = string.Join(" ", values);
        foreach (Match match in Regex.Matches(text, @"([A-Z]{1,3})-?(\d{1,4})", RegexOptions.IgnoreCase))
        {
            var code = $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}";
            if (procedure.GrpRows.All(x => !string.Equals(x.GRPCode, code, StringComparison.OrdinalIgnoreCase)))
                procedure.GrpRows.Add(new ParsedGrp { GRPCode = code, Remark = text });
        }
    }

    private static void Validate(ParsedEobHeader result)
    {
        if (result.Patients.Count == 0) result.Warnings.Add("CCA claims were not found.");
        if (result.Patients.Any(x => x.Procedures.Count == 0)) result.Warnings.Add("One or more CCA claims contain no service lines.");
        if (result.DocumentTotals == null) result.Warnings.Add("CCA statement totals were not found.");
        if (result.DocumentTotals != null && result.DocumentTotals.ClaimCount == null) result.DocumentTotals.ClaimCount = result.Patients.Count;
    }

    private static decimal? MoneyAt(IReadOnlyList<WordBox> words, double min, double max) =>
        Money(words.FirstOrDefault(w => w.Left >= min && w.Left < max && Money(w.Text).HasValue)?.Text);
    private static decimal? Money(string? value) => Decimal(value?.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal));
    private static decimal? Decimal(string? value) => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static string? Match(string text, string pattern) { var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value.Trim() : null; }
    private static string Clean(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
    private static int IndexOf(IReadOnlyList<TextLine> lines, Func<TextLine, bool> predicate) { for (var i = 0; i < lines.Count; i++) if (predicate(lines[i])) return i; return -1; }
    private static string? JoinFollowing(IReadOnlyList<TextLine> lines, int start, int count) { var value = string.Join(", ", lines.Skip(start).Take(count).Select(x => Clean(x.Text))); return string.IsNullOrWhiteSpace(value) ? null : value; }
}
