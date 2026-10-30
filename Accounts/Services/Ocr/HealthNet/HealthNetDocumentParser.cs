using Accounts.Services.Ocr.HealthNet.Models;
using Accounts.Services.Ocr.HealthNet;
using System.Security.Cryptography;

namespace Accounts.Services.Ocr.HealthNet
{
    public class HealthNetDocumentParser
        : IHealthNetDocumentParser
    {
        private readonly IHealthNetPdfReader _pdfReader;
        private readonly HealthNetHeaderParser _headerParser;
        private readonly HealthNetClaimParser _claimParser;
        private readonly HealthNetServiceLineParser _serviceLineParser;
        private readonly HealthNetSummaryParser _summaryParser;

        public HealthNetDocumentParser(
            IHealthNetPdfReader pdfReader,
            HealthNetHeaderParser headerParser,
            HealthNetClaimParser claimParser,
            HealthNetServiceLineParser serviceLineParser,
            HealthNetSummaryParser summaryParser,
            HealthNetExplanationCodeParser explanationParser)
        {
            _pdfReader = pdfReader;
            _headerParser = headerParser;
            _claimParser = claimParser;
            _serviceLineParser = serviceLineParser;
            _summaryParser = summaryParser;
            /*
             * Explanation-code legend is intentionally NOT
             * mapped into structured claim/result data.
             * Keep DI parameter for Program.cs compatibility.
             */
            _ = explanationParser;
        }

        public async Task<HealthNetOCRResultViewModel> ParseAsync(
            Stream pdfStream,
            string? fileName = null,
            CancellationToken cancellationToken = default)
        {
            var result = new HealthNetOCRResultViewModel
            {
                FileName = fileName,
                ProcessingStatus = HealthNetProcessingStatus.Parsing
            };

            try
            {
                if (pdfStream == null)
                    throw new ArgumentNullException(nameof(pdfStream));

                byte[] pdfBytes;

                using (var memory = new MemoryStream())
                {
                    await pdfStream.CopyToAsync(memory, cancellationToken);
                    pdfBytes = memory.ToArray();
                }

                if (pdfBytes.Length == 0)
                {
                    throw new InvalidOperationException(
                        "The uploaded PDF is empty.");
                }

                result.FileHash = CalculateSha256(pdfBytes);

                using var readStream = new MemoryStream(pdfBytes);

                var pages = await _pdfReader.ReadPagesAsync(
                    readStream,
                    cancellationToken);

                result.PageCount = pages.Count;

                result.Header = _headerParser.Parse(pages);

#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    "HEADER immediately after HealthNetHeaderParser.Parse: " +
                    $"ProcessDate={result.Header.ProcessDate:yyyy-MM-dd} " +
                    $"RemittanceNo={result.Header.RemittanceNo} " +
                    $"PayeeNo={result.Header.PayeeNo} " +
                    $"SvcProvNo={result.Header.ServiceProviderNo} " +
                    $"SvcProvName={result.Header.ServiceProviderName} " +
                    $"InsAddr={result.Header.InsuranceAddress} " +
                    $"ProvAddr={result.Header.ProviderAddress}");
#endif

                /*
                 * ============================================================
                 * CLAIM LIFECYCLE (EOP-BLOCK AWARE)
                 *
                 * A physical page may contain multiple EXPLANATION OF PAYMENT
                 * blocks. Each block is parsed independently for:
                 *   header â†’ service lines â†’ financial Total
                 *
                 * Claim # is the primary identity:
                 *   same incomplete Claim #  â†’ merge continuation
                 *   completed Total          â†’ finalize, next block is new
                 *   different Claim #        â†’ never merge on Patient Name
                 * ============================================================
                 */

                HealthNetClaimModel? currentClaim = null;
                var claimState = ClaimParseState.NoClaim;
                HealthNetServiceColumnLayout? activeLayout = null;
                double activePageWidth = 0;

                foreach (var page in pages.OrderBy(x => x.PageNumber))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (ShouldSkipPage(page, result))
                        continue;

                    var eopBlocks = _claimParser.SplitEopBlocks(page);

#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"PAGE {page.PageNumber} EOP Blocks = {eopBlocks.Count}");
#endif

                    /*
                     * LEGACY CONTINUATION REGION:
                     * Service lines / Total for the prior claim may sit
                     * ABOVE the first EXPLANATION OF PAYMENT on this page
                     * (next claim's EOP starts lower down).
                     */
                    var preamble = _claimParser.GetPreambleBeforeFirstEop(page);
                    if (preamble != null
                        && (preamble.Words.Count > 0 || preamble.Lines.Count > 0))
                    {
                        var preambleLayout = ResolveTableLayout(preamble, result,
                            currentClaim != null && !IsFinanciallyComplete(currentClaim),
                            ref activeLayout, ref activePageWidth);
                        var preambleLines =
                            _serviceLineParser.Parse(preamble, preambleLayout);
                        var preambleFinancials =
                            _claimParser.ParseFinancials(preamble, preambleLayout);

                        if (preambleLines.Count > 0 || preambleFinancials != null)
                        {
                            if (currentClaim == null)
                            {
                                result.Warnings.Add(
                                    $"Page {page.PageNumber}: " +
                                    "service/financial content above first EOP " +
                                    "found before a claim header.");
                            }
                            else
                            {
                                if (preambleLines.Count > 0)
                                {
                                    AppendServiceLines(
                                        currentClaim,
                                        preambleLines,
                                        page.PageNumber);
                                    claimState = ClaimParseState.ClaimActive;
                                }

                                if (preambleFinancials != null)
                                {
                                    ApplyFinancials(
                                        currentClaim,
                                        preambleFinancials);

                                    if (IsFinanciallyComplete(currentClaim))
                                    {
                                        claimState =
                                            ClaimParseState.ClaimCompleted;
                                    }
                                }

#if DEBUG
                                System.Diagnostics.Debug.WriteLine(
                                    $"  PREAMBLE above EOP: Lines={preambleLines.Count} " +
                                    $"HasTotal={preambleFinancials?.Totals != null && HasAnyTotal(preambleFinancials.Totals)} " +
                                    $"Action=ATTACH PRIOR CLAIM");
#endif
                            }
                        }
                    }

                    foreach (var eopBlock in eopBlocks)
                    {
                        /*
                         * Inside an EOP block, rows above the first
                         * Patient Name close/continue the PRIOR claim.
                         */
                        var abovePatient =
                            _claimParser.GetRegionAboveFirstPatient(eopBlock);

                        if (abovePatient != null)
                        {
                            ApplyBlockContentToCurrentClaim(
                                abovePatient,
                                page.PageNumber,
                                result,
                                ref currentClaim,
                                ref claimState,
                                ref activeLayout, ref activePageWidth,
                                "ABOVE PATIENT â†’ PRIOR");
                        }

                        var segments =
                            _claimParser.SplitClaimSegments(eopBlock);

                        foreach (var segment in segments)
                        {
                            var header =
                                _claimParser.ParseClaimHeader(segment);

                            var segmentLayout = ResolveTableLayout(segment, result,
                                currentClaim != null && !IsFinanciallyComplete(currentClaim)
                                && (header == null || IsSameClaimNumber(currentClaim, header)),
                                ref activeLayout, ref activePageWidth);
                            var serviceLines =
                                _serviceLineParser.Parse(segment, segmentLayout);

                            var financials =
                                _claimParser.ParseFinancials(segment, segmentLayout);

                            var hasTotal =
                                financials?.Totals != null
                                && HasAnyTotal(financials.Totals);

                            string action = "NONE";

                            if (header != null)
                            {
                                if (currentClaim == null)
                                {
                                    currentClaim = StartClaim(
                                        header,
                                        result.Claims.Count,
                                        page.PageNumber);
                                    claimState = ClaimParseState.ClaimActive;
                                    action = "NEW CLAIM";
                                }
                                else if (IsSameClaimNumber(currentClaim, header)
                                         && !IsFinanciallyComplete(currentClaim))
                                {
                                    MergeRepeatedHeader(
                                        currentClaim,
                                        header,
                                        page.PageNumber);
                                    claimState = ClaimParseState.ClaimActive;
                                    action = "MERGE CONTINUATION";

#if DEBUG
                                    System.Diagnostics.Debug.WriteLine(
                                        "HealthNet: repeated header merged into active claim | " +
                                        $"Patient={currentClaim.PatientNameRaw} " +
                                        $"Claim#={currentClaim.ClaimNo} " +
                                        $"Page={page.PageNumber}");
#endif
                                }
                                else if (IsFinanciallyComplete(currentClaim))
                                {
                                    result.Claims.Add(currentClaim);

#if DEBUG
                                    System.Diagnostics.Debug.WriteLine(
                                        "HealthNet: claim finalized at Total | " +
                                        $"Patient={currentClaim.PatientNameRaw} " +
                                        $"Claim#={currentClaim.ClaimNo} " +
                                        $"Lines={currentClaim.ServiceLines?.Count ?? 0}");
#endif

                                    currentClaim = StartClaim(
                                        header,
                                        result.Claims.Count,
                                        page.PageNumber);
                                    claimState = ClaimParseState.ClaimActive;
                                    action = "NEW CLAIM";
                                }
                                else
                                {
                                    result.Warnings.Add(
                                        $"Page {page.PageNumber}: different Claim # " +
                                        $"('{header.ClaimNo}') appeared before prior claim " +
                                        $"('{currentClaim.ClaimNo}' / " +
                                        $"'{currentClaim.PatientNameRaw}') reached its Total row. " +
                                        "Prior claim kept incomplete; starting new claim.");

                                    result.Claims.Add(currentClaim);

                                    currentClaim = StartClaim(
                                        header,
                                        result.Claims.Count,
                                        page.PageNumber);
                                    claimState = ClaimParseState.ClaimActive;
                                    action = "NEW CLAIM (PRIOR INCOMPLETE)";
                                }
                            }
                            else if (currentClaim == null
                                     && (serviceLines.Count > 0
                                         || financials != null))
                            {
                                result.Warnings.Add(
                                    $"Page {page.PageNumber}: " +
                                    "service/financial content found without a claim header.");
                            }

                            if (serviceLines.Count > 0)
                            {
                                if (currentClaim == null)
                                {
                                    result.Warnings.Add(
                                        $"Page {page.PageNumber}: " +
                                        "service lines found before a claim header.");
                                }
                                else
                                {
                                    AppendServiceLines(
                                        currentClaim,
                                        serviceLines,
                                        page.PageNumber);
                                    claimState = ClaimParseState.ClaimActive;
                                }
                            }

                            if (currentClaim != null && financials != null)
                            {
                                ApplyFinancials(currentClaim, financials);

                                if (IsFinanciallyComplete(currentClaim))
                                {
                                    claimState = ClaimParseState.ClaimCompleted;

#if DEBUG
                                    System.Diagnostics.Debug.WriteLine(
                                        $"HN claim finalized by Total row: " +
                                        $"Patient={currentClaim.PatientNameRaw} " +
                                        $"Claim#={currentClaim.ClaimNo} " +
                                        $"Page={page.PageNumber}");
#endif
                                }
                            }

#if DEBUG
                            System.Diagnostics.Debug.WriteLine(
                                $"  BLOCK Top={segment.Top:0.0} Bottom={segment.Bottom:0.0} " +
                                $"Patient={header?.PatientNameRaw ?? currentClaim?.PatientNameRaw} " +
                                $"ClaimNo={header?.ClaimNo ?? currentClaim?.ClaimNo} " +
                                $"ServiceLines={serviceLines.Count} " +
                                $"HasTotal={hasTotal} Action={action}");
#endif
                        }
                    }
                }

                if (currentClaim != null)
                {
                    if (!IsFinanciallyComplete(currentClaim))
                    {
                        result.Warnings.Add(
                            $"Claim {currentClaim.ClaimNo ?? "(unknown)"} " +
                            $"(patient '{currentClaim.PatientNameRaw}') " +
                            "ended at EOF without Total row. " +
                            "Claim data was kept; financial completion was not fabricated.");

#if DEBUG
                        System.Diagnostics.Debug.WriteLine(
                            "HealthNet: claim ended at EOF without Total row | " +
                            $"Patient={currentClaim.PatientNameRaw} " +
                            $"Claim#={currentClaim.ClaimNo} " +
                            $"Lines={currentClaim.ServiceLines?.Count ?? 0}");
#endif
                    }

                    result.Claims.Add(currentClaim);
                }

                // Final guard shared by the preview modal and persistence.
                // Repair a missing line Allowed and Billed-contaminated total.
                foreach (var claim in result.Claims)
                    HealthNetFinancialValidationHelper
                        .RepairContaminatedAllowedFromLines(claim);
                for (int i = 0; i < result.Claims.Count; i++)
                    result.Claims[i].ClaimIndex = i;

                result.Summary = _summaryParser.Parse(pages);

                if (string.IsNullOrWhiteSpace(result.Header.MemberPlanCode))
                {
                    result.Header.MemberPlanCode =
                        result.Summary.MemberPlanCode;
                }

                /*
                 * Document-level Payment/Denial/Adjustment Explanation
                 * Code Description legend is intentionally excluded.
                 */
                result.ExplanationCodes =
                    new List<HealthNetExplanationCodeModel>();

                AddValidationWarnings(result, pages);
                AddSummaryCrossCheckWarning(result);

#if DEBUG
                DumpPreviewDiagnostics(result, claimState);
#endif

                if (result.Claims.Sum(c => c.ServiceLines.Count) == 0)
                    result.Errors.Add("No readable Health Net service rows were found. The PDF was not saved or posted.");
                if (result.Errors.Count > 0)
                {
                    result.Success = false;
                    result.ProcessingStatus = HealthNetProcessingStatus.Failed;
                    result.Message = "Health Net PDF requires review; no data was saved or posted.";
                    return result;
                }
                result.ProcessingStatus = HealthNetProcessingStatus.Parsed;
                result.Success = true;

                result.Message =
                    $"Health Net PDF parsed successfully. " +
                    $"Pages: {result.PageCount}, " +
                    $"Claims: {result.Claims.Count}, " +
                    $"Service Lines: " +
                    $"{result.Claims.Sum(x => x.ServiceLines.Count)}.";

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ProcessingStatus = HealthNetProcessingStatus.Failed;
                result.Message = "Health Net PDF parsing failed.";
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        internal static HealthNetServiceColumnLayout ResolveTableLayout(
            HealthNetEopBlock block,
            HealthNetOCRResultViewModel result,
            bool allowContinuation,
            ref HealthNetServiceColumnLayout? activeLayout,
            ref double activePageWidth)
        {
            var layout = HealthNetServiceColumnLayout.Detect(block.Lines, block.PageWidth);
            if (layout.HasHeaderGeometry)
            {
                activeLayout = layout;
                activePageWidth = block.PageWidth;
                return layout;
            }
            var hasTableHeader = block.Lines.Any(l => l.Text.Contains("CPT", StringComparison.OrdinalIgnoreCase));
            if (!hasTableHeader && allowContinuation && activeLayout != null
                && Math.Abs(activePageWidth-block.PageWidth) < 1)
                return activeLayout;

            var hasFinancialRows = block.Lines.Any(l =>
                (HealthNetParsingHelper.DateRegex.IsMatch(l.Text)
                    && HealthNetParsingHelper.MoneyRegex.IsMatch(l.Text))
                || System.Text.RegularExpressions.Regex.IsMatch(l.Text.Trim(),
                    @"^TOTAL\s*(?:\(?-?\$?[\d,.]+\)?\s*)*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            if (hasFinancialRows)
            {
                var message = $"Page {block.PageNumber}: Health Net table columns could not be identified reliably. "
                    + "The PDF was not saved or posted; check the table header/text layer.";
                if (!result.Errors.Contains(message)) result.Errors.Add(message);
            }
            return layout;
        }
        private void ApplyBlockContentToCurrentClaim(
            HealthNetEopBlock block,
            int pageNumber,
            HealthNetOCRResultViewModel result,
            ref HealthNetClaimModel? currentClaim,
            ref ClaimParseState claimState,
            ref HealthNetServiceColumnLayout? activeLayout,
            ref double activePageWidth,
            string debugLabel)
        {
            var layout = ResolveTableLayout(block, result,
                currentClaim != null && !IsFinanciallyComplete(currentClaim),
                ref activeLayout, ref activePageWidth);
            var serviceLines = _serviceLineParser.Parse(block, layout);
            var financials = _claimParser.ParseFinancials(block, layout);

            if (serviceLines.Count == 0 && financials == null)
                return;

            if (currentClaim == null)
            {
                result.Warnings.Add(
                    $"Page {pageNumber}: " +
                    "service/financial content found before a claim header.");
                return;
            }

            if (serviceLines.Count > 0)
            {
                AppendServiceLines(currentClaim, serviceLines, pageNumber);
                claimState = ClaimParseState.ClaimActive;
            }

            if (financials != null)
            {
                ApplyFinancials(currentClaim, financials);

                if (IsFinanciallyComplete(currentClaim))
                    claimState = ClaimParseState.ClaimCompleted;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                $"  {debugLabel}: Lines={serviceLines.Count} " +
                $"HasTotal={financials?.Totals != null && HasAnyTotal(financials.Totals)} " +
                $"Claim#={currentClaim.ClaimNo}");
#endif
        }

        private enum ClaimParseState
        {
            NoClaim,
            ClaimActive,
            ClaimCompleted
        }

        private static bool ShouldSkipPage(
            HealthNetPageData page,
            HealthNetOCRResultViewModel result)
        {
            if (page.PageType == HealthNetPageType.TermsInformation)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    $"HN boilerplate/terms page ignored: {page.PageNumber}");
#endif
                return true;
            }

            var probe =
                !string.IsNullOrWhiteSpace(page.CleanText)
                    ? page.CleanText
                    : page.RawText;

            if (HealthNetNoisePolicy.IsExplanationLegendSection(probe)
                || HealthNetNoisePolicy.IsProviderInstructionSection(probe))
            {
                /*
                 * Pure legend/instruction pages.
                 * Hybrid claim+summary pages still have service evidence
                 * and are classified as ClaimEop earlier.
                 */
                if (page.PageType != HealthNetPageType.ClaimEop
                    && page.PageType != HealthNetPageType.RemittanceSummary
                    && page.PageType != HealthNetPageType.RemittanceHeader)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"HN legend/instruction page ignored: {page.PageNumber}");
#endif
                    return true;
                }
            }

