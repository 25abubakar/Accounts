using Accounts.Services.Ocr.HealthNet.Models;
using System.Text.RegularExpressions;

namespace Accounts.Services.Ocr.HealthNet
{
    /// <summary>
    /// Parses Health Net service rows by:
    ///   1) DOS Y-band (row)
    ///   2) CenterX column bands (Modifier / money / Ex Code / QPA)
    /// so blank cells cannot shift values and column words cannot leak.
    /// </summary>
    public class HealthNetServiceLineParser
    {
        private static readonly Regex ProcedureCodeRegex =
            new Regex(@"^[A-Za-z0-9]{3,8}$", RegexOptions.Compiled);

        private static readonly Regex ModifierTokenRegex =
            new Regex(@"^[A-Za-z0-9]{1,4}$", RegexOptions.Compiled);

        private static readonly Regex ExCodeTokenRegex =
            new Regex(@"^[A-Za-z0-9]{2,24}$", RegexOptions.Compiled);

        private static readonly Regex WrappedCodeFragmentRegex =
            new Regex(@"^[A-Za-z0-9]{1,20}$", RegexOptions.Compiled);

        private static readonly Regex SubRowRemarkLeadRegex =
            new Regex(
                @"^([A-Za-z]{0,4}\d{2,6}|\d{2,5})\s*[-â€“â€”]\s*",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const double DefaultRowYTolerance = 4.5;

        public List<HealthNetClaimLineModel> Parse(
            HealthNetPageData page)
        {
            if (page == null)
                return new List<HealthNetClaimLineModel>();

            var topLines = GetLinesBeforePatientHeader(
                page.Lines ?? new List<HealthNetTextLineModel>());

            var pageWidth = page.PageWidth > 0
                ? page.PageWidth
                : topLines.Select(x => x.Right).DefaultIfEmpty(612).Max();

            var layout = HealthNetServiceColumnLayout.Detect(
                topLines,
                pageWidth);

            var words = GetWordsBeforePatientHeader(page, topLines);

            if (words.Count == 0)
                return new List<HealthNetClaimLineModel>();

#if DEBUG
            DumpCoordinateSample(page.PageNumber, words, layout);
#endif

            return ParseFromWords(
                words,
                page.PageNumber,
                layout);
        }

        /// <summary>
        /// Parse service lines inside one EOP / claim Y-bounded region.
        /// Column layout is detected from THIS block's table headers.
        /// </summary>
        public List<HealthNetClaimLineModel> Parse(
            HealthNetEopBlock block) => Parse(block, null);

        internal List<HealthNetClaimLineModel> Parse(
            HealthNetEopBlock block,
            HealthNetServiceColumnLayout? inheritedLayout)
        {
            if (block == null
                || (block.Words.Count == 0 && block.Lines.Count == 0))
            {
                return new List<HealthNetClaimLineModel>();
            }

            var pageWidth = block.PageWidth > 0
                ? block.PageWidth
                : block.Lines.Select(x => x.Right).DefaultIfEmpty(612).Max();

            var layout = HealthNetServiceColumnLayout.Detect(
                block.Lines,
                pageWidth);
            if (!layout.HasHeaderGeometry && inheritedLayout?.HasHeaderGeometry == true)
                layout = inheritedLayout;

            var words = block.Words.Count > 0
                ? block.Words
                : block.Lines
                    .Where(x => x.Words != null)
                    .SelectMany(x => x.Words!)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .ToList();

            if (words.Count == 0)
                return new List<HealthNetClaimLineModel>();

            return ParseFromWords(
                words,
                block.PageNumber,
                layout);
        }

        public List<HealthNetClaimLineModel> Parse(
            IReadOnlyList<HealthNetTextLineModel> lines,
            int pageNumber,
            double pageWidth = 0)
        {
            if (lines == null || lines.Count == 0)
                return new List<HealthNetClaimLineModel>();

            if (pageWidth <= 0)
            {
                pageWidth = lines
                    .Select(x => x.Right)
                    .DefaultIfEmpty(612)
                    .Max();
            }

            var layout = HealthNetServiceColumnLayout.Detect(
                lines,
                pageWidth);

            var words = lines
                .Where(x => x.Words != null)
                .SelectMany(x => x.Words!)
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (words.Count == 0)
                return new List<HealthNetClaimLineModel>();

            return ParseFromWords(words, pageNumber, layout);
        }

        internal static List<HealthNetTextLineModel>
            GetLinesBeforePatientHeader(
                IReadOnlyList<HealthNetTextLineModel> lines)
        {
            var result = new List<HealthNetTextLineModel>();

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line.Text)
                    && line.Text.Contains(
                        "PATIENT NAME",
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                result.Add(line);
            }

            return result;
        }

