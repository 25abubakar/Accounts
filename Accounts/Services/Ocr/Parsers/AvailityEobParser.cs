using System.Globalization;
using System.Text.RegularExpressions;
using Accounts.DTOs;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

/// <summary>
/// Parses the fixed-column Availity Remittance Notice. Availity prints several
/// logical fields on one text line, so the parser uses PDF X positions instead
/// of treating every numeric token as the next amount.
/// </summary>
public sealed class AvailityEobParser : IAvailityEobParser
{
    private readonly IPdfTextExtractor _extractor;

    public AvailityEobParser(IPdfTextExtractor extractor) => _extractor = extractor;

    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        var pages = _extractor.ExtractPages(pdfBytes, yTolerance: 3.5, xGap: 0.75);
        var header = new ParsedEobHeader { Source = "AVAILITY Docs" };
        if (pages.Count == 0) return header;

        ParseHeader(pages[0], header);
        ParsedPatient? patient = null;
        ParsedProcedure? procedure = null;

        foreach (var page in pages)
        {
            for (var index = 0; index < page.Lines.Count; index++)
            {
                var line = page.Lines[index];
                var text = Normalize(line.Text);

                if (IsPatientHeader(text))
                {
                    patient = ParsePatient(line);
                    procedure = null;
                    if (patient != null) header.Patients.Add(patient);
                    continue;
                }

                if (patient != null && text.StartsWith("PT RESP ", StringComparison.OrdinalIgnoreCase))
                {
                    ParsePatientTotals(line, patient);
                    continue;
                }

                if (patient != null && text.StartsWith("ADJ TO TOTALS", StringComparison.OrdinalIgnoreCase))
                {
                    ParsePatientAdjustments(line, patient);
                    continue;
                }

                if (patient != null && TryParseProcedure(line, out var parsedProcedure))
                {
                    procedure = parsedProcedure;
                    patient.Procedures.Add(procedure);
                    continue;
                }

                if (procedure != null && TryParseStandaloneGroup(line, out var group))
                {
                    procedure.GrpRows.Add(group);
                    ApplyGroupAmount(procedure, group);
                    continue;
                }

                if (text.StartsWith("TOTALS:", StringComparison.OrdinalIgnoreCase))
                {
                    var valuesLine = page.Lines.Skip(index + 1)
                        .FirstOrDefault(candidate => candidate.Words.Count >= 9 && candidate.Words.Any(w => IsMoney(w.Text)));
                    if (valuesLine != null) header.DocumentTotals = ParseDocumentTotals(valuesLine);
                }
            }
        }

        if (header.DocumentTotals != null)
            header.DocumentTotals.ProviderDiscount = header.Patients
                .SelectMany(p => p.Procedures)
                .Sum(p => p.ProviderDiscount ?? 0m);

