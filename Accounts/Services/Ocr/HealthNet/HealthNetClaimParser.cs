using Accounts.Services.Ocr.HealthNet.Models;
using Accounts.Services.Ocr.HealthNet;

namespace Accounts.Services.Ocr.HealthNet
{
    public class HealthNetClaimParser
    {
        public HealthNetClaimParser(
            HealthNetServiceLineParser serviceLineParser,
            IHealthNetTextNormalizer normalizer)
        {
            /*
             * Kept in DI constructor for compatibility.
             * Document parser owns service-line extraction order.
             * Patient/account normalization is done locally in
             * matching (when activated), not stored on claims.
             */
            _ = serviceLineParser;
            _ = normalizer;
        }

        /// <summary>
        /// Content above the first EXPLANATION OF PAYMENT on a page
        /// (legacy continuation of the prior claim).
        /// </summary>
        public HealthNetEopBlock? GetPreambleBeforeFirstEop(
            HealthNetPageData page)
        {
            if (page?.Lines == null || page.Lines.Count == 0)
                return null;

            var firstEop = page.Lines
                .Where(IsExplanationOfPaymentLine)
                .OrderByDescending(MidY)
                .FirstOrDefault();

            if (firstEop == null)
                return null;

            var bottom = MidY(firstEop) + 1.0;
            var top = page.Lines
                .Select(MidY)
                .DefaultIfEmpty(page.PageHeight)
                .Max() + 2.0;

            if (top <= bottom)
                return null;

            var block = BuildBlock(
                page.PageNumber,
                top,
                bottom,
                page.PageWidth,
                page.Lines,
                page.Words ?? new List<HealthNetWordModel>());

            if (block.Words.Count == 0 && block.Lines.Count == 0)
                return null;

            return block;
        }

        /// <summary>
        /// Split a physical page into EXPLANATION OF PAYMENT blocks.
        /// A page may contain 0, 1, or many complete claims.
        /// </summary>
        public List<HealthNetEopBlock> SplitEopBlocks(
            HealthNetPageData page)
        {
            var result = new List<HealthNetEopBlock>();

            if (page == null)
                return result;

            var lines = page.Lines ?? new List<HealthNetTextLineModel>();
            var words = page.Words ?? new List<HealthNetWordModel>();
            var pageWidth = page.PageWidth > 0
                ? page.PageWidth
                : lines.Select(x => x.Right).DefaultIfEmpty(612).Max();

            var eopYs = lines
                .Where(IsExplanationOfPaymentLine)
                .Select(MidY)
                .OrderByDescending(y => y)
                .ToList();

            if (eopYs.Count == 0)
            {
                /*
                 * Fallback: whole page as one region when EOP title
                 * is missing but claim content still exists.
                 */
                if (lines.Any(IsPatientNameLine)
                    || words.Any(w =>
                        HealthNetParsingHelper.DateRegex.IsMatch(w.Text)))
                {
                    result.Add(BuildBlock(
                        page.PageNumber,
                        lines.Select(MidY).DefaultIfEmpty(page.PageHeight).Max() + 2,
                        0,
                        pageWidth,
                        lines,
                        words));
                }

                return result;
            }

            for (int i = 0; i < eopYs.Count; i++)
            {
                var top = eopYs[i] + 4.0;
                var bottom = i + 1 < eopYs.Count
                    ? eopYs[i + 1] + 1.0
                    : 0.0;

                result.Add(BuildBlock(
                    page.PageNumber,
                    top,
                    bottom,
                    pageWidth,
                    lines,
                    words));
            }

            return result;
        }

        /// <summary>
        /// Within an EOP block, content ABOVE the first Patient Name
        /// belongs to the prior open claim (legacy continuation).
        /// </summary>
        public HealthNetEopBlock? GetRegionAboveFirstPatient(
            HealthNetEopBlock block)
        {
            if (block?.Lines == null || block.Lines.Count == 0)
                return null;

            var firstPatient = block.Lines
                .Where(IsPatientNameLine)
                .OrderByDescending(MidY)
                .FirstOrDefault();

            if (firstPatient == null)
                return null;

            var bottom = MidY(firstPatient) + 1.0;
            if (block.Top <= bottom + 2.0)
                return null;

            var region = BuildBlock(
                block.PageNumber,
                block.Top,
                bottom,
                block.PageWidth,
                block.Lines,
                block.Words);

            if (region.Words.Count == 0 && region.Lines.Count == 0)
                return null;

            return region;
        }

        /// <summary>
        /// Within one EOP region, split claim segments when a second
        /// Patient Name appears (two complete claims or header-only next).
        /// </summary>
        public List<HealthNetEopBlock> SplitClaimSegments(
            HealthNetEopBlock block)
        {
            var result = new List<HealthNetEopBlock>();

            if (block == null)
                return result;

            var patientYs = block.Lines
                .Where(IsPatientNameLine)
                .Select(MidY)
                .OrderByDescending(y => y)
                .ToList();

            if (patientYs.Count == 0)
            {
                result.Add(block);
                return result;
            }

            for (int i = 0; i < patientYs.Count; i++)
            {
                /*
                 * Segment starts at the Patient Name (not the EOP title)
                 * so prior-claim service rows above the patient stay out.
                 */
                var top = patientYs[i] + 6.0;

                var bottom = i + 1 < patientYs.Count
                    ? patientYs[i + 1] + 1.0
                    : block.Bottom;

                result.Add(BuildBlock(
                    block.PageNumber,
                    top,
                    bottom,
                    block.PageWidth,
                    block.Lines,
                    block.Words));
            }

            return result;
        }

        public HealthNetClaimModel? ParseClaimHeader(
            HealthNetEopBlock block)
        {
            if (block?.Lines == null || block.Lines.Count == 0)
                return null;

            return ParseClaimHeaderOnly(
                block.Lines,
                block.PageNumber);
        }

        public HealthNetClaimPageFinancials? ParseFinancials(
            HealthNetEopBlock block) => ParseFinancials(block, null);

        internal HealthNetClaimPageFinancials? ParseFinancials(
            HealthNetEopBlock block,
            HealthNetServiceColumnLayout? inheritedLayout)
        {
            if (block?.Lines == null || block.Lines.Count == 0)
                return null;

            var financials = new HealthNetClaimPageFinancials();

            financials.PreviouslyPaidAmount =
                FindLabeledAmount(
                    block.Lines,
                    "PREVIOUSLY PAID AMOUNT",
                    "PREVIOUSLY PAID");

            financials.TotalOverpayment =
                FindLabeledAmount(
                    block.Lines,
                    "TOTAL OVERPAYMENT",
                    "OVERPAYMENT");

            financials.Totals = ParseClaimTotals(
                block,
                block.PageWidth, inheritedLayout);

            bool hasAnything =
                financials.PreviouslyPaidAmount.HasValue
                || financials.TotalOverpayment.HasValue
                || HasAnyTotal(financials.Totals);

            return hasAnything ? financials : null;
        }

        /// <summary>
        /// Legacy page-level API retained for compatibility.
        /// Prefer block-scoped parsing from DocumentParser.
        /// </summary>
        public List<HealthNetClaimModel> ParseClaimHeaders(
            HealthNetPageData page)
        {
            var result = new List<HealthNetClaimModel>();

            foreach (var block in SplitEopBlocks(page))
            {
                foreach (var segment in SplitClaimSegments(block))
                {
                    var claim = ParseClaimHeader(segment);
                    if (claim != null)
                        result.Add(claim);
                }
            }

            return result;
        }

        /// <summary>
        /// Legacy page-level financials (content before first patient).
        /// </summary>
        public HealthNetClaimPageFinancials? ParseFinancials(
            HealthNetPageData page)
        {
            if (page?.Lines == null || page.Lines.Count == 0)
                return null;

            var topLines =
                HealthNetServiceLineParser
                    .GetLinesBeforePatientHeader(page.Lines);

            if (topLines.Count == 0)
                return null;

            var block = BuildBlock(
                page.PageNumber,
                topLines.Select(MidY).DefaultIfEmpty(0).Max() + 2,
                topLines.Select(MidY).DefaultIfEmpty(0).Min() - 2,
                page.PageWidth,
                topLines,
                topLines
                    .Where(x => x.Words != null)
                    .SelectMany(x => x.Words!)
                    .ToList());

            return ParseFinancials(block);
        }

        private static HealthNetEopBlock BuildBlock(
            int pageNumber,
            double top,
            double bottom,
            double pageWidth,
            IEnumerable<HealthNetTextLineModel> sourceLines,
            IEnumerable<HealthNetWordModel> sourceWords)
        {
            var lines = sourceLines
                .Where(x => InYRange(MidY(x), top, bottom))
                .OrderByDescending(MidY)
                .ThenBy(x => x.Left)
                .ToList();

            var words = sourceWords
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Where(x => InYRange(MidY(x), top, bottom))
                .OrderByDescending(MidY)
                .ThenBy(x => x.Left)
                .ToList();

            return new HealthNetEopBlock
            {
                PageNumber = pageNumber,
                Top = top,
                Bottom = bottom,
                PageWidth = pageWidth,
                Lines = lines,
                Words = words
            };
        }

        private static bool InYRange(double y, double top, double bottom)
        {
            return y <= top + 0.01 && y >= bottom - 0.01;
        }

        private static double MidY(HealthNetTextLineModel line)
        {
            return (line.Top + line.Bottom) / 2.0;
        }

        private static double MidY(HealthNetWordModel word)
        {
            return (word.Top + word.Bottom) / 2.0;
        }

        private static bool IsExplanationOfPaymentLine(
            HealthNetTextLineModel line)
        {
            return !string.IsNullOrWhiteSpace(line.Text)
                && line.Text.Contains(
                    "EXPLANATION OF PAYMENT",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static decimal? FindLabeledAmount(
            IReadOnlyList<HealthNetTextLineModel> lines,
            params string[] labels)
        {
            var text =
                HealthNetParsingHelper.GetValueAfterAnyLabel(
                    lines,
                    labels,
                    "TOTAL OVERPAYMENT",
                    "CLAIM TOTAL",
                    "TOTAL",
                    "PATIENT NAME",
                    "MEMBER PLAN");

            var fromText =
                HealthNetParsingHelper.ParseDecimal(text);

            if (fromText.HasValue)
                return fromText;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                    continue;

                if (!labels.Any(label =>
                        line.Text.Contains(
                            label,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var amounts =
                    HealthNetParsingHelper.ExtractMoneyValues(
                        line.Text);

                if (amounts.Count > 0)
                    return amounts[0];
            }

            return null;
        }

        private HealthNetClaimModel? ParseClaimHeaderOnly(
            IReadOnlyList<HealthNetTextLineModel> lines,
            int pageNumber)
        {
            var patientName =
                HealthNetParsingHelper.GetValueAfterLabel(
                    lines,
                    "PATIENT NAME",
                    "YOUR ACCT",
                    "SUBID/CIN",
                    "CLAIM #");

            var accountNo =
                HealthNetParsingHelper.GetValueAfterAnyLabel(
                    lines,
                    new[]
                    {
                        "YOUR ACCT #",
                        "YOUR ACCT",
                        "ACCOUNT #"
                    },
                    "SUBID/CIN",
                    "CLAIM #",
                    "RECEIPT DATE");

            var subIdCin =
                HealthNetParsingHelper.GetValueAfterAnyLabel(
                    lines,
                    new[]
                    {
                        "SUBID/CIN",
                        "SUBID CIN",
                        "SUB ID/CIN"
                    },
                    "CLAIM #",
                    "RECEIPT DATE",
                    "NPI");

            var claimNo =
                HealthNetParsingHelper.GetValueAfterAnyLabel(
                    lines,
                    new[]
                    {
                        "CLAIM #",
                        "CLAIM NO",
                        "CLAIM NUMBER"
                    },
                    "RECEIPT DATE",
                    "NPI",
                    "VALUE CODE");

            var receiptDateText =
                HealthNetParsingHelper.GetValueAfterLabel(
                    lines,
                    "RECEIPT DATE",
                    "NPI",
                    "VALUE CODE");

            var npi =
                HealthNetParsingHelper.GetValueAfterLabel(
                    lines,
                    "NPI",
                    "VALUE CODE",
                    "SVC DATES");

            var valueCodeText =
                HealthNetParsingHelper.GetValueAfterAnyLabel(
                    lines,
                    new[]
                    {
                        "VALUE CODE AMT",
                        "VALUE CODE AMOUNT"
                    },
                    "SVC DATES",
                    "CPT/REV");

            bool hasClaimIdentity =
                !string.IsNullOrWhiteSpace(patientName)
                || !string.IsNullOrWhiteSpace(accountNo)
                || !string.IsNullOrWhiteSpace(claimNo);

            if (!hasClaimIdentity)
                return null;

            return new HealthNetClaimModel
            {
                PageNumber = pageNumber,
                HeaderPageNumber = pageNumber,

                PatientNameRaw = patientName,
                AccountNoRaw = accountNo,
                SubIdCinRaw = subIdCin,

                ClaimNo =
                    HealthNetParsingHelper.CleanIdentifier(claimNo),

                ReceiptDate =
                    HealthNetParsingHelper.ParseDate(receiptDateText),

                Npi =
                    HealthNetParsingHelper.CleanIdentifier(npi),

                ValueCodeAmount =
                    HealthNetParsingHelper.ParseDecimal(valueCodeText),

                ServiceLines = new List<HealthNetClaimLineModel>(),
                Totals = new HealthNetClaimTotalsModel(),

                RawClaimText = string.Join(
                    Environment.NewLine,
                    lines
                        .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                        .Select(x => x.Text))
            };
        }

        private static HealthNetClaimTotalsModel ParseClaimTotals(
            HealthNetEopBlock block,
            double pageWidth,
            HealthNetServiceColumnLayout? inheritedLayout)
        {
            var result = new HealthNetClaimTotalsModel();
            var lines = block.Lines;

            var totalLine = lines.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.Text)
                && IsClaimTotalLabel(x.Text));

            if (totalLine == null)
                return result;

            var layout = HealthNetServiceColumnLayout.Detect(
                lines,
                pageWidth > 0 ? pageWidth : Math.Max(totalLine.Right, 612));
            if (!layout.HasHeaderGeometry && inheritedLayout?.HasHeaderGeometry == true)
                layout = inheritedLayout;

            result.HasPrintedTotalRow = layout.HasHeaderGeometry;

            var totalY = MidY(totalLine);
            var totalRowTolerance = Math.Max(2.5, Math.Abs(totalLine.Top-totalLine.Bottom)*0.75);

            /*
             * Total amounts may sit on the Total label line or on the
             * same visual row as sibling money glyphs. Collect by Y band
             * inside this EOP block only â€” stop before Member Plan Code.
             */
            var memberPlanY = block.Lines
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Text)
                    && x.Text.Contains(
                        "MEMBER PLAN",
                        StringComparison.OrdinalIgnoreCase))
                .Select(MidY)
                .Where(y => y < totalY)
                .DefaultIfEmpty(block.Bottom)
                .Max();

            var moneyWords = block.Words
                .Where(x =>
                {
                    if (string.IsNullOrWhiteSpace(x.Text)
                        || !HealthNetParsingHelper.MoneyRegex.IsMatch(x.Text))
                    {
                        return false;
                    }

                    var y = MidY(x);
                    if (y > totalY + 4.0)
                        return false;

                    if (y < memberPlanY + 1.0 && memberPlanY < totalY - 1.0)
                        return false;

                    if (layout.HasHeaderGeometry)
                        return Math.Abs(y-totalY) <= totalRowTolerance;

                    return Math.Abs(y - totalY) <= 14.0
                        || (y <= totalY && y >= totalY - 18.0);
                })
                .OrderBy(x => x.Left)
                .ToList();

            if (moneyWords.Count == 0
                && totalLine.Words != null)
            {
                moneyWords = totalLine.Words
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Text)
                        && HealthNetParsingHelper.MoneyRegex.IsMatch(x.Text))
                    .OrderBy(x => x.Left)
                    .ToList();
            }