        private static List<HealthNetWordModel> GetWordsBeforePatientHeader(
            HealthNetPageData page,
            List<HealthNetTextLineModel> topLines)
        {
            var allWords = (page.Words ?? new List<HealthNetWordModel>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (allWords.Count == 0)
            {
                return topLines
                    .Where(x => x.Words != null)
                    .SelectMany(x => x.Words!)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .ToList();
            }

            var patientWord = allWords
                .OrderBy(MidY)
                .FirstOrDefault(x =>
                {
                    var nearby = allWords.Where(w =>
                        Math.Abs(MidY(w) - MidY(x)) <= 6);

                    var text = string.Join(
                        " ",
                        nearby.OrderBy(w => w.Left).Select(w => w.Text));

                    return text.Contains(
                        "PATIENT NAME",
                        StringComparison.OrdinalIgnoreCase);
                });

            if (patientWord == null)
                return allWords;

            var patientY = MidY(patientWord);

            return allWords
                .Where(x => MidY(x) > patientY + 2.0)
                .ToList();
        }

        private static List<HealthNetClaimLineModel> ParseFromWords(
            List<HealthNetWordModel> words,
            int pageNumber,
            HealthNetServiceColumnLayout layout)
        {
            var result = new List<HealthNetClaimLineModel>();

            var ordered = words
                .OrderByDescending(MidY)
                .ThenBy(x => x.Left)
                .ToList();

            var dateWords = ordered
                .Where(x => HealthNetParsingHelper.DateRegex.IsMatch(x.Text))
                .Where(x => !layout.HasHeaderGeometry || layout.Dos.Contains(MidX(x)))
                .ToList();

            if (dateWords.Count == 0)
                return result;

            var rowTolerance = Math.Max(EstimateRowYTolerance(ordered), 8.0);
            if (layout.HasHeaderGeometry)
            {
                var dateHeight = dateWords.Select(w => Math.Abs(w.Top-w.Bottom)).OrderBy(h => h).ToList();
                var medianDateHeight = dateHeight[dateHeight.Count/2];
                rowTolerance = Math.Max(EstimateRowYTolerance(ordered), medianDateHeight*1.2);
                var rowYs = dateWords.Select(MidY).OrderByDescending(y => y).ToList();
                var minSpacing = rowYs.Zip(rowYs.Skip(1), (a,b) => a-b)
                    .Where(d => d > medianDateHeight*0.5).DefaultIfEmpty(double.MaxValue).Min();
                rowTolerance = Math.Min(rowTolerance, minSpacing*0.45);
            }

            var rowAnchors = ClusterServiceRowAnchors(dateWords, rowTolerance);

            layout = HealthNetServiceColumnLayout.RefineFromSampleRows(
                layout,
                ordered,
                rowAnchors.Select(x => x.CenterY).ToList(),
                rowTolerance);

            int lineNo = 1;

            for (int i = 0; i < rowAnchors.Count; i++)
            {
                var anchor = rowAnchors[i];
                var nextAnchorY = i + 1 < rowAnchors.Count
                    ? rowAnchors[i + 1].CenterY
                    : (double?)null;

                var rowWords = ordered
                    .Where(x => Math.Abs(MidY(x) - anchor.CenterY) <= rowTolerance)
                    .OrderBy(x => x.Left)
                    .ToList();

                var parsed = BuildServiceLineFromRowWords(
                    rowWords,
                    pageNumber,
                    lineNo,
                    layout);

                if (parsed == null)
                    continue;

                /*
                 * Ex Code continuation (same X band only):
                 * collect every code/token belonging to this service row
                 * until the next DOS/service row begins.
                 * Never append Modifier / CPT / money / QPA (X-filtered).
                 */
                var financialFloorY = FindFinancialSectionTopY(
                    ordered,
                    anchor.CenterY);

                var continuationWords = ordered
                    .Where(x =>
                    {
                        var y = MidY(x);

                        if (y >= anchor.CenterY - 0.5)
                            return false;

                        if (nextAnchorY.HasValue
                            && y <= nextAnchorY.Value + rowTolerance)
                        {
                            return false;
                        }

                        /*
                         * Hard stop above Previously Paid / Overpayment /
                         * Total / legend so instruction text cannot
                         * leak into ExCodes when this is the last row.
                         */
                        if (financialFloorY.HasValue
                            && y <= financialFloorY.Value + 1.0)
                        {
                            return false;
                        }

                        if (IsFinancialBoundaryWord(x.Text)
                            || IsPatientBoundaryWord(x.Text)
                            || IsLegendOrInstructionWord(x.Text))
                        {
                            return false;
                        }

                        if (HealthNetParsingHelper.DateRegex.IsMatch(x.Text))
                            return false;

                        if (IsMoneyToken(x.Text))
                            return false;

                        var cx = MidX(x);

                        if (!layout.Explanation.Contains(cx))
                            return false;

                        if (layout.Modifier.Contains(cx)
                            || layout.Cpt.Contains(cx)
                            || layout.Qpa.Contains(cx)
                            || layout.Billed.Contains(cx)
                            || layout.NotAllowed.Contains(cx)
                            || layout.Allowed.Contains(cx)
                            || layout.CobAllowed.Contains(cx)
                            || layout.CobPaid.Contains(cx)
                            || layout.PatientCostShare.Contains(cx)
                            || layout.PatientResponsibility.Contains(cx)
                            || layout.BenefitPayable.Contains(cx))
                        {
                            return false;
                        }

                        return true;
                    })
                    .OrderByDescending(MidY)
                    .ThenBy(x => x.Left)
                    .ToList();

                ApplyExplanationContinuations(parsed, continuationWords);
                ApplySubRowRemarkExCodes(
                    parsed,
                    ordered,
                    anchor.CenterY,
                    nextAnchorY,
                    rowTolerance,
                    financialFloorY);

                result.Add(parsed);
                lineNo++;
            }

            return result;
        }

        private static void ApplyExplanationContinuations(
            HealthNetClaimLineModel line,
            List<HealthNetWordModel> continuationWords)
        {
            foreach (var word in continuationWords)
            {
                var text = word.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (LooksLikeExplanationCode(text)
                    || LooksLikeWrappedCodeFragment(text))
                {
                    AppendExCodeToken(line.ExCodes, text);
                }
            }
        }

        /// <summary>
        /// Medicare-style EOP prints remark codes on a full-width row under the
        /// service line (e.g. "MB01 - Medicare Deductible", "740 - Bundled code").
        /// Those glyphs are outside the Ex Code X-band and must still become GRP.
        /// </summary>
        private static void ApplySubRowRemarkExCodes(
            HealthNetClaimLineModel line,
            List<HealthNetWordModel> ordered,
            double anchorCenterY,
            double? nextAnchorCenterY,
            double rowTolerance,
            double? financialFloorY)
        {
            var bandWords = ordered
                .Where(x =>
                {
                    var y = MidY(x);

                    if (y >= anchorCenterY - 0.5)
                        return false;

                    if (nextAnchorCenterY.HasValue
                        && y <= nextAnchorCenterY.Value + rowTolerance)
                    {
                        return false;
                    }

                    if (financialFloorY.HasValue
                        && y <= financialFloorY.Value + 1.0)
                    {
                        return false;
                    }

                    if (HealthNetParsingHelper.DateRegex.IsMatch(x.Text))
                        return false;

                    if (IsMoneyToken(x.Text))
                        return false;

                    if (IsFinancialBoundaryWord(x.Text)
                        || IsPatientBoundaryWord(x.Text)
                        || IsLegendOrInstructionWord(x.Text))
                    {
                        return false;
                    }

                    return true;
                })
                .ToList();

            if (bandWords.Count == 0)
                return;

            var clusters = ClusterWordsByY(bandWords, Math.Min(rowTolerance, 5.0));

            foreach (var cluster in clusters)
            {
                var lineText = string.Join(
                    " ",
                    cluster.OrderBy(x => x.Left).Select(x => x.Text.Trim()));

                if (string.IsNullOrWhiteSpace(lineText))
                    continue;

                var match = SubRowRemarkLeadRegex.Match(lineText);
                if (!match.Success)
                    continue;

                AppendExCodeToken(line.ExCodes, match.Groups[1].Value);
            }
        }

        private static List<List<HealthNetWordModel>> ClusterWordsByY(
            List<HealthNetWordModel> words,
            double tolerance)
        {
            var sorted = words
                .OrderByDescending(MidY)
                .ThenBy(x => x.Left)
                .ToList();

            var clusters = new List<List<HealthNetWordModel>>();
            List<HealthNetWordModel>? current = null;
            double? currentY = null;

            foreach (var word in sorted)
            {
                var y = MidY(word);
                if (current == null
                    || !currentY.HasValue
                    || Math.Abs(y - currentY.Value) > tolerance)
                {
                    current = new List<HealthNetWordModel> { word };
                    clusters.Add(current);
                    currentY = y;
                }
                else
                {
                    current.Add(word);
                }
            }

            return clusters;
        }

        private static void AppendExCodeToken(
            List<string> exCodes,
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var remarkLead = SubRowRemarkLeadRegex.Match(text.Trim());
            if (remarkLead.Success)
            {
                text = remarkLead.Groups[1].Value;
            }

            /*
             * Source cells often print "CO96, CO234," as one glyph-run.
             */
            var parts = text
                .Split(new[] { ',', ';', ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var token = part.Trim().TrimEnd(',', ';', '.');
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                if (IsLegendOrInstructionWord(token)
                    || IsCommonEnglishNoise(token))
                {
                    continue;
                }

                if (exCodes.Count > 0
                    && token.Length <= 3
                    && !LooksLikeExplanationCode(token)
                    && LooksLikeWrappedCodeFragment(token))
                {
                    exCodes[exCodes.Count - 1] += token;
                    continue;
                }

                if (exCodes.Any(x =>
                        string.Equals(
                            x,
                            token,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (LooksLikeExplanationCode(token)
                    || (LooksLikeWrappedCodeFragment(token)
                        && token.Any(char.IsDigit)))
                {
                    exCodes.Add(token);
                }
            }
        }

        private static double? FindFinancialSectionTopY(
            List<HealthNetWordModel> ordered,
            double rowCenterY)
        {
            double? top = null;

            foreach (var word in ordered)
            {
                var y = MidY(word);
                if (y >= rowCenterY)
                    continue;

                if (!IsFinancialBoundaryWord(word.Text)
                    && !IsLegendOrInstructionWord(word.Text))
                {
                    continue;
                }

                if (!top.HasValue || y > top.Value)
                    top = y;
            }

            return top;
        }

        private static bool IsLegendOrInstructionWord(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var upper = text.ToUpperInvariant();
            return upper.Contains("PAYMENT/DENIAL")
                || upper.Contains("EXPLANATION CODE DESCRIPTION")
                || upper.Contains("ADJUSTMENT EXPLANATION")
                || upper.Contains("CALVIVA")
                || upper.Contains("CONTESTED")
                || upper.Contains("ABBREVIATION")
                || upper.Contains("DISPUTE")
                || upper.Contains("ACKNOWLEDG")
                || HealthNetNoisePolicy.IsExplanationLegendSection(text)
                || HealthNetNoisePolicy.IsProviderInstructionSection(text);
        }

        private static bool IsCommonEnglishNoise(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return true;

            var upper = token.Trim().ToUpperInvariant();
            if (upper.Length <= 1 && !char.IsDigit(upper[0]))
                return true;

            string[] noise =
            {
                "THE", "AND", "FOR", "BECAUSE", "SHOULD", "SHOULDBE",
                "ONLY", "PROVIDER", "PROVIDERS", "DISPUTES", "DISPUTE",
                "EXCEEDS", "DENIED", "DENIAL", "PAYMENT", "PAYMENTS",
                "ADJUSTMENT", "COMPRISED", "HEALTHCAREOF", "HEALTHCARE",
                "NCPDP", "OF", "TO", "OR", "IN", "ON", "AT", "BY",
                "THIS", "THAT", "WITH", "FROM", "WAS", "WERE", "ARE",
                "IS", "BE", "AS", "AN", "A"
            };

            return noise.Contains(upper);
        }

        private static HealthNetClaimLineModel? BuildServiceLineFromRowWords(
            List<HealthNetWordModel> rowWords,
            int pageNumber,
            int lineNo,
            HealthNetServiceColumnLayout layout)
        {
            if (rowWords.Count == 0)
                return null;

            var dateTokens = rowWords
                .Where(x => HealthNetParsingHelper.DateRegex.IsMatch(x.Text))
                .Where(x => !layout.HasHeaderGeometry || layout.Dos.Contains(MidX(x)))
                .OrderBy(x => x.Left)
                .Take(2)
                .ToList();

            if (dateTokens.Count == 0)
                return null;

            var fromDate =
                HealthNetParsingHelper.ParseDate(dateTokens[0].Text);

            if (!fromDate.HasValue)
                return null;

            var thruDate = dateTokens.Count > 1
                ? HealthNetParsingHelper.ParseDate(dateTokens[1].Text)
                : fromDate;

            if (!thruDate.HasValue)
                thruDate = fromDate;

            string? cpt = null;
            var modifiers = new List<string>();
            var exCodes = new List<string>();
            string? qpa = null;

            decimal? billed = null;
            decimal? notAllowed = null;
            decimal? allowed = null;
            decimal? cobAllowed = null;
            decimal? cobPaid = null;
            decimal? costShare = null;
            string? costShareRaw = null;
            decimal? ptResp = null;
            decimal? benefit = null;

            foreach (var word in rowWords.OrderBy(x => x.Left))
            {
                var text = word.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (HealthNetParsingHelper.DateRegex.IsMatch(text))
                    continue;

                var cx = MidX(word);

                /*
                 * Column assignment is exclusive by CenterX.
                 * Modifier / Ex Code / QPA never share another column.
                 */
                if (layout.Modifier.Contains(cx))
                {
                    if (LooksLikeModifier(text)
                        && !modifiers.Contains(text, StringComparer.OrdinalIgnoreCase))
                    {
                        modifiers.Add(text);
                    }

                    continue;
                }

                if (layout.Cpt.Contains(cx)
                    && LooksLikeProcedureCode(text)
                    && cpt == null)
                {
                    cpt = text;
                    continue;
                }

                if (layout.Explanation.Contains(cx) && !IsMoneyToken(text))
                {
                    /*
                     * Collect every discrete Ex Code token in the
                     * Ex Code X-band. Do not invent Ex Detail.
                     */
                    if (LooksLikeExplanationCode(text)
                        || LooksLikeWrappedCodeFragment(text))
                    {
                        AppendExCodeToken(exCodes, text);
                    }

                    continue;
                }

                if (layout.Qpa.Contains(cx))
                {
                    /*
                     * QPA stays null when the cell is blank or
                     * non-money text leaks into this band.
                     */
                    if (IsMoneyToken(text))
                        qpa ??= text;

                    continue;
                }

                if (IsMoneyToken(text))
                {
                    var amount =
                        HealthNetParsingHelper.ParseDecimal(text);

                    if (!amount.HasValue)
                        continue;

#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"HN FINANCIAL EXTRACTION Page={pageNumber} Row=SERVICE " +
                        $"Raw='{string.Join(" ", rowWords.OrderBy(x => x.Left).Select(x => x.Text))}' " +
                        $"Word='{text}' X={cx:0.00} Y={MidY(word):0.00} " +
                        $"Column={layout.FinancialColumnAt(cx) ?? "UNMAPPED"} Amount={amount:0.00}");
#endif
                    if (layout.Billed.Contains(cx))
                        billed ??= amount;
                    else if (layout.NotAllowed.Contains(cx))
                        notAllowed ??= amount;
                    else if (layout.Allowed.Contains(cx))
                        allowed ??= amount;
                    else if (layout.CobAllowed.Contains(cx))
                        cobAllowed ??= amount;
                    else if (layout.CobPaid.Contains(cx))
                        cobPaid ??= amount;
                    else if (layout.PatientCostShare.Contains(cx))
                    {
                        costShare ??= amount;
                        costShareRaw ??= text;
                    }
                    else if (layout.PatientResponsibility.Contains(cx))
                        ptResp ??= amount;
                    else if (layout.BenefitPayable.Contains(cx))
                        benefit ??= amount;

                    continue;
                }
            }

            if (cpt == null)
            {
                cpt = rowWords
                    .Where(x => layout.Cpt.Contains(MidX(x))
                                || MidX(x) < layout.Modifier.Left)
                    .Where(x => LooksLikeProcedureCode(x.Text))
                    .Where(x => !HealthNetParsingHelper.DateRegex.IsMatch(x.Text))
                    .OrderBy(x => x.Left)
                    .Select(x => x.Text.Trim())
                    .FirstOrDefault();
            }

            /*
             * Modifier ONLY from the Modifier X band (already collected).
             * If the band was empty because layout was slightly off,
             * recover only tokens whose CenterX sits between CPT and Billed
             * â€” the physical Modifier column â€” never Ex Code tokens.
             */
            if (modifiers.Count == 0 && cpt != null)
            {
                var cptWord = rowWords.FirstOrDefault(x =>
                    string.Equals(
                        x.Text.Trim(),
                        cpt,
                        StringComparison.OrdinalIgnoreCase));

                if (cptWord != null)
                {
                    modifiers = rowWords
                        .Where(x =>
                        {
                            var mx = MidX(x);
                            return (layout.Modifier.Contains(mx)
                                    || (mx > cptWord.Right
                                        && mx < layout.Billed.Left))
                                && LooksLikeModifier(x.Text)
                                && !LooksLikeExplanationCode(x.Text);
                        })
                        .OrderBy(x => x.Left)
                        .Select(x => x.Text.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            if (exCodes.Count == 0)
            {
                foreach (var word in rowWords
                    .Where(x => layout.Explanation.Contains(MidX(x))
                                || MidX(x) >= layout.BenefitPayable.Right)
                    .Where(x => !IsMoneyToken(x.Text))
                    .Where(x =>
                        !string.Equals(
                            x.Text.Trim(),
                            cpt,
                            StringComparison.OrdinalIgnoreCase)
                        && !modifiers.Contains(
                            x.Text.Trim(),
                            StringComparer.OrdinalIgnoreCase))
                    .OrderBy(x => x.Left))
                {
                    AppendExCodeToken(exCodes, word.Text);
                }
            }

            if (string.IsNullOrWhiteSpace(cpt))
                return null;

            bool hasFinancial =
                billed.HasValue
                || notAllowed.HasValue
                || allowed.HasValue
                || cobAllowed.HasValue
                || cobPaid.HasValue
                || costShare.HasValue
                || ptResp.HasValue
                || benefit.HasValue;

            if (!hasFinancial && exCodes.Count == 0)
                return null;

            var trailingMoney = rowWords
                .Where(x => IsMoneyToken(x.Text))
                .Select(x =>
                {
                    var amt = HealthNetParsingHelper.ParseDecimal(x.Text);
                    return amt.HasValue
                        ? ((decimal)amt.Value, MidX(x))
                        : ((decimal?)null, MidX(x));
                })
                .Where(x => x.Item1.HasValue)
                .Where(x => x.Item2 >= layout.PatientCostShare.Left - 8)
                .OrderBy(x => x.Item2)
                .Select(x => x.Item1!.Value)
                .ToList();

            if (!layout.HasHeaderGeometry)
            {
                HealthNetFinancialValidationHelper.ReconcileLinePtRespAndBenefit(
                    ref costShare,
                    ref ptResp,
                    ref benefit,
                    trailingMoney);
            }

            return new HealthNetClaimLineModel
            {
                LineNo = lineNo,
                PageNumber = pageNumber,
                RawLineText = string.Join(
                    " ",
                    rowWords.OrderBy(x => x.Left).Select(x => x.Text)),
                ServiceDateFrom = fromDate,
                ServiceDateThru = thruDate,
                CPTRev = cpt,
                Modifiers = modifiers,
                BilledAmount = billed,
                AmountNotAllowed = notAllowed,
                AllowedAmount = allowed,
                CobAllowedAmount = cobAllowed,
                CobPaidAmount = cobPaid,
                PatientCostShareAmount = costShare,
                PatientCostShareRaw = costShareRaw,
                PatientResponsibility = ptResp,
                BenefitPayable = benefit,
                ExCodes = exCodes,
                QPA = qpa
            };
        }

        private sealed class ServiceRowAnchor
        {
            public double CenterY { get; set; }
            public HealthNetWordModel DateWord { get; set; } = null!;
        }

        private static List<ServiceRowAnchor> ClusterServiceRowAnchors(
            List<HealthNetWordModel> dateWords,
            double rowTolerance)
        {
            var anchors = new List<ServiceRowAnchor>();

            foreach (var dateWord in dateWords.OrderByDescending(MidY))
            {
                var y = MidY(dateWord);

                var existing = anchors.FirstOrDefault(a =>
                    Math.Abs(a.CenterY - y) <= rowTolerance);

                if (existing != null)
                {
                    if (dateWord.Left < existing.DateWord.Left)
                    {
                        existing.DateWord = dateWord;
                        existing.CenterY = y;
                    }

                    continue;
                }

                anchors.Add(new ServiceRowAnchor
                {
                    CenterY = y,
                    DateWord = dateWord
                });
            }

            return anchors
                .OrderByDescending(x => x.CenterY)
                .ToList();
        }

        private static double EstimateRowYTolerance(
            List<HealthNetWordModel> words)
        {
            var heights = words
                .Select(x => Math.Abs(x.Top - x.Bottom))
                .Where(x => x > 0.5 && x < 30)
                .OrderBy(x => x)
                .ToList();

            if (heights.Count == 0)
                return DefaultRowYTolerance;

            var median = heights[heights.Count / 2];
            return Math.Clamp(median * 0.65, 3.0, 7.0);
        }

        private static bool LooksLikeProcedureCode(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && ProcedureCodeRegex.IsMatch(value.Trim());
        }

        private static bool LooksLikeModifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();

            if (trimmed.Length >= 5 && LooksLikeProcedureCode(trimmed))
                return false;

            return ModifierTokenRegex.IsMatch(trimmed);
        }

        private static bool LooksLikeExplanationCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            /*
             * Source glyphs often print "CO96," / "CO234," with a
             * trailing comma still attached to the code token.
             */
            var trimmed = value.Trim().TrimEnd(',', ';', '.');

            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            if (IsMoneyToken(trimmed))
                return false;

            /*
             * Keep short pure modifiers out of Ex Code.
             */
            if (trimmed.Length <= 2 && LooksLikeModifier(trimmed))
                return false;

            return ExCodeTokenRegex.IsMatch(trimmed);
        }

        private static bool LooksLikeWrappedCodeFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim().TrimEnd(',', ';', '.');

            if (LooksLikeExDetailText(trimmed))
                return false;

            if (IsMoneyToken(trimmed))
                return false;

            if (HealthNetParsingHelper.DateRegex.IsMatch(trimmed))
                return false;

            return WrappedCodeFragmentRegex.IsMatch(trimmed);
        }

        private static bool LooksLikeExDetailText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim().TrimEnd(',', ';', '.');

            /*
             * Multi-word English legend/detail prose â€” not a code
             * token that merely carries a trailing comma.
             */
            if (trimmed.Contains(' '))
                return true;

            if (trimmed.Contains(',')
                && trimmed.Any(char.IsWhiteSpace))
            {
                return true;
            }

            return false;
        }

        private static bool IsMoneyToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return HealthNetParsingHelper.MoneyRegex.IsMatch(value.Trim());
        }

        private static bool IsFinancialBoundaryWord(string text)
        {
            var upper = text.ToUpperInvariant();
            return upper.Contains("PREVIOUSLY")
                || upper.Contains("OVERPAYMENT")
                || (upper.Contains("TOTAL") && upper.Length <= 12);
        }

        private static bool IsPatientBoundaryWord(string text)
        {
            var upper = text.ToUpperInvariant();
            return upper.Contains("PATIENT")
                || upper.Contains("ACCT")
                || upper.Contains("SUBID")
                || upper.Contains("CLAIM");
        }

        private static string AppendText(
            string? existing,
            string addition)
        {
            if (string.IsNullOrWhiteSpace(existing))
                return addition;

            if (string.IsNullOrWhiteSpace(addition))
                return existing;

            return existing.TrimEnd() + " " + addition.Trim();
        }

        private static double MidX(HealthNetWordModel word)
        {
            return (word.Left + word.Right) / 2.0;
        }

        private static double MidY(HealthNetWordModel word)
        {
            return (word.Top + word.Bottom) / 2.0;
        }

#if DEBUG
        private static void DumpCoordinateSample(
            int pageNumber,
            List<HealthNetWordModel> words,
            HealthNetServiceColumnLayout layout)
        {
            System.Diagnostics.Debug.WriteLine(
                $"==== HN COORD DUMP page {pageNumber} ====");

            System.Diagnostics.Debug.WriteLine(
                $"CPT=[{layout.Cpt.Left:0.0}-{layout.Cpt.Right:0.0}] " +
                $"MOD=[{layout.Modifier.Left:0.0}-{layout.Modifier.Right:0.0}] " +
                $"BILLED=[{layout.Billed.Left:0.0}-{layout.Billed.Right:0.0}] " +
                $"COST=[{layout.PatientCostShare.Left:0.0}-{layout.PatientCostShare.Right:0.0}] " +
                $"PTRESP=[{layout.PatientResponsibility.Left:0.0}-{layout.PatientResponsibility.Right:0.0}] " +
                $"BEN=[{layout.BenefitPayable.Left:0.0}-{layout.BenefitPayable.Right:0.0}] " +
                $"EX=[{layout.Explanation.Left:0.0}-{layout.Explanation.Right:0.0}] " +
                $"QPA=[{layout.Qpa.Left:0.0}-{layout.Qpa.Right:0.0}]");

            foreach (var word in words
                .OrderByDescending(MidY)
                .ThenBy(x => x.Left)
                .Take(150))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"'{word.Text}' " +
                    $"L={word.Left:0.0} R={word.Right:0.0} " +
                    $"CX={MidX(word):0.0} " +
                    $"T={word.Top:0.0} B={word.Bottom:0.0} " +
                    $"CY={MidY(word):0.0}");
            }
        }
#endif
    }

    /// <summary>
    /// Horizontal bands for Health Net service-line columns.
    /// Patient Copay/Coinsurance/Deductible is ONE combined column.
    /// </summary>
    internal sealed class HealthNetServiceColumnLayout
    {
        public bool HasHeaderGeometry { get; private set; }

        public ColumnBand Dos { get; private set; }
        public ColumnBand Cpt { get; private set; }
        public ColumnBand Modifier { get; private set; }
        public ColumnBand Billed { get; private set; }
        public ColumnBand NotAllowed { get; private set; }
        public ColumnBand Allowed { get; private set; }
        public ColumnBand CobAllowed { get; private set; }
        public ColumnBand CobPaid { get; private set; }
        public ColumnBand PatientCostShare { get; private set; }
        public ColumnBand PatientResponsibility { get; private set; }
        public ColumnBand BenefitPayable { get; private set; }
        public ColumnBand Explanation { get; private set; }
        public ColumnBand Qpa { get; private set; }

        public static HealthNetServiceColumnLayout Detect(
            IReadOnlyList<HealthNetTextLineModel> lines,
            double pageWidth)
        {
            return TryDetectHeaderGeometry(lines, pageWidth, out var detected)
                ? detected
                : CreateDefault(pageWidth);
        }
        // Read a single table's stacked header in two dimensions. Sorting all
        // header words by X mixes adjacent cells (Not/Allowed, COB/Allowed,
        // Patient/Copay and Patient/Resp), especially on sparse service rows.
        private static bool TryDetectHeaderGeometry(
            IReadOnlyList<HealthNetTextLineModel> lines,
            double pageWidth,
            out HealthNetServiceColumnLayout layout)
        {
            layout = CreateDefault(pageWidth);
            var words = lines.Where(l => l.Words != null)
                .SelectMany(l => l.Words!)
                .Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            double X(HealthNetWordModel w) => (w.Left + w.Right) / 2;
            double Y(HealthNetWordModel w) => (w.Top + w.Bottom) / 2;
            string N(HealthNetWordModel w) =>
                HealthNetParsingHelper.NormalizeTableHeader(w.Text).Trim('.', ':');
            var cpt = words.FirstOrDefault(w => N(w).Contains("CPT"));
            if (cpt == null)
                return false;

            var height = Math.Max(4, Math.Abs(cpt.Top - cpt.Bottom));
            var header = words.Where(w => Y(w) >= Y(cpt) - height
                && Y(w) <= Y(cpt) + height * 5).ToList();
            HealthNetWordModel? Find(Func<string, bool> matches) =>
                header.FirstOrDefault(w => matches(N(w)));
            // Some extractors provide one logical line per complete header
            // cell. Preserve those cell starts instead of mixing their words.
            var cellHeaders = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)
                && (l.Top+l.Bottom)/2 >= Y(cpt)-height
                && (l.Top+l.Bottom)/2 <= Y(cpt)+height*5)
                .Select(l => (Line: l, Key: HealthNetParsingHelper.ClassifyMoneyHeaderKey(l.Text)))
                .Where(x => x.Key != null).GroupBy(x => x.Key!)
                .ToDictionary(g => g.Key, g => g.First().Line);
            HealthNetWordModel? Cell(string key) => cellHeaders.Count == 8 && cellHeaders.TryGetValue(key, out var cell)
                ? new HealthNetWordModel { Text = cell.Text, Left = cell.Left, Right = cell.Left,
                    Top = cell.Top, Bottom = cell.Bottom } : null;
            var billed = Cell("BILLED") ?? Find(n => n.Contains("BILLED"));
            var notAllowed = Cell("NOT_ALLOWED") ?? Find(n => n == "NOT" || n.Contains("NOT ALLOW"));
            var benefit = Cell("BENEFIT") ?? Find(n => n.Contains("BENEFIT"));
            var costShare = Cell("COST_SHARE") ?? Find(n => n.Contains("COINS"))
                ?? Find(n => n.Contains("COPAY"))
                ?? Find(n => n.Contains("DED"));
            var ptResp = Cell("PT_RESP") ?? Find(n => n.Contains("RESP"));
            var modifier = Find(n => n.StartsWith("MOD"));
            var cobs = header.Where(w => N(w).Contains("COB")).OrderBy(X).ToList();
            if (Cell("COB_ALLOWED") != null && Cell("COB_PAID") != null)
                cobs = new List<HealthNetWordModel> { Cell("COB_ALLOWED")!, Cell("COB_PAID")! };
            if (billed == null || notAllowed == null || benefit == null
                || costShare == null || ptResp == null || modifier == null
                || cobs.Count != 2)
                return false;