        if (header.DocumentTotals == null) header.Warnings.Add("Availity document totals could not be read.");
        if (header.Patients.Count == 0) header.Warnings.Add("Availity patient claims could not be read.");
        return header;
    }

    private static void ParseHeader(PdfPageLines page, ParsedEobHeader header)
    {
        var lines = page.Lines;
        var fullText = page.FullText;
        header.DocTitle = fullText.Contains("AVAILITY", StringComparison.OrdinalIgnoreCase)
            ? "AVAILITY REMITTANCE NOTICE"
            : "REMITTANCE NOTICE";

        var addressLines = lines.Where(l => l.Bottom > page.Height * .86 && l.Left < page.Width * .45)
            .Select(l => JoinWords(l, 0, page.Width * .45))
            .Where(s => !string.IsNullOrWhiteSpace(s) && !Regex.IsMatch(s, @"^[pf]:", RegexOptions.IgnoreCase))
            .ToList();
        header.Address = NullIfEmpty(string.Join(", ", addressLines));
        header.Phone = MatchValue(fullText, @"\bp:\s*([\d() .-]{10,})");
        header.Fax = MatchValue(fullText, @"\bf:\s*([\d() .-]{10,})");
        header.CheckEFT = MatchValue(fullText, @"CHECK/EFT\s*#?:?\s*([A-Za-z0-9_-]+)");
        header.ProviderId = MatchValue(fullText, @"PROVIDER\s*#?:?\s*([A-Za-z0-9_-]+)");
        header.NPI = MatchValue(fullText, @"NPI\s*#?:?\s*(\d{10})");

        var checkLine = lines.FirstOrDefault(l => l.Text.Contains("CHECK/EFT", StringComparison.OrdinalIgnoreCase));
        var statementDate = checkLine?.Words.Select(w => w.Text)
            .FirstOrDefault(value => Regex.IsMatch(value, @"^\d{1,2}/\d{1,2}/\d{2,4}$"));
        header.StatementDate = statementDate;
        header.StatementDateParsed = ServiceDateParser.Parse(statementDate);

        var partyEnd = FindIndex(lines, l => l.Text.TrimStart().StartsWith("PROVIDER", StringComparison.OrdinalIgnoreCase));
        if (partyEnd >= 3)
        {
            var partyLines = lines.Skip(partyEnd - 3).Take(3).ToList();
            var splitX = page.Width / 2d;
            var insurer = partyLines.Select(l => JoinWords(l, 0, splitX)).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            var provider = partyLines.Select(l => JoinWords(l, splitX, page.Width)).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            header.InsuranceName = insurer.FirstOrDefault();
            header.InsuranceAddress = NullIfEmpty(string.Join(", ", insurer.Skip(1)));
            header.ProviderName = provider.FirstOrDefault();
            header.ProviderAddress = NullIfEmpty(string.Join(", ", provider.Skip(1)));
        }
    }

    private static bool IsPatientHeader(string text) =>
        Regex.IsMatch(text, @"^NAME\s*:?.*\bMID\s*:?.*\bACNT\s*:?.*\bICN\s*:?", RegexOptions.IgnoreCase);

    private static ParsedPatient? ParsePatient(TextLine line)
    {
        var match = Regex.Match(Normalize(line.Text),
            @"^NAME\s*:?[ ]*(?<name>.+?)[ ]+MID\s*:?[ ]*(?<mid>\S+)[ ]+ACNT\s*:?[ ]*(?<account>\S+)[ ]+ICN\s*:?[ ]*(?<icn>\S+)",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return new ParsedPatient
        {
            PatientName = match.Groups["name"].Value.Trim(),
            MID = match.Groups["mid"].Value.Trim(),
            PatientNo = match.Groups["account"].Value.Trim(),
            ICN = match.Groups["icn"].Value.Trim()
        };
    }

    private static bool TryParseProcedure(TextLine line, out ParsedProcedure procedure)
    {
        procedure = null!;
        var words = line.Words;
        if (words.Count < 8) return false;
        var procWord = words.FirstOrDefault(w => w.Left >= 175 && w.Left < 250 && Regex.IsMatch(w.Text, @"^[A-Z0-9]{5}$"));
        var posWord = words.FirstOrDefault(w => w.Left >= 130 && w.Left < 160 && Regex.IsMatch(w.Text, @"^\d{2}$"));
        if (procWord == null || MoneyAt(words, 250, 300) == null) return false;

        var serviceTokens = words.Where(w => w.Left >= 82 && w.Left < 130).OrderBy(w => w.Left).ToList();
        var dateToken = serviceTokens.LastOrDefault()?.Text;
        var groupWord = words.FirstOrDefault(w => w.Left >= 415 && w.Left < 455 && Regex.IsMatch(w.Text, @"^[A-Z]{2}-[A-Z0-9]+$"));
        procedure = new ParsedProcedure
        {
            RenderingProviderId = words[0].Left < 80 && Regex.IsMatch(words[0].Text, @"^\d{8,12}$") ? words[0].Text : null,
            DayMonth = serviceTokens.FirstOrDefault()?.Text,
            DateOfService = dateToken,
            DateOfServiceParsed = ServiceDateParser.Parse(dateToken),
            POS = posWord?.Text,
            NOS = WordAt(words, 155, 178)?.Text,
            ProcedureCode = procWord.Text,
            Modifiers = NullIfEmpty(string.Join(" ", words.Where(w => w.Left > procWord.Right && w.Left < 255).Select(w => w.Text))),
            Billed = MoneyAt(words, 250, 300),
            Allowed = MoneyAt(words, 300, 340),
            Deductible = MoneyAt(words, 340, 380),
            CoInsurance = MoneyAt(words, 380, 420),
            ProviderPaid = MoneyAt(words, 500, 550)
        };
        if (groupWord != null)
        {
            var group = new ParsedGrp { GRPCode = groupWord.Text, RcAmt = MoneyAt(words, 450, 500), Remark = groupWord.Text };
            procedure.GrpRows.Add(group);
            ApplyGroupAmount(procedure, group);
        }
        return true;
    }

    private static bool TryParseStandaloneGroup(TextLine line, out ParsedGrp group)
    {
        group = null!;
        if (line.Words.Count < 2 || line.Words[0].Left < 410) return false;
        if (!Regex.IsMatch(line.Words[0].Text, @"^[A-Z]{2}-[A-Z0-9]+$")) return false;
        if (!TryMoney(line.Words[1].Text, out var amount)) return false;
        group = new ParsedGrp { GRPCode = line.Words[0].Text, RcAmt = amount, Remark = Normalize(line.Text) };
        return true;
    }

    private static void ApplyGroupAmount(ParsedProcedure procedure, ParsedGrp group)
    {
        var parts = group.GRPCode?.Split('-', 2);
        var prefix = parts?.FirstOrDefault()?.ToUpperInvariant();
        var reason = parts?.ElementAtOrDefault(1);
        if (prefix == "PR")
        {
            procedure.PatientResponsibility = (procedure.PatientResponsibility ?? 0m) + (group.RcAmt ?? 0m);
            if (reason == "3") procedure.Copay = (procedure.Copay ?? 0m) + (group.RcAmt ?? 0m);
        }
        else if (prefix == "CO") procedure.ProviderDiscount = (procedure.ProviderDiscount ?? 0m) + (group.RcAmt ?? 0m);
        else if (prefix == "OA") procedure.OtherAdjustments = (procedure.OtherAdjustments ?? 0m) + (group.RcAmt ?? 0m);
    }

    private static void ParsePatientTotals(TextLine line, ParsedPatient patient)
    {
        patient.PatientResponsibility = MoneyAt(line.Words, 90, 150);
        patient.Billed = MoneyAt(line.Words, 250, 300);
        patient.Allowed = MoneyAt(line.Words, 300, 340);
        patient.Deductible = MoneyAt(line.Words, 340, 380);
        patient.CoInsurance = MoneyAt(line.Words, 380, 420);
        patient.RcAmount = MoneyAt(line.Words, 450, 500);
        patient.ProviderPaid = MoneyAt(line.Words, 500, 550);
        patient.ProviderDiscount = patient.Procedures.Sum(p => p.ProviderDiscount ?? 0m);
        patient.OtherAdjustments = patient.Procedures.Sum(p => p.OtherAdjustments ?? 0m);
    }

    private static void ParsePatientAdjustments(TextLine line, ParsedPatient patient)
    {
        patient.Interest = MoneyAt(line.Words, 250, 310);
        patient.LateFiling = MoneyAt(line.Words, 415, 470);
        patient.Net = MoneyAt(line.Words, 490, 550);
    }

    private static ParsedDocumentTotals? ParseDocumentTotals(TextLine line)
    {
        var words = line.Words;
        var claimWord = WordAt(words, 80, 130);
        if (claimWord == null || !int.TryParse(claimWord.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var claims)) return null;
        return new ParsedDocumentTotals
        {
            ClaimCount = claims,
            Billed = MoneyAt(words, 135, 190),
            Allowed = MoneyAt(words, 190, 240),
            Deductible = MoneyAt(words, 240, 290),
            CoInsurance = MoneyAt(words, 290, 340),
            RC = MoneyAt(words, 340, 400),
            ProviderPaid = MoneyAt(words, 400, 450),
            Adjustment = MoneyAt(words, 450, 500),
            CheckAmount = MoneyAt(words, 500, 550)
        };
    }

    private static WordBox? WordAt(IReadOnlyList<WordBox> words, double minX, double maxX) =>
        words.FirstOrDefault(w => w.Left >= minX && w.Left < maxX);

    private static decimal? MoneyAt(IReadOnlyList<WordBox> words, double minX, double maxX)
    {
        var word = words.FirstOrDefault(w => w.Left >= minX && w.Left < maxX && IsMoney(w.Text));
        return word != null && TryMoney(word.Text, out var value) ? value : null;
    }

    private static bool IsMoney(string value) => TryMoney(value, out _);

    private static bool TryMoney(string value, out decimal amount)
    {
        var text = value.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim();
        var negative = text.StartsWith('(') && text.EndsWith(')');
        text = text.Trim('(', ')');
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) return false;
        if (negative) amount = -amount;
        return true;
    }

    private static string JoinWords(TextLine line, double minX, double maxX) =>
        Normalize(string.Join(" ", line.Words.Where(w => w.Left >= minX && w.Left < maxX).Select(w => w.Text)));

    private static string? MatchValue(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int FindIndex(IReadOnlyList<TextLine> lines, Func<TextLine, bool> predicate)
    {
        for (var index = 0; index < lines.Count; index++) if (predicate(lines[index])) return index;
        return -1;
    }

    private static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ");
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