            return false;
        }

        private static HealthNetClaimModel StartClaim(
            HealthNetClaimModel nextClaim,
            int claimIndex,
            int pageNumber)
        {
            nextClaim.ClaimIndex = claimIndex;
            nextClaim.ServiceLines ??=
                new List<HealthNetClaimLineModel>();
            nextClaim.Totals ??=
                new HealthNetClaimTotalsModel();
            nextClaim.ServicePageNumbers ??=
                new List<int>();
            nextClaim.HeaderPageNumber ??= pageNumber;
            nextClaim.PageNumber = pageNumber;
            return nextClaim;
        }

        private static void AppendServiceLines(
            HealthNetClaimModel claim,
            List<HealthNetClaimLineModel> serviceLines,
            int pageNumber)
        {
            var nextLineNo = claim.ServiceLines.Count + 1;

            foreach (var serviceLine in serviceLines)
            {
                serviceLine.LineNo = nextLineNo++;
                claim.ServiceLines.Add(serviceLine);
            }

            if (!claim.ServicePageNumbers.Contains(pageNumber))
                claim.ServicePageNumbers.Add(pageNumber);

            claim.RawClaimText = AppendRaw(
                claim.RawClaimText,
                string.Join(
                    Environment.NewLine,
                    serviceLines
                        .Select(x => x.RawLineText)
                        .Where(x => !string.IsNullOrWhiteSpace(x))));
        }

        private static void ApplyFinancials(
            HealthNetClaimModel claim,
            HealthNetClaimPageFinancials pageFinancials)
        {
            if (pageFinancials.PreviouslyPaidAmount.HasValue)
            {
                claim.PreviouslyPaidAmount =
                    pageFinancials.PreviouslyPaidAmount;
            }

            if (pageFinancials.TotalOverpayment.HasValue)
            {
                claim.TotalOverpayment =
                    pageFinancials.TotalOverpayment;
            }

            if (pageFinancials.Totals != null
                && HasAnyTotal(pageFinancials.Totals))
            {
                claim.Totals = pageFinancials.Totals;
            }
        }

        private static bool IsFinanciallyComplete(
            HealthNetClaimModel claim)
        {
            return claim.Totals != null
                && HasAnyTotal(claim.Totals);
        }

        private static bool IsSameClaimNumber(
            HealthNetClaimModel current,
            HealthNetClaimModel next)
        {
            var currentClaimNo = NormId(current.ClaimNo);
            var nextClaimNo = NormId(next.ClaimNo);

            if (!string.IsNullOrWhiteSpace(currentClaimNo)
                && !string.IsNullOrWhiteSpace(nextClaimNo))
            {
                return string.Equals(
                    currentClaimNo,
                    nextClaimNo,
                    StringComparison.OrdinalIgnoreCase);
            }

            /*
             * Claim # missing on one side â€” fall back to supporting IDs.
             * Never treat Patient Name alone as identity.
             */
            return IsSameClaimIdentity(current, next);
        }

        private static bool IsSameClaimIdentity(
            HealthNetClaimModel current,
            HealthNetClaimModel next)
        {
            var currentClaimNo = NormId(current.ClaimNo);
            var nextClaimNo = NormId(next.ClaimNo);

            if (!string.IsNullOrWhiteSpace(currentClaimNo)
                && !string.IsNullOrWhiteSpace(nextClaimNo)
                && string.Equals(
                    currentClaimNo,
                    nextClaimNo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var currentAcct = NormId(current.AccountNoRaw);
            var nextAcct = NormId(next.AccountNoRaw);

            var currentSub = NormId(current.SubIdCinRaw);
            var nextSub = NormId(next.SubIdCinRaw);

            var currentName = NormName(current.PatientNameRaw);
            var nextName = NormName(next.PatientNameRaw);

            bool nameMatch =
                !string.IsNullOrWhiteSpace(currentName)
                && !string.IsNullOrWhiteSpace(nextName)
                && string.Equals(
                    currentName,
                    nextName,
                    StringComparison.OrdinalIgnoreCase);

            if (nameMatch
                && !string.IsNullOrWhiteSpace(currentAcct)
                && !string.IsNullOrWhiteSpace(nextAcct)
                && string.Equals(
                    currentAcct,
                    nextAcct,
                    StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * Same patient+acct but DIFFERENT claim numbers
                 * means a new claim after financial completion.
                 * Without claim numbers, treat as continuation.
                 */
                if (string.IsNullOrWhiteSpace(currentClaimNo)
                    || string.IsNullOrWhiteSpace(nextClaimNo)
                    || string.Equals(
                        currentClaimNo,
                        nextClaimNo,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (nameMatch
                && !string.IsNullOrWhiteSpace(currentSub)
                && !string.IsNullOrWhiteSpace(nextSub)
                && string.Equals(
                    currentSub,
                    nextSub,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(currentClaimNo)
                    || string.IsNullOrWhiteSpace(nextClaimNo)
                    || string.Equals(
                        currentClaimNo,
                        nextClaimNo,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var currentNpi = NormId(current.Npi);
            var nextNpi = NormId(next.Npi);

            if (nameMatch
                && !string.IsNullOrWhiteSpace(currentNpi)
                && !string.IsNullOrWhiteSpace(nextNpi)
                && string.Equals(
                    currentNpi,
                    nextNpi,
                    StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(currentClaimNo)
                    || string.IsNullOrWhiteSpace(nextClaimNo)
                    || string.Equals(
                        currentClaimNo,
                        nextClaimNo,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static void MergeRepeatedHeader(
            HealthNetClaimModel current,
            HealthNetClaimModel repeated,
            int pageNumber)
        {
            MergeMissingIdentity(current, repeated);

            if (!current.HeaderPageNumber.HasValue)
                current.HeaderPageNumber = pageNumber;

            current.RawClaimText = AppendRaw(
                current.RawClaimText,
                repeated.RawClaimText);
        }

        private static void MergeMissingIdentity(
            HealthNetClaimModel current,
            HealthNetClaimModel other)
        {
            current.PatientNameRaw ??= other.PatientNameRaw;
            current.AccountNoRaw ??= other.AccountNoRaw;
            current.SubIdCinRaw ??= other.SubIdCinRaw;
            current.ClaimNo ??= other.ClaimNo;
            current.ReceiptDate ??= other.ReceiptDate;
            current.Npi ??= other.Npi;
            current.ValueCodeAmount ??= other.ValueCodeAmount;
        }

        private static string NormId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(
                value.Where(char.IsLetterOrDigit).ToArray())
                .ToUpperInvariant();
        }

        private static string NormName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(
                " ",
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();
        }

        private static decimal CalculateTotalClaimBenefit(
            HealthNetOCRResultViewModel result)
        {
            return result.Claims
                .Where(c => c.Totals != null
                            && c.Totals.BenefitPayable.HasValue)
                .Sum(c => c.Totals.BenefitPayable!.Value);
        }

        private static void AddSummaryCrossCheckWarning(
            HealthNetOCRResultViewModel result)
        {
            var calculatedBenefit = CalculateTotalClaimBenefit(result);

            var pdfPayable = result.Summary?.TotalClaimsPayable;
            var pdfCheck = result.Summary?.TotalCheckAmount;

            if (!pdfPayable.HasValue && !pdfCheck.HasValue)
                return;

            bool mismatch =
                (pdfPayable.HasValue
                 && calculatedBenefit != pdfPayable.Value)
                || (pdfCheck.HasValue
                    && calculatedBenefit != pdfCheck.Value)
                || (pdfPayable.HasValue
                    && pdfCheck.HasValue
                    && pdfPayable.Value != pdfCheck.Value);

            if (!mismatch)
                return;

            result.Warnings.Add(
                "Calculated claim benefit total does not match PDF Total Claims Payable. " +
                $"Calculated={calculatedBenefit:0.00}, " +
                $"TotalClaimsPayable={pdfPayable?.ToString("0.00") ?? "null"}, " +
                $"TotalCheckAmount={pdfCheck?.ToString("0.00") ?? "null"}.");
        }

#if DEBUG
        private static void DumpPreviewDiagnostics(
            HealthNetOCRResultViewModel result,
            ClaimParseState lastState)
        {
            var calculatedBenefit = CalculateTotalClaimBenefit(result);

            System.Diagnostics.Debug.WriteLine(
                "==== HEALTH NET PREVIEW DIAGNOSTICS ====");
            System.Diagnostics.Debug.WriteLine(
                $"DOCUMENT Pages={result.PageCount} Claims={result.Claims.Count} LastState={lastState}");

            foreach (var claim in result.Claims)
            {
                var complete = IsFinanciallyComplete(claim);

                System.Diagnostics.Debug.WriteLine(
                    $"CLAIM {claim.ClaimIndex} " +
                    $"Patient={claim.PatientNameRaw} " +
                    $"Acct={claim.AccountNoRaw} " +
                    $"Claim#={claim.ClaimNo} " +
                    $"HeaderPage={claim.HeaderPageNumber} " +
                    $"ServicePages=[{string.Join(",", claim.ServicePageNumbers)}] " +
                    $"Lines={claim.ServiceLines?.Count ?? 0} " +
                    $"Completed={complete}");

                if (claim.ServiceLines != null)
                {
                    foreach (var line in claim.ServiceLines)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"  DOS={line.ServiceDateFrom:MM/dd/yyyy}-{line.ServiceDateThru:MM/dd/yyyy} | " +
                            $"CPT={line.CPTRev} | " +
                            $"Mods=[{string.Join(",", line.Modifiers)}] | " +
                            $"Billed={line.BilledAmount} NotAllowed={line.AmountNotAllowed} Allowed={line.AllowedAmount} | " +
                            $"COBAllowed={line.CobAllowedAmount} COBPaid={line.CobPaidAmount} | " +
                            $"CostShare={line.PatientCostShareAmount} PtResp={line.PatientResponsibility} Benefit={line.BenefitPayable} | " +
                            $"ExCodes=[{string.Join(",", line.ExCodes)}] QPA={line.QPA}");
                    }
                }

                var t = claim.Totals;
                System.Diagnostics.Debug.WriteLine(
                    $"  TOTALS Billed={t?.BilledAmount} NotAllowed={t?.AmountNotAllowed} Allowed={t?.AllowedAmount} " +
                    $"COBAllowed={t?.CobAllowedAmount} COBPaid={t?.CobPaidAmount} " +
                    $"CostShare={t?.PatientCostShareAmount} PtResp={t?.PatientResponsibility} Benefit={t?.BenefitPayable} " +
                    $"PrevPaid={claim.PreviouslyPaidAmount} Overpay={claim.TotalOverpayment}");
            }

            System.Diagnostics.Debug.WriteLine(
                $"DOCUMENT SUMMARY MemberPlan={result.Summary?.MemberPlanCode} " +
                $"PDF_TotalClaimsPayable={result.Summary?.TotalClaimsPayable} " +
                $"PDF_TotalCheckAmount={result.Summary?.TotalCheckAmount} " +
                $"CalculatedClaimBenefit={calculatedBenefit}");

            System.Diagnostics.Debug.WriteLine(
                $"HEADER Insurance={result.Header?.InsuranceName} " +
                $"InsAddr={result.Header?.InsuranceAddress} " +
                $"SvcProv={result.Header?.ServiceProviderName} " +
                $"ProvAddr={result.Header?.ProviderAddress}");

            System.Diagnostics.Debug.WriteLine(
                "EXPLANATION DEFINITIONS excluded from structured result " +
                $"(Count={result.ExplanationCodes?.Count ?? 0})");
        }
#endif

        private static bool HasAnyTotal(HealthNetClaimTotalsModel totals)
        {
            return totals.BilledAmount.HasValue
                || totals.AmountNotAllowed.HasValue
                || totals.AllowedAmount.HasValue
                || totals.CobAllowedAmount.HasValue
                || totals.CobPaidAmount.HasValue
                || totals.PatientCostShareAmount.HasValue
                || totals.PatientResponsibility.HasValue
                || totals.BenefitPayable.HasValue;
        }

        private static string AppendRaw(
            string? existing,
            string? addition)
        {
            if (string.IsNullOrWhiteSpace(existing))
                return addition ?? string.Empty;

            if (string.IsNullOrWhiteSpace(addition))
                return existing;

            return existing.TrimEnd()
                + Environment.NewLine
                + addition;
        }

        private static string CalculateSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data));
        }

        private static void AddValidationWarnings(
            HealthNetOCRResultViewModel result,
            IReadOnlyList<HealthNetPageData> pages)
        {
            if (string.IsNullOrWhiteSpace(result.Header.RemittanceNo))
            {
                result.Warnings.Add(
                    "Remittance/check number was not detected " +
                    "(REMITTANCE # or CHECK NUMBER).");
            }

            if (!result.Header.ProcessDate.HasValue)
            {
                result.Warnings.Add(
                    "Process/check date was not detected " +
                    "(PROCESS DATE or CHECK DATE).");
            }

            if (result.Claims.Count == 0)
            {
                result.Warnings.Add(
                    "No Health Net claims were detected.");
            }

            foreach (var claim in result.Claims)
            {
                if (string.IsNullOrWhiteSpace(claim.AccountNoRaw)
                    && string.IsNullOrWhiteSpace(claim.SubIdCinRaw))
                {
                    result.Warnings.Add(
                        $"Claim {claim.ClaimNo ?? "(unknown)"} " +
                        "does not contain Account No or SubID/CIN.");
                }

                if (claim.ServiceLines.Count == 0)
                {
                    result.Warnings.Add(
                        $"Claim {claim.ClaimNo ?? "(unknown)"} " +
                        $"(header page {claim.HeaderPageNumber}) " +
                        "contains no parsed service lines.");
                }
            }

            var scannedPages = pages
                .Where(x => x.IsScannedPage && !x.UsedOcrFallback)
                .Select(x => x.PageNumber)
                .ToList();

            if (scannedPages.Count > 0)
            {
                result.Warnings.Add(
                    "Possible scanned pages detected without OCR fallback: " +
                    string.Join(", ", scannedPages));
            }
        }
    }
}