            // “Amount Not Allowed” and “Allowed Amount” are stacked,
            // multi-line headers. PdfPig therefore exposes several separate
            // words named “Allowed”. Pick the candidate at the physical
            // column expected between Not Allowed and the first COB column;
            // header Y/order is not a reliable way to distinguish them.
            var expectedAllowedCenter = (X(notAllowed) + X(cobs[0])) / 2.0;
            var allowed = Cell("ALLOWED") ?? header.Where(w => N(w).Contains("ALLOW")
                && !N(w).Contains("NOT") && !N(w).Contains("COB")
                && X(w) > X(notAllowed)
                && X(w) < X(cobs[0]))
                .OrderBy(w => Math.Abs(X(w) - expectedAllowedCenter))
                .FirstOrDefault();
            if (allowed == null)
                return false;

            var moneyCenters = new[] { X(billed), X(notAllowed), X(allowed),
                X(cobs[0]), X(cobs[1]), X(costShare), X(ptResp), X(benefit) };
            if (moneyCenters.Zip(moneyCenters.Skip(1), (a,b) => b-a).Any(d => d <= height))
                return false;

            var ex = Find(n => n == "EX" || n.Contains("EX CODE"));
            var qpa = Find(n => n == "QPA");
            var dos = Find(n => n.Contains("SVC"));
            double exCenter = ex != null ? X(ex) : X(benefit) + (X(benefit) - X(ptResp));
            if (ex != null && N(ex) == "EX")
            {
                var code = header.FirstOrDefault(w => N(w) == "CODE"
                    && w.Left >= ex.Right && Math.Abs(Y(w)-Y(ex)) <= height);
                if (code != null) exCenter = (ex.Left + code.Right) / 2;
            }
            double qpaCenter = qpa != null ? X(qpa) : exCenter + (exCenter-X(benefit));
            if (exCenter <= X(benefit) || qpaCenter <= exCenter)
                return false;

