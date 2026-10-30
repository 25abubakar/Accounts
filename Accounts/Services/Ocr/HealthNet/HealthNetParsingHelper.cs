using Accounts.Services.Ocr.HealthNet.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Accounts.Services.Ocr.HealthNet
{
    public static class HealthNetParsingHelper
    {
        private static readonly string[] DateFormats =
        {
            "MM/dd/yyyy",
            "MM/dd/yy",
            "M/d/yyyy",
            "M/d/yy",
            "MM-dd-yyyy",
            "MM-dd-yy",
            "M-d-yyyy",
            "M-d-yy"
        };

        public static readonly Regex DateRegex =
            new Regex(
                @"\b\d{1,2}[\/\-]\d{1,2}[\/\-]\d{2,4}\b",
                RegexOptions.Compiled);

        public static readonly Regex MoneyRegex =
            new Regex(
                @"(?<!\d)\(?-?\$?(?:\d[\d,]*)?\.\d{2}\)?",
                RegexOptions.Compiled);

        /// <summary>
        /// Normalize multiline / OCR table headers for column matching.
        /// Example: "COB\nAllowed\nAmount\n($)" → "COB ALLOWED AMOUNT"
        /// </summary>
        public static string NormalizeTableHeader(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var upper = text.ToUpperInvariant();
            upper = upper.Replace('\r', ' ').Replace('\n', ' ');
            upper = System.Text.RegularExpressions.Regex.Replace(
                upper,
                @"[\$\(\)\.]",
                " ");
            upper = System.Text.RegularExpressions.Regex.Replace(
                upper,
                @"\s+",
                " ")
                .Trim();

            upper = System.Text.RegularExpressions.Regex.Replace(
                upper,
                @"\bAMT\b",
                "AMOUNT");
            upper = System.Text.RegularExpressions.Regex.Replace(
                upper,
                @"\bRESPONSIBILITY\b",
                "RESP");
            upper = System.Text.RegularExpressions.Regex.Replace(
                upper,
                @"\bCOINSURANCE\b",
                "COINS");
            upper = System.Text.RegularExpressions.Regex.Replace(
                upper,
                @"\bDEDUCTIBLE\b",
                "DED");

            return upper;
        }

        /// <summary>
        /// Map a normalized header fragment to a money-column key.
        /// Returns null when the text is not a recognized money header.
        /// </summary>
        public static string? ClassifyMoneyHeaderKey(string? normalizedHeader)
        {
            if (string.IsNullOrWhiteSpace(normalizedHeader))
                return null;

            var n = NormalizeTableHeader(normalizedHeader);

            if (n.Contains("COB") && n.Contains("ALLOW"))
                return "COB_ALLOWED";

            if (n.Contains("COB") && n.Contains("PAID"))
                return "COB_PAID";

            if (n.Contains("NOT") && n.Contains("ALLOW"))
                return "NOT_ALLOWED";

            if ((n.Contains("BILLED") || n.Contains("BILL"))
                && !n.Contains("NOT"))
            {
                return "BILLED";
            }

            if (n.Contains("ALLOW")
                && !n.Contains("COB")
                && !n.Contains("NOT"))
            {
                return "ALLOWED";
            }

            if (n.Contains("COPAY")
                || n.Contains("COINS")
                || n.Contains("DED")
                || (n.Contains("PATIENT")
                    && (n.Contains("COPAY")
                        || n.Contains("COINS")
                        || n.Contains("DED"))))
            {
                return "COST_SHARE";
            }

            if ((n.Contains("TOTAL") && n.Contains("PATIENT") && n.Contains("RESP"))
                || (n.Contains("PATIENT") && n.Contains("RESP") && !n.Contains("COPAY"))
                || n.Contains("TOTAL PATIENT RESP"))
            {
                return "PT_RESP";
            }

            if (n.Contains("BENEFIT") || n == "PAYABLE")
                return "BENEFIT";

            return null;
        }

        public static string? GetValueAfterLabel(
            IReadOnlyList<HealthNetTextLineModel> lines,
            string label,
            params string[] stopLabels)
        {
            if (lines == null || lines.Count == 0)
                return null;

            var labelPattern =
                BuildFlexibleLabelRegexPattern(label);

            for (int i = 0; i < lines.Count; i++)
            {
                var text = lines[i].Text;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var hasLabel =
                    text.IndexOf(
                        label,
                        StringComparison.OrdinalIgnoreCase) >= 0
                    || (labelPattern != null
                        && Regex.IsMatch(
                            text,
                            labelPattern,
                            RegexOptions.IgnoreCase
                                | RegexOptions.CultureInvariant));

                if (!hasLabel)
                    continue;

                var matched =
                    TryReadValueAfterLabelOnLine(
                        text,
                        label,
                        labelPattern,
                        stopLabels);

                if (!string.IsNullOrWhiteSpace(matched))
                    return matched;

                var index = text.IndexOf(
                    label,
                    StringComparison.OrdinalIgnoreCase);

                if (index >= 0)
                {
                    var startIndex =
                        index + label.Length;

                    var value =
                        text.Substring(startIndex)
                            .Trim();

                    value = value.TrimStart(
                        ':',
                        '#',
                        '-',
                        ' ');

                    value = NormalizeExtractedFieldValue(
                        value,
                        stopLabels);

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                /*
                 * Label row only (e.g. "Check Number  Check Date  Page 1 of 6")
                 * with values on the next visual row.
                 */
                if (i + 1 < lines.Count)
                {
                    var nextValue =
                        lines[i + 1].Text?.Trim();

                    if (!string.IsNullOrWhiteSpace(nextValue)
                        && !LooksLikeLabel(nextValue))
                    {
                        var remittance =
                            ExtractRemittanceIdFromText(
                                StopAtNextLabel(
                                    nextValue,
                                    stopLabels));

                        if (!string.IsNullOrWhiteSpace(remittance))
                            return remittance;

                        var stopped =
                            NormalizeExtractedFieldValue(
                                nextValue,
                                stopLabels);

                        if (!string.IsNullOrWhiteSpace(stopped))
                            return stopped;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Remittance / check number (same field for Health Net EOP).
        /// </summary>
        public static string? ExtractRemittanceIdFromText(
            string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            foreach (var token in text
                .Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = token.Trim().TrimEnd(',', ';', '.');
                if (string.IsNullOrWhiteSpace(clean))
                    continue;

                if (DateRegex.IsMatch(clean))
                    continue;

                if (Regex.IsMatch(
                        clean,
                        @"^\d{5,14}$",
                        RegexOptions.CultureInvariant))
                {
                    return clean;
                }
            }

            var match = Regex.Match(
                text,
                @"(?<![\d/])(\d{5,14})(?!\d)",
                RegexOptions.CultureInvariant);

            if (match.Success
                && !DateRegex.IsMatch(match.Value))
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        public static string? TryParseRemittanceIdFromPageText(
            string? pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText))
                return null;

            var patterns = new[]
            {
                @"(?is)\bREMITTANCE\s*#?\s*(?:NUMBER|NO\.?)?\s*:?\s*(\d{5,14})",
                @"(?is)\bCHECK\s*(?:/EFT\s*)?(?:NUMBER|NO\.?|#)\s*:?\s*(\d{5,14})",
                @"(?is)\bEFT\s*(?:NUMBER|NO\.?)\s*:?\s*(\d{5,14})"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(
                    pageText,
                    pattern,
                    RegexOptions.CultureInvariant);

                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }

        public static DateTime? TryParseCheckOrProcessDateFromPageText(
            string? pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText))
                return null;

            var patterns = new[]
            {
                @"(?is)\bPROCESS\s*DATE\s*:?\s*(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{2,4})",
                @"(?is)\bCHECK\s*DATE\s*:?\s*(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{2,4})",
                @"(?is)\bPAYMENT\s*DATE\s*:?\s*(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{2,4})",
                @"(?is)\bDATE\s*OF\s*CHECK\s*:?\s*(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{2,4})"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(
                    pageText,
                    pattern,
                    RegexOptions.CultureInvariant);

                if (match.Success)
                {
                    var date = ParseDate(match.Groups[1].Value);
                    if (date.HasValue)
                        return date;
                }
            }

            return null;
        }

        private static string? TryReadValueAfterLabelOnLine(
            string text,
            string literalLabel,
            string? labelPattern,
            IEnumerable<string>? stopLabels)
        {
            if (labelPattern == null)
                return null;

            var match = Regex.Match(
                text,
                labelPattern
                    + @"\s*:?\s*(.+)$",
                RegexOptions.IgnoreCase
                    | RegexOptions.CultureInvariant);

            if (!match.Success)
                return null;

            var value = NormalizeExtractedFieldValue(
                match.Groups[1].Value,
                stopLabels);

            if (!string.IsNullOrWhiteSpace(value))
            {
                var remittance =
                    ExtractRemittanceIdFromText(value);

                return remittance ?? value;
            }

            return null;
        }

        private static string? NormalizeExtractedFieldValue(
            string? value,
            IEnumerable<string>? stopLabels)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();
            value = StopAtNextLabel(value, stopLabels);

            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (LooksLikeAdjacentHeaderLabel(value))
                return null;

            var remittance = ExtractRemittanceIdFromText(value);
            return remittance ?? value.Trim();
        }

        private static string? BuildFlexibleLabelRegexPattern(
            string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return null;

            var parts = label
                .Trim()
                .Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(Regex.Escape);

            return @"\b" + string.Join(@"\s+", parts) + @"\b";
        }

        private static bool LooksLikeAdjacentHeaderLabel(
            string text)
        {
            var upper = CollapseWhitespace(text).ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(upper))
                return true;

            return upper.StartsWith("CHECK DATE", StringComparison.Ordinal)
                || upper.StartsWith("CHECK NUMBER", StringComparison.Ordinal)
                || upper.StartsWith("CHECK NO", StringComparison.Ordinal)
                || upper.StartsWith("PROCESS DATE", StringComparison.Ordinal)
                || upper.StartsWith("REMITTANCE", StringComparison.Ordinal)
                || upper.StartsWith("PAGE ", StringComparison.Ordinal)
                || upper.StartsWith("PAYEE", StringComparison.Ordinal)
                || upper == "CHECK"
                || upper == "DATE"
                || upper == "NUMBER";
        }

        private static string CollapseWhitespace(string text)
        {
            return Regex.Replace(
                text.Trim(),
                @"\s+",
                " ",
                RegexOptions.CultureInvariant);
        }

        public static string? GetValueAfterAnyLabel(
            IReadOnlyList<HealthNetTextLineModel> lines,
            IEnumerable<string> labels,
            params string[] stopLabels)
        {
            foreach (var label in labels)
            {
                var result =
                    GetValueAfterLabel(
                        lines,
                        label,
                        stopLabels);

                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }

            return null;
        }

        public static DateTime? ParseDate(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match =
                DateRegex.Match(value);

            if (match.Success)
            {
                if (DateTime.TryParseExact(
                    match.Value,
                    DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
                {
                    return date.Date;
                }

                if (DateTime.TryParse(
                    match.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date))
                {
                    return date.Date;
                }
            }

            /*
             * Receipt Date may be written as:
             * August 2, 2026
             */
            if (DateTime.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var namedDate))
            {
                return namedDate.Date;
            }

            return null;
        }

        public static List<DateTime> ExtractDates(
            string? text)
        {
            var result =
                new List<DateTime>();

            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (
                Match match in DateRegex.Matches(text))
            {
                var date =
                    ParseDate(match.Value);

                if (date.HasValue)
                    result.Add(date.Value);
            }

            return result;
        }

        public static decimal? ParseDecimal(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match =
                MoneyRegex.Match(value);

            if (!match.Success)
                return null;

            var clean =
                match.Value
                    .Replace("$", "")
                    .Replace(",", "")
                    .Trim();

            bool negative =
                clean.StartsWith("(") &&
                clean.EndsWith(")");

            clean =
                clean
                    .Replace("(", "")
                    .Replace(")", "");

            if (decimal.TryParse(
                clean,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var amount))
            {
                return negative
                    ? -amount
                    : amount;
            }

            return null;
        }

        public static List<decimal> ExtractMoneyValues(
            string? text)
        {
            var result =
                new List<decimal>();

            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (
                Match match in MoneyRegex.Matches(text))
            {
                var amount =
                    ParseDecimal(match.Value);

                if (amount.HasValue)
                    result.Add(amount.Value);
            }

            return result;
        }

        public static string CleanIdentifier(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Trim()
                .Trim(':', '#', '-', ',', ';');
        }

        private static string StopAtNextLabel(
            string value,
            IEnumerable<string>? stopLabels)
        {
            if (stopLabels == null)
                return value;

            var earliestIndex = -1;

            foreach (var stopLabel in stopLabels)
            {
                if (string.IsNullOrWhiteSpace(stopLabel))
                    continue;

                var index =
                    value.IndexOf(
                        stopLabel,
                        StringComparison.OrdinalIgnoreCase);

                if (index >= 0 &&
                    (earliestIndex < 0 ||
                     index < earliestIndex))
                {
                    earliestIndex = index;
                }
            }

            if (earliestIndex >= 0)
            {
                value =
                    value.Substring(
                        0,
                        earliestIndex);
            }

            return value.Trim();
        }

        private static bool LooksLikeLabel(
            string text)
        {
            var upper =
                text.ToUpperInvariant();

            return
                upper.Contains("PATIENT NAME") ||
                upper.Contains("YOUR ACCT") ||
                upper.Contains("SUBID") ||
                upper.Contains("CLAIM #") ||
                upper.Contains("PROCESS DATE") ||
                upper.Contains("CHECK DATE") ||
                upper.Contains("CHECK NUMBER") ||
                upper.Contains("REMITTANCE") ||
                upper.Contains("RECEIPT DATE") ||
                upper.Contains("CPT/REV") ||
                upper.Contains("BENEFIT PAYABLE");
        }
    }
}