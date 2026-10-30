using Accounts.Services.Ocr.HealthNet.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Accounts.Services.Ocr.HealthNet
{
    /// <summary>
    /// Coordinate-based fallback for two-column Health Net remittance headers
    /// (labels on the right, values to the right of labels; payer/provider on the left).
    /// Does not overwrite fields already populated by the line-based parser.
    /// </summary>
    internal static class HealthNetHeaderCoordinateExtractor
    {
        private const double RowYTolerance = 4.0;
        private const double ValueGapTolerance = 28.0;

        public static void TryFillMissingFields(
            HealthNetPageData page,
            HealthNetDocumentHeaderModel header)
        {
            var words = GetHeaderZoneWords(page);
            if (words.Count == 0)
                return;

            var pageWidth = page.PageWidth > 0
                ? page.PageWidth
                : words.Max(w => w.Right);

            var columnSplit = pageWidth * 0.40;
            var rightWords = words
                .Where(w => MidX(w) >= columnSplit)
                .ToList();
            var leftWords = words
                .Where(w => MidX(w) < columnSplit)
                .ToList();

            if (!header.ProcessDate.HasValue)
            {
                header.ProcessDate =
                    TryParseDateRightOfLabel(
                        rightWords,
                        new[] { "PROCESS", "DATE" });
            }

            if (string.IsNullOrWhiteSpace(header.RemittanceNo))
            {
                header.RemittanceNo =
                    TryExtractScalarRightOfLabel(
                        rightWords,
                        new[] { "REMITTANCE", "#" })
                    ?? TryExtractScalarRightOfLabel(
                        rightWords,
                        new[] { "REMITTANCE" });
            }

            if (string.IsNullOrWhiteSpace(header.PayeeNo))
            {
                header.PayeeNo =
                    TryExtractScalarRightOfLabel(
                        rightWords,
                        new[] { "PAYEE", "#" })
                    ?? TryExtractScalarRightOfLabel(
                        rightWords,
                        new[] { "PAYEE" });
            }

            if (string.IsNullOrWhiteSpace(header.ServiceProviderNo))
            {
                header.ServiceProviderNo =
                    TryExtractScalarRightOfLabel(
                        rightWords,
                        new[] { "SVC", "PROVIDER", "#" })
                    ?? TryExtractScalarRightOfLabel(
                        rightWords,
                        new[] { "SVC", "PROVIDER", "NO" });
            }

            if (string.IsNullOrWhiteSpace(header.ServiceProviderName))
            {
                header.ServiceProviderName =
                    TryExtractSvcProviderName(rightWords);
            }

            if (string.IsNullOrWhiteSpace(header.InsuranceAddress))
            {
                TryExtractInsuranceAddress(leftWords, header);
            }

            if (string.IsNullOrWhiteSpace(header.ProviderAddress))
            {
                TryExtractProviderAddress(
                    leftWords,
                    rightWords,
                    header);
            }
        }

        private static List<HealthNetWordModel> GetHeaderZoneWords(
            HealthNetPageData page)
        {
            var all = (page.Words ?? new List<HealthNetWordModel>())
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .ToList();

            if (all.Count == 0
                && page.Lines != null)
            {
                all = page.Lines
                    .Where(l => l.Words != null)
                    .SelectMany(l => l.Words!)
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                    .ToList();
            }

            if (all.Count == 0)
                return all;

            double? floorY = null;

            /*
             * Do NOT cut at EXPLANATION OF PAYMENT â€” on denial/remittance
             * layouts, remittance labels/values often sit at or below that
             * title. Only exclude claim body below PATIENT NAME.
             */
            foreach (var line in page.Lines ?? new List<HealthNetTextLineModel>())
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                    continue;

                if (line.Text.Contains(
                        "PATIENT NAME",
                        StringComparison.OrdinalIgnoreCase))
                {
                    floorY = (line.Top + line.Bottom) / 2.0;
                    break;
                }
            }

            if (!floorY.HasValue)
                return all;

            return all
                .Where(w => MidY(w) > floorY.Value + 2.0)
                .ToList();
        }

        private static DateTime? TryParseDateRightOfLabel(
            List<HealthNetWordModel> words,
            string[] labelTokens)
        {
            var valueText = CollectValueWordsRightOfLabel(
                words,
                labelTokens,
                stopAtNextLabel: true);

            if (string.IsNullOrWhiteSpace(valueText))
                return null;

            return HealthNetParsingHelper.ParseDate(valueText)
                ?? ParseNamedDate(valueText);
        }

        private static DateTime? ParseNamedDate(string text)
        {
            if (DateTime.TryParse(
                    text.Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var date))
            {
                return date.Date;
            }

            if (DateTime.TryParse(
                    text.Trim(),
                    CultureInfo.GetCultureInfo("en-US"),
                    DateTimeStyles.AllowWhiteSpaces,
                    out date))
            {
                return date.Date;
            }

            return null;
        }

        private static string? TryExtractScalarRightOfLabel(
            List<HealthNetWordModel> words,
            string[] labelTokens)
        {
            var valueText = CollectValueWordsRightOfLabel(
                words,
                labelTokens,
                stopAtNextLabel: true);

            if (string.IsNullOrWhiteSpace(valueText))
                return null;

            valueText = valueText.Trim();

            if (LooksLikeKnownRightColumnLabel(valueText))
                return null;

            return valueText;
        }

        private static string? TryExtractSvcProviderName(
            List<HealthNetWordModel> words)
        {
            var rows = ClusterRows(words);
            var labelRows = new List<(double Y, List<HealthNetWordModel> Row)>();

            foreach (var row in rows)
            {
                if (!RowContainsTokens(row, "SVC", "PROVIDER"))
                    continue;

                if (RowContainsHash(row))
                    continue;

                labelRows.Add((RowMidY(row), row));
            }

            if (labelRows.Count == 0)
                return null;

            var anchor = labelRows
                .OrderByDescending(x => x.Y)
                .First();

            var labelRight = anchor.Row.Max(w => w.Right);
            var anchorY = anchor.Y;

            var nameWords = words
                .Where(w =>
                {
                    var y = MidY(w);
                    if (Math.Abs(y - anchorY) > RowYTolerance)
                        return false;

                    return w.Left > labelRight + 2;
                })
                .OrderBy(w => w.Left)
                .ToList();

            if (nameWords.Count == 0)
            {
                var rowBelow = rows
                    .Where(r =>
                        RowMidY(r) < anchorY - RowYTolerance
                        && RowMidY(r) >= anchorY - RowYTolerance * 5)
                    .OrderByDescending(RowMidY)
                    .FirstOrDefault();

                if (rowBelow == null)
                    return null;

                nameWords = rowBelow
                    .Where(w => MidX(w) < words.Max(x => x.Right) * 0.75)
                    .OrderBy(w => w.Left)
                    .ToList();
            }

            var text = string.Join(
                " ",
                nameWords.Select(w => w.Text.Trim()));

            return string.IsNullOrWhiteSpace(text)
                ? null
                : text.Trim();
        }

        private static void TryExtractInsuranceAddress(
            List<HealthNetWordModel> leftWords,
            HealthNetDocumentHeaderModel header)
        {
            if (leftWords.Count == 0)
                return;

            var rows = ClusterRows(leftWords);
            var lines = new List<string>();

            foreach (var row in rows
                .OrderByDescending(RowMidY)
                .Take(12))
            {
                var text = string.Join(
                    " ",
                    row.OrderBy(w => w.Left).Select(w => w.Text.Trim()));

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (LineLooksLikeHealthNetBrand(text)
                    || text.Contains(
                        "CALVIVA",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsInsuranceAddressLine(text))
                    continue;

                lines.Add(text.Trim());

                if (lines.Count >= 3)
                    break;
            }

            if (lines.Count == 0)
                return;

            lines.Reverse();
            foreach (var line in lines)
                header.InsuranceAddressLines.Add(line);
        }

        private static void TryExtractProviderAddress(
            List<HealthNetWordModel> leftWords,
            List<HealthNetWordModel> rightWords,
            HealthNetDocumentHeaderModel header)
        {
            if (leftWords.Count == 0)
                return;

            var insuranceYs = leftWords
                .Where(w =>
                    header.InsuranceAddressLines.Any(a =>
                        a.Contains(w.Text, StringComparison.OrdinalIgnoreCase)))
                .Select(MidY)
                .ToList();

            var insuranceBottom = insuranceYs.Count > 0
                ? insuranceYs.Min()
                : leftWords.Max(MidY);

            var providerRows = ClusterRows(leftWords)
                .Where(r =>
                {
                    var y = RowMidY(r);
                    return y < insuranceBottom - RowYTolerance * 2;
                })
                .OrderByDescending(RowMidY)
                .ToList();

            var collected = new List<string>();

            foreach (var row in providerRows)
            {
                var text = string.Join(
                    " ",
                    row.OrderBy(w => w.Left).Select(w => w.Text.Trim()));

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (IsInsuranceAddressLine(text)
                    || LineLooksLikeHealthNetBrand(text))
                {
                    continue;
                }

                if (LooksLikeKnownRightColumnLabel(text))
                    continue;

                if (text.Contains(
                        "PROCESS DATE",
                        StringComparison.OrdinalIgnoreCase)
                    || text.Contains(
                        "REMITTANCE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                collected.Add(text.Trim());

                if (collected.Count >= 6)
                    break;
            }

            if (collected.Count == 0)
                return;

            collected.Reverse();
            foreach (var line in collected)
                header.ProviderAddressLines.Add(line);
        }

        private static string? CollectValueWordsRightOfLabel(
            List<HealthNetWordModel> words,
            string[] labelTokens,
            bool stopAtNextLabel)
        {
            var anchor = FindLabelAnchor(words, labelTokens);
            if (anchor == null)
                return null;

            var labelRight = anchor.Value.LabelRight;
            var anchorY = anchor.Value.Y;

            var candidates = words
                .Where(w =>
                {
                    if (Math.Abs(MidY(w) - anchorY) > RowYTolerance)
                        return false;

                    return w.Left > labelRight + 1.5;
                })
                .OrderBy(w => w.Left)
                .ToList();

            if (candidates.Count == 0)
            {
                var rowBelow = ClusterRows(words)
                    .Where(r =>
                        Math.Abs(RowMidY(r) - anchorY) > RowYTolerance
                        && RowMidY(r) < anchorY - RowYTolerance
                        && RowMidY(r) >= anchorY - RowYTolerance * 4)
                    .OrderByDescending(RowMidY)
                    .FirstOrDefault();

                if (rowBelow == null)
                    return null;

                candidates = rowBelow
                    .Where(w => w.Left > labelRight - 40)
                    .OrderBy(w => w.Left)
                    .ToList();
            }

            var parts = new List<string>();
            double? lastRight = null;

            foreach (var w in candidates)
            {
                if (stopAtNextLabel
                    && parts.Count > 0
                    && lastRight.HasValue
                    && w.Left - lastRight.Value > ValueGapTolerance)
                {
                    break;
                }

                var token = w.Text.Trim();
                if (IsLabelNoiseToken(token))
                    continue;

                if (parts.Count == 0
                    && LooksLikeKnownRightColumnLabel(token))
                {
                    continue;
                }

                parts.Add(token);
                lastRight = w.Right;

                if (stopAtNextLabel
                    && parts.Count >= 1
                    && labelTokens[0] == "REMITTANCE"
                    && !token.Contains('#'))
                {
                    if (Regex.IsMatch(token, @"^[A-Za-z0-9\-]+$"))
                        break;
                }
            }

            return parts.Count == 0
                ? null
                : string.Join(" ", parts);
        }

        private static (double Y, double LabelRight)? FindLabelAnchor(
            List<HealthNetWordModel> words,
            string[] labelTokens)
        {
            var rows = ClusterRows(words);

            foreach (var row in rows.OrderByDescending(RowMidY))
            {
                if (!RowContainsTokens(row, labelTokens))
                    continue;

                if (labelTokens.Contains("#")
                    && !RowContainsHash(row)
                    && labelTokens.Contains("PROVIDER"))
                {
                    continue;
                }

                if (!labelTokens.Contains("#")
                    && labelTokens.Contains("PROVIDER")
                    && RowContainsHash(row))
                {
                    continue;
                }

                var labelWords = MatchLabelWords(row, labelTokens);
                if (labelWords.Count == 0)
                    continue;

                return (
                    RowMidY(row),
                    labelWords.Max(w => w.Right));
            }

            return null;
        }

        private static List<HealthNetWordModel> MatchLabelWords(
            List<HealthNetWordModel> row,
            string[] labelTokens)
        {
            var ordered = row.OrderBy(w => w.Left).ToList();
            var matched = new List<HealthNetWordModel>();
            int tokenIndex = 0;

            foreach (var word in ordered)
            {
                var norm = NormalizeToken(word.Text);
                if (tokenIndex >= labelTokens.Length)
                    break;

                var expected = labelTokens[tokenIndex];
                if (expected == "#")
                {
                    if (norm == "#" || norm.EndsWith("#", StringComparison.Ordinal))
                    {
                        matched.Add(word);
                        tokenIndex++;
                    }

                    continue;
                }

                if (norm == expected
                    || norm.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(word);
                    tokenIndex++;
                }
            }

            return tokenIndex >= labelTokens.Length
                ? matched
                : new List<HealthNetWordModel>();
        }

        private static List<List<HealthNetWordModel>> ClusterRows(
            List<HealthNetWordModel> words)
        {
            var sorted = words
                .OrderByDescending(MidY)
                .ThenBy(MidX)
                .ToList();

            var rows = new List<List<HealthNetWordModel>>();
            List<HealthNetWordModel>? current = null;
            double? currentY = null;

            foreach (var w in sorted)
            {
                var y = MidY(w);
                if (current == null
                    || !currentY.HasValue
                    || Math.Abs(y - currentY.Value) > RowYTolerance)
                {
                    current = new List<HealthNetWordModel> { w };
                    rows.Add(current);
                    currentY = y;
                }
                else
                {
                    current.Add(w);
                }
            }

            return rows;
        }

        private static bool RowContainsTokens(
            List<HealthNetWordModel> row,
            params string[] tokens)
        {
            var norms = row
                .Select(w => NormalizeToken(w.Text))
                .ToList();

            int found = 0;
            foreach (var token in tokens)
            {
                if (token == "#")
                {
                    if (norms.Any(n => n == "#" || n.EndsWith("#")))
                        found++;
                    continue;
                }

                if (norms.Any(n =>
                        n == token
                        || n.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
                {
                    found++;
                }
            }

            return found >= tokens.Length;
        }

        private static bool RowContainsHash(List<HealthNetWordModel> row)
        {
            return row.Any(w =>
            {
                var n = NormalizeToken(w.Text);
                return n == "#"
                    || n.EndsWith("#", StringComparison.Ordinal)
                    || w.Text.Contains('#');
            });
        }

        private static bool LooksLikeKnownRightColumnLabel(string text)
        {
            var upper = text.ToUpperInvariant();
            return upper.Contains("PROCESS DATE")
                || upper.Contains("REMITTANCE")
                || upper.Contains("PAYEE")
                || upper.Contains("SVC PROVIDER")
                || upper.StartsWith("PAGE ");
        }

        private static bool IsLabelNoiseToken(string token)
        {
            return token == "#"
                || token == ":"
                || string.Equals(token, "NO", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsuranceAddressLine(string text)
        {
            var upper = text.ToUpperInvariant();
            if (upper.Contains("PO BOX")
                || upper.Contains("P.O. BOX")
                || Regex.IsMatch(upper, @"\b[A-Z]{2}\s+\d{5}"))
            {
                return true;
            }

            return Regex.IsMatch(
                upper,
                @"\b\d{5}(?:-\d{4})?\b")
                && upper.Any(char.IsLetter);
        }

        private static bool LineLooksLikeHealthNetBrand(string text)
        {
            var upper = text.ToUpperInvariant();
            return upper.Contains("HEALTH NET")
                || upper.Contains("HEALTHNET");
        }

        private static string NormalizeToken(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim().TrimEnd('.', ',', ':').ToUpperInvariant();
        }

        private static double MidX(HealthNetWordModel w) =>
            (w.Left + w.Right) / 2.0;

        private static double MidY(HealthNetWordModel w) =>
            (w.Top + w.Bottom) / 2.0;

        private static double RowMidY(List<HealthNetWordModel> row) =>
            row.Average(MidY);
    }
}