            double mid(double a, double b) => (a+b)/2;
            var dosCenter = dos != null ? X(dos) : Math.Max(0, X(cpt) - (X(modifier)-X(cpt)));
            layout.Dos = new ColumnBand(0, mid(dosCenter, X(cpt)));
            layout.Cpt = new ColumnBand(layout.Dos.Right, mid(X(cpt), X(modifier)));
            layout.Modifier = new ColumnBand(layout.Cpt.Right, mid(X(modifier), moneyCenters[0]));
            var firstMoneyLeft = layout.Modifier.Right;
            ColumnBand Money(int i) => new ColumnBand(
                i == 0 ? firstMoneyLeft : mid(moneyCenters[i-1],moneyCenters[i]),
                i == 7 ? mid(moneyCenters[i],exCenter) : mid(moneyCenters[i],moneyCenters[i+1]));
            layout.Billed = Money(0);
            layout.NotAllowed = Money(1);
            layout.Allowed = Money(2);
            layout.CobAllowed = Money(3);
            layout.CobPaid = Money(4);
            layout.PatientCostShare = Money(5);
            layout.PatientResponsibility = Money(6);
            layout.BenefitPayable = Money(7);
            layout.Explanation = new ColumnBand(layout.BenefitPayable.Right,mid(exCenter,qpaCenter));
            layout.Qpa = new ColumnBand(layout.Explanation.Right,pageWidth+20);
            layout.HasHeaderGeometry = true;
            return true;
        }
        public static HealthNetServiceColumnLayout RefineFromSampleRows(
            HealthNetServiceColumnLayout layout,
            List<HealthNetWordModel> allWords,
            List<double> rowCenterYs,
            double rowTolerance)
        {
            if (layout.HasHeaderGeometry)
                return layout;

            var sampleRows = new List<List<HealthNetWordModel>>();

            foreach (var centerY in rowCenterYs.Take(6))
            {
                var row = allWords
                    .Where(x =>
                        Math.Abs(((x.Top + x.Bottom) / 2.0) - centerY)
                        <= rowTolerance)
                    .OrderBy(x => x.Left)
                    .ToList();

                if (row.Count > 0)
                    sampleRows.Add(row);
            }

            if (sampleRows.Count == 0)
                return layout;

            var cptWords = sampleRows
                .Select(row => row
                    .Where(x => ProcedureCodeRegex.IsMatch(x.Text.Trim()))
                    .Where(x => !HealthNetParsingHelper.DateRegex.IsMatch(x.Text))
                    .OrderBy(x => x.Left)
                    .FirstOrDefault())
                .Where(x => x != null)
                .Cast<HealthNetWordModel>()
                .ToList();

            var firstMoneys = sampleRows
                .Select(row => row
                    .FirstOrDefault(x =>
                        HealthNetParsingHelper.MoneyRegex.IsMatch(x.Text)))
                .Where(x => x != null)
                .Cast<HealthNetWordModel>()
                .ToList();

            if (cptWords.Count > 0 && firstMoneys.Count > 0)
            {
                var cptLeft = cptWords.Min(x => x.Left) - 2;
                var cptRight = cptWords.Max(x => x.Right) + 2;
                var moneyLeft = firstMoneys.Min(x => x.Left);

                if (moneyLeft > cptRight + 1)
                {
                    layout.Cpt = new ColumnBand(cptLeft, cptRight);
                    layout.Modifier = new ColumnBand(cptRight + 0.5, moneyLeft - 0.5);
                    layout.Billed = new ColumnBand(
                        moneyLeft - 1,
                        Math.Max(layout.Billed.Right, moneyLeft + 20));
                }
            }

            /*
             * Snap the 8 Health Net money columns from real sample-row
             * money word CenterX values so blank cells cannot shift
             * Cost Share / Pt Resp / Benefit.
             */
            RefineMoneyColumnsFromSampleRows(layout, sampleRows);

            var exLefts = new List<double>();

            foreach (var row in sampleRows)
            {
                var lastMoney = row
                    .Where(x => HealthNetParsingHelper.MoneyRegex.IsMatch(x.Text))
                    .OrderBy(x => x.Left)
                    .LastOrDefault();

                if (lastMoney == null)
                    continue;

                var exWord = row
                    .Where(x => x.Left > lastMoney.Right)
                    .Where(x => !HealthNetParsingHelper.MoneyRegex.IsMatch(x.Text))
                    .Where(x => ExCodeTokenRegex.IsMatch(x.Text.Trim()))
                    .OrderBy(x => x.Left)
                    .FirstOrDefault();

                if (exWord != null)
                    exLefts.Add(exWord.Left);
            }

            if (exLefts.Count > 0)
            {
                var exLeft = exLefts.Min() - 2;

                layout.Explanation = new ColumnBand(
                    exLeft,
                    Math.Max(layout.Qpa.Left, exLeft + 40));

                if (layout.BenefitPayable.Right > exLeft)
                {
                    layout.BenefitPayable = new ColumnBand(
                        layout.BenefitPayable.Left,
                        exLeft - 0.5);
                }

                if (layout.Qpa.Left < layout.Explanation.Right)
                {
                    layout.Qpa = new ColumnBand(
                        layout.Explanation.Right,
                        layout.Qpa.Right);
                }
            }

            return layout;
        }

        private static void RefineMoneyColumnsFromSampleRows(
            HealthNetServiceColumnLayout layout,
            List<List<HealthNetWordModel>> sampleRows)
        {
            var moneyRows = sampleRows
                .Select(row => row
                    .Where(x =>
                        HealthNetParsingHelper.MoneyRegex.IsMatch(x.Text))
                    .OrderBy(x => x.Left)
                    .ToList())
                .Where(x => x.Count >= 6)
                .ToList();

            if (moneyRows.Count == 0)
                return;

            var targetCount = moneyRows
                .GroupBy(x => x.Count)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .Select(g => g.Key)
                .First();

            /*
             * Only refine money bands from sample rows when every
             * expected money cell is present (8 columns). Fewer tokens
             * mean blank cells â€” remapping by index would shift values
             * into Copay / CostShare / PtResp incorrectly.
             */
            if (targetCount != 8)
                return;

            var aligned = moneyRows
                .Where(x => x.Count == targetCount)
                .ToList();

            if (aligned.Count == 0)
                return;

            var centers = new double[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                centers[i] = aligned
                    .Select(row => (row[i].Left + row[i].Right) / 2.0)
                    .OrderBy(x => x)
                    .ElementAt(aligned.Count / 2);
            }

            ColumnBand BandAt(int index, ColumnBand fallback)
            {
                if (index < 0 || index >= targetCount)
                    return fallback;

                double left = index == 0
                    ? centers[0] - 12
                    : (centers[index - 1] + centers[index]) / 2.0;

                double right = index == targetCount - 1
                    ? centers[index] + 12
                    : (centers[index] + centers[index + 1]) / 2.0;

                return new ColumnBand(left, right);
            }

            /*
             * Expected Health Net money order when ALL cells print:
             * 0 Billed, 1 NotAllowed, 2 Allowed, 3 CobAllowed,
             * 4 CobPaid, 5 CostShare, 6 PtResp, 7 Benefit
             */
            layout.Billed = BandAt(0, layout.Billed);
            layout.NotAllowed = BandAt(1, layout.NotAllowed);
            layout.Allowed = BandAt(2, layout.Allowed);
            layout.CobAllowed = BandAt(3, layout.CobAllowed);
            layout.CobPaid = BandAt(4, layout.CobPaid);
            layout.PatientCostShare = BandAt(5, layout.PatientCostShare);
            layout.PatientResponsibility = BandAt(6, layout.PatientResponsibility);
            layout.BenefitPayable = BandAt(7, layout.BenefitPayable);
        }

        private static readonly Regex ProcedureCodeRegex =
            new Regex(@"^[A-Za-z0-9]{3,8}$", RegexOptions.Compiled);

        private static readonly Regex ExCodeTokenRegex =
            new Regex(@"^[A-Za-z0-9]{2,24}$", RegexOptions.Compiled);

        private static HealthNetServiceColumnLayout CreateDefault(
            double pageWidth)
        {
            double W(double fraction) => pageWidth * fraction;

            return new HealthNetServiceColumnLayout
            {
                Dos = new ColumnBand(W(0.02), W(0.16)),
                Cpt = new ColumnBand(W(0.16), W(0.23)),
                Modifier = new ColumnBand(W(0.23), W(0.29)),
                Billed = new ColumnBand(W(0.29), W(0.37)),
                NotAllowed = new ColumnBand(W(0.37), W(0.45)),
                Allowed = new ColumnBand(W(0.45), W(0.52)),
                CobAllowed = new ColumnBand(W(0.52), W(0.59)),
                CobPaid = new ColumnBand(W(0.59), W(0.65)),
                PatientCostShare = new ColumnBand(W(0.65), W(0.73)),
                PatientResponsibility = new ColumnBand(W(0.73), W(0.81)),
                BenefitPayable = new ColumnBand(W(0.81), W(0.89)),
                Explanation = new ColumnBand(W(0.89), W(0.96)),
                Qpa = new ColumnBand(W(0.96), W(1.05))
            };
        }

        internal readonly struct ColumnBand
        {
            public ColumnBand(double left, double right)
            {
                Left = left;
                Right = Math.Max(left + 1, right);
            }

            public double Left { get; }
            public double Right { get; }

            public bool Contains(double x) =>
                x >= Left && x < Right;
        }
        internal string? FinancialColumnAt(double x)
        {
            if (Billed.Contains(x)) return "BilledAmount";
            if (NotAllowed.Contains(x)) return "AmountNotAllowed";
            if (Allowed.Contains(x)) return "AllowedAmount";
            if (CobAllowed.Contains(x)) return "CobAllowedAmount";
            if (CobPaid.Contains(x)) return "CobPaidAmount";
            if (PatientCostShare.Contains(x)) return "PatientCostShareAmount";
            if (PatientResponsibility.Contains(x)) return "PatientResponsibility";
            if (BenefitPayable.Contains(x)) return "BenefitPayable";
            return null;
        }
    }
}