            if (moneyWords.Count > 0)
            {
                var sequentialAmounts = moneyWords
                    .Select(x => HealthNetParsingHelper.ParseDecimal(x.Text))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();

                /*
                 * Full 8-cell Total row: left-to-right money order is the
                 * authoritative PDF source. Geometry alone can mis-band
                 * Billed into Not Allowed on some pages.
                 */
                if (!layout.HasHeaderGeometry && sequentialAmounts.Count == 8)
                {
                    HealthNetFinancialValidationHelper
                        .ApplySequentialTotalAmounts(result, sequentialAmounts);

                    return result;
                }

                /*
                 * Sparse Total row (blank cells): CenterX against layout,
                 * then repair known mis-band contamination from sequence.
                 */
                foreach (var word in moneyWords)
                {
                    var amount =
                        HealthNetParsingHelper.ParseDecimal(word.Text);

                    if (!amount.HasValue)
                        continue;

                    var x = (word.Left + word.Right) / 2.0;

#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"HN FINANCIAL EXTRACTION Page={block.PageNumber} Row=TOTAL " +
                        $"Raw='{totalLine.Text}' Word='{word.Text}' " +
                        $"X={x:0.00} Y={MidY(word):0.00} " +
                        $"Column={layout.FinancialColumnAt(x) ?? "UNMAPPED"} Amount={amount:0.00}");
#endif
                    if (layout.Billed.Contains(x))
                        result.BilledAmount ??= amount;
                    else if (layout.NotAllowed.Contains(x))
                        result.AmountNotAllowed ??= amount;
                    else if (layout.Allowed.Contains(x))
                        result.AllowedAmount ??= amount;
                    else if (layout.CobAllowed.Contains(x))
                        result.CobAllowedAmount ??= amount;
                    else if (layout.CobPaid.Contains(x))
                        result.CobPaidAmount ??= amount;
                    else if (layout.PatientCostShare.Contains(x))
                        result.PatientCostShareAmount ??= amount;
                    else if (layout.PatientResponsibility.Contains(x))
                        result.PatientResponsibility ??= amount;
                    else if (layout.BenefitPayable.Contains(x))
                        result.BenefitPayable ??= amount;
                }

                if (layout.HasHeaderGeometry)
                    return result;

                HealthNetFinancialValidationHelper
                    .CorrectMisbandedTotalsFromSequence(
                        result,
                        sequentialAmounts);

                HealthNetFinancialValidationHelper
                    .ReconcileSparsePtRespAndBenefitTail(
                        result,
                        sequentialAmounts);

                return result;
            }

            var amounts =
                HealthNetParsingHelper.ExtractMoneyValues(totalLine.Text);

            /*
             * Text-only fallback: require a full 8-amount Total row so
             * blank cost-share cells cannot shift later columns left.
             */
            if (amounts.Count != 8)
                return result;

            HealthNetFinancialValidationHelper
                .ApplySequentialTotalAmounts(result, amounts);

            return result;
        }

        private static bool IsClaimTotalLabel(string text)
        {
            var upper = text.Trim().ToUpperInvariant();

            if (upper.Contains("OVERPAYMENT")
                || upper.Contains("PREVIOUSLY")
                || upper.Contains("PATIENT RESP")
                || upper.Contains("BENEFIT PAYABLE"))
            {
                return false;
            }

            if (upper.Contains("CLAIM TOTAL"))
                return true;

            /*
             * Bare "Total" row, optionally followed by money tokens.
             */
            if (upper == "TOTAL"
                || upper.StartsWith("TOTAL ")
                || (upper.StartsWith("TOTAL")
                    && HealthNetParsingHelper.MoneyRegex.IsMatch(text)))
            {
                return true;
            }

            return false;
        }

        private static bool HasAnyTotal(HealthNetClaimTotalsModel? totals)
        {
            if (totals == null)
                return false;

            return totals.BilledAmount.HasValue
                || totals.AmountNotAllowed.HasValue
                || totals.AllowedAmount.HasValue
                || totals.CobAllowedAmount.HasValue
                || totals.CobPaidAmount.HasValue
                || totals.PatientCostShareAmount.HasValue
                || totals.PatientResponsibility.HasValue
                || totals.BenefitPayable.HasValue;
        }

        private static bool IsPatientNameLine(
            HealthNetTextLineModel line)
        {
            return !string.IsNullOrWhiteSpace(line.Text)
                && line.Text.Contains(
                    "PATIENT NAME",
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}

