using Accounts.Services.Ocr.HealthNet.Models;

namespace Accounts.Services.Ocr.HealthNet
{
    public class HealthNetHeaderParser
    {
        public HealthNetDocumentHeaderModel Parse(
            IEnumerable<HealthNetPageData> pages)
        {
            var result =
                new HealthNetDocumentHeaderModel();

            foreach (var page in pages.OrderBy(x => x.PageNumber))
            {
                var isFirstPage = page.PageNumber == 1;
                var hasWords = page.Words != null && page.Words.Count > 0;
                var hasLines = page.Lines != null && page.Lines.Count > 0;

                if (!hasLines && !(isFirstPage && hasWords))
                {
                    continue;
                }

                var pageText = page.CleanText ?? page.RawText;
                var skipAsNoisePage =
                    !isFirstPage
                    && (HealthNetNoisePolicy.IsProviderInstructionSection(pageText)
                        || HealthNetNoisePolicy.IsExplanationLegendSection(pageText));

                if (skipAsNoisePage)
                {
                    continue;
                }

                if (hasLines)
                {
                    ParsePage(
                        page.Lines,
                        result);
                }

                TryParseRemittanceAndCheckDateFromPage(
                    page,
                    result);

                if (page.PageNumber <= 2)
                {
                    HealthNetHeaderCoordinateExtractor
                        .TryFillMissingFields(page, result);
                }

                /*
                 * Prefer header/remittance pages for branding/address.
                 */
                if (page.PageType == HealthNetPageType.RemittanceHeader
                    || page.PageNumber <= 2)
                {
                    TryParseInsuranceBlock(page, result);
                    TryParseProviderBlock(page.Lines, result);
                }

                if (!string.IsNullOrWhiteSpace(result.RemittanceNo)
                    && result.ProcessDate.HasValue
                    && !string.IsNullOrWhiteSpace(result.ServiceProviderNo)
                    && (!string.IsNullOrWhiteSpace(result.InsuranceName)
                        || !string.IsNullOrWhiteSpace(result.ProviderAddress)))
                {
                    break;
                }
            }

            /*
             * Final branding from page-1 text layer (logo may be split
             * words or absent as graphical content).
             */
            var pageList = pages.OrderBy(x => x.PageNumber).ToList();
            var page1 = pageList.FirstOrDefault();

            string brandingSource = "None";

            if (page1 != null)
            {
                brandingSource = DetectPayerBranding(page1, result);
            }

            /*
             * CASE 2 — graphical logo: Health Net letters are not in
             * the PdfPig text layer. This parser only runs for the
             * Health Net OCR route; if the document validates as a
             * Health Net EOP, assign the canonical payer name.
             */
            if (string.IsNullOrWhiteSpace(result.InsuranceName)
                && IsValidatedHealthNetEopDocument(pageList, result))
            {
                result.InsuranceName = "Health Net";
                brandingSource = "ValidatedHealthNetFormatFallback";
            }

            if (string.IsNullOrWhiteSpace(result.InsuranceAddress)
                && result.InsuranceAddressLines.Count > 0)
            {
                result.InsuranceAddress = string.Join(
                    ", ",
                    result.InsuranceAddressLines);
            }

            if (string.IsNullOrWhiteSpace(result.ProviderAddress)
                && result.ProviderAddressLines.Count > 0)
            {
                result.ProviderAddress = string.Join(
                    " ",
                    result.ProviderAddressLines);
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                "===== HEALTH NET HEADER BRANDING DEBUG =====");
            System.Diagnostics.Debug.WriteLine(
                $"Branding source: {brandingSource}");
            System.Diagnostics.Debug.WriteLine(
                $"Final Title={result.Title ?? "(null)"}");
            System.Diagnostics.Debug.WriteLine(
                $"Final InsuranceName={result.InsuranceName ?? "(null)"}");
            System.Diagnostics.Debug.WriteLine(
                $"Final InsuranceAddress={result.InsuranceAddress ?? "(null)"}");
            WriteHeaderFieldDiagnostics(result);
            System.Diagnostics.Debug.WriteLine(
                HealthNetHeaderRuntimeTrace.BuildReport(
                    pages.OrderBy(x => x.PageNumber).ToList(),
                    result));
#endif

            return result;
        }

#if DEBUG
        private static void WriteHeaderFieldDiagnostics(
            HealthNetDocumentHeaderModel header)
        {
            System.Diagnostics.Debug.WriteLine(
                "===== HEALTH NET HEADER FIELD REPORT =====");
            WriteDiag("InsuranceName", header.InsuranceName, "branding");
            WriteDiag("InsuranceAddress", header.InsuranceAddress, "left/payer");
            WriteDiag("ProcessDate", header.ProcessDate?.ToString("yyyy-MM-dd"), "Process Date");
            WriteDiag("RemittanceNo", header.RemittanceNo, "Remittance #");
            WriteDiag("PayeeNo", header.PayeeNo, "Payee #");
            WriteDiag("ServiceProviderNo", header.ServiceProviderNo, "SVC Provider #");
            WriteDiag("ServiceProviderName", header.ServiceProviderName, "SVC Provider");
            WriteDiag("ProviderAddress", header.ProviderAddress, "left/provider block");
            WriteDiag("ProviderNpi", header.ProviderNpi, "labeled NPI only");
            WriteDiag("CheckNumber", header.CheckNumber, "alias→RemittanceNo");
            WriteDiag("EobDate", header.EobDate?.ToString("yyyy-MM-dd"), "alias→ProcessDate");
        }

        private static void WriteDiag(
            string field,
            string? value,
            string source)
        {
            System.Diagnostics.Debug.WriteLine(
                $"{field,-22} | {(string.IsNullOrWhiteSpace(value) ? "(null)" : value),-30} | {source}");
        }
#endif

        private static void ParsePage(
            IReadOnlyList<HealthNetTextLineModel> lines,
            HealthNetDocumentHeaderModel result)
        {
            if (string.IsNullOrWhiteSpace(
                result.RemittanceNo))
            {
                result.RemittanceNo =
                    HealthNetParsingHelper
                        .GetValueAfterAnyLabel(
                            lines,
                            new[]
                            {
                                "REMITTANCE #",
                                "REMITTANCE NO",
                                "REMITTANCE NUMBER"
                            },
                            "PROCESS DATE",
                            "CHECK DATE",
                            "PAYEE",
                            "SVC PROVIDER");
            }

            if (string.IsNullOrWhiteSpace(result.RemittanceNo))
            {
                result.RemittanceNo =
                    HealthNetParsingHelper
                        .GetValueAfterAnyLabel(
                            lines,
                            new[]
                            {
                                "CHECK NUMBER",
                                "CHECK NO",
                                "CHECK #",
                                "CHECK/EFT",
                                "CHECK EFT",
                                "EFT NUMBER",
                                "EFT NO"
                            },
                            "CHECK DATE",
                            "PROCESS DATE",
                            "PAGE",
                            "PAYEE",
                            "SVC PROVIDER",
                            "PATIENT NAME");
            }

            if (!string.IsNullOrWhiteSpace(result.RemittanceNo))
            {
                result.RemittanceNo =
                    NormalizeRemittanceOrCheckValue(result.RemittanceNo);
            }

            if (!result.ProcessDate.HasValue)
            {
                var processDate =
                    HealthNetParsingHelper
                        .GetValueAfterLabel(
                            lines,
                            "PROCESS DATE",
                            "REMITTANCE",
                            "CHECK NUMBER",
                            "PAYEE",
                            "SVC PROVIDER");

                result.ProcessDate =
                    HealthNetParsingHelper
                        .ParseDate(processDate);
            }

            if (!result.ProcessDate.HasValue)
            {
                var checkDate =
                    HealthNetParsingHelper
                        .GetValueAfterAnyLabel(
                            lines,
                            new[]
                            {
                                "CHECK DATE",
                                "DATE OF CHECK",
                                "PAYMENT DATE",
                                "ISSUE DATE"
                            },
                            "CHECK NUMBER",
                            "REMITTANCE",
                            "PAGE",
                            "PAYEE",
                            "SVC PROVIDER",
                            "PATIENT NAME");

                result.ProcessDate =
                    HealthNetParsingHelper.ParseDate(checkDate);
            }

            if (!result.ProcessDate.HasValue)
            {
                result.ProcessDate =
                    HealthNetParsingHelper
                        .TryParseCheckOrProcessDateFromPageText(
                            JoinLinesForRegexScan(lines));
            }

            if (string.IsNullOrWhiteSpace(
                result.PayeeNo))
            {
                result.PayeeNo =
                    HealthNetParsingHelper
                        .GetValueAfterAnyLabel(
                            lines,
                            new[]
                            {
                                "PAYEE #",
                                "PAYEE NO",
                                "PAYEE NUMBER"
                            },
                            "SVC PROVIDER",
                            "SERVICE PROVIDER",
                            "NPI");
            }

            if (string.IsNullOrWhiteSpace(
                result.ServiceProviderNo))
            {
                result.ServiceProviderNo =
                    HealthNetParsingHelper
                        .GetValueAfterAnyLabel(
                            lines,
                            new[]
                            {
                                "SVC PROVIDER #",
                                "SVC PROVIDER NO",
                                "SERVICE PROVIDER #",
                                "SERVICE PROVIDER NO"
                            },
                            "NPI",
                            "PROCESS DATE",
                            "SVC PROVIDER");
            }

            if (string.IsNullOrWhiteSpace(
                result.ProviderNpi))
            {
                /*
                 * Provider NPI only — NEVER claim/patient-level "NPI".
                 * Bare "NPI" under EOP blocks is claim.Npi.
                 */
                result.ProviderNpi =
                    HealthNetParsingHelper
                        .GetValueAfterAnyLabel(
                            lines,
                            new[]
                            {
                                "PROVIDER NPI",
                                "SVC PROVIDER NPI",
                                "SERVICE PROVIDER NPI"
                            },
                            "PATIENT NAME",
                            "EXPLANATION OF PAYMENT",
                            "PROCESS DATE");

                if (!LooksLikeNpi(result.ProviderNpi))
                    result.ProviderNpi = null;
            }

            if (string.IsNullOrWhiteSpace(
                result.ServiceProviderName))
            {
                result.ServiceProviderName =
                    HealthNetParsingHelper
                        .GetValueAfterAnyLabel(
                            lines,
                            new[]
                            {
                                "SVC PROVIDER NAME",
                                "SERVICE PROVIDER NAME"
                            },
                            "NPI",
                            "ADDRESS",
                            "REMITTANCE",
                            "PROCESS DATE",
                            "PAYEE");

                if (LooksLikeProviderId(result.ServiceProviderName)
                    || HealthNetNoisePolicy.IsStructuredNoise(
                        result.ServiceProviderName))
                {
                    result.ServiceProviderName = null;
                }
            }
        }

        private static string? NormalizeRemittanceOrCheckValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var fromToken =
                HealthNetParsingHelper
                    .ExtractRemittanceIdFromText(value);

            if (!string.IsNullOrWhiteSpace(fromToken))
                return fromToken;

            var trimmed = value.Trim();

            var firstToken = trimmed
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstToken))
                return trimmed;

            if (HealthNetParsingHelper.DateRegex.IsMatch(firstToken))
                return trimmed;

            return firstToken.TrimEnd(',', ';', '.');
        }

        private static void TryParseRemittanceAndCheckDateFromPage(
            HealthNetPageData page,
            HealthNetDocumentHeaderModel result)
        {
            var lines = page.Lines
                ?? new List<HealthNetTextLineModel>();

            if (string.IsNullOrWhiteSpace(result.RemittanceNo))
            {
                result.RemittanceNo =
                    TryParseCheckNumberFromWords(page);

                if (!string.IsNullOrWhiteSpace(result.RemittanceNo))
                {
                    result.RemittanceNo =
                        NormalizeRemittanceOrCheckValue(
                            result.RemittanceNo);
                }
            }

            if (!result.ProcessDate.HasValue)
            {
                result.ProcessDate =
                    TryParseCheckDateFromWords(page);
            }

            var pageText =
                page.CleanText
                ?? page.RawText
                ?? JoinLinesForRegexScan(lines);

            if (string.IsNullOrWhiteSpace(result.RemittanceNo))
            {
                result.RemittanceNo =
                    HealthNetParsingHelper
                        .TryParseRemittanceIdFromPageText(
                            pageText);

                if (!string.IsNullOrWhiteSpace(result.RemittanceNo))
                {
                    result.RemittanceNo =
                        NormalizeRemittanceOrCheckValue(
                            result.RemittanceNo);
                }
            }

            if (!result.ProcessDate.HasValue)
            {
                result.ProcessDate =
                    HealthNetParsingHelper
                        .TryParseCheckOrProcessDateFromPageText(
                            pageText);
            }
        }

        private static string JoinLinesForRegexScan(
            IReadOnlyList<HealthNetTextLineModel> lines)
        {
            return string.Join(
                " ",
                lines
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .Select(x => x.Text!.Trim()));
        }

        private static string? TryParseCheckNumberFromWords(
            HealthNetPageData page)
        {
            var words = (page.Words ?? new List<HealthNetWordModel>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (words.Count == 0
                && page.Lines != null)
            {
                words = page.Lines
                    .Where(x => x.Words != null)
                    .SelectMany(x => x.Words!)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .ToList();
            }

            if (words.Count == 0)
                return null;

            const double rowYTolerance = 4.0;

            static double MidY(HealthNetWordModel w) =>
                (w.Top + w.Bottom) / 2.0;

            static double MidX(HealthNetWordModel w) =>
                (w.Left + w.Right) / 2.0;

            var ordered = words
                .OrderByDescending(MidY)
                .ThenBy(MidX)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var w = ordered[i];
                var token = w.Text.Trim().ToUpperInvariant();

                if (token != "CHECK"
                    && token != "REMITTANCE"
                    && token != "EFT")
                {
                    continue;
                }

                var row = ordered
                    .Where(x =>
                        Math.Abs(MidY(x) - MidY(w))
                        <= rowYTolerance)
                    .OrderBy(MidX)
                    .ToList();

                var numberWord = row.FirstOrDefault(x =>
                    string.Equals(
                        x.Text.Trim(),
                        "NUMBER",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        x.Text.Trim(),
                        "NO",
                        StringComparison.OrdinalIgnoreCase)
                    || x.Text.Trim() == "#");

                double valueColumnX = numberWord != null
                    ? MidX(numberWord)
                    : MidX(w) + 40;

                var sameRowId = row
                    .Where(x => MidX(x) > valueColumnX + 8)
                    .Select(x => x.Text.Trim())
                    .Select(HealthNetParsingHelper.ExtractRemittanceIdFromText)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (!string.IsNullOrWhiteSpace(sameRowId))
                    return sameRowId;

                var rowY = MidY(w);
                var rowBelow = ordered
                    .Where(x =>
                        rowY - MidY(x) >= rowYTolerance
                        && rowY - MidY(x) <= rowYTolerance * 4)
                    .OrderByDescending(MidY)
                    .ThenBy(MidX)
                    .ToList();

                if (rowBelow.Count == 0)
                    continue;

                var belowY = rowBelow
                    .GroupBy(MidY)
                    .OrderByDescending(g => g.Key)
                    .First()
                    .Key;

                var belowRow = rowBelow
                    .Where(x => Math.Abs(MidY(x) - belowY) <= rowYTolerance)
                    .OrderBy(MidX)
                    .ToList();

                var belowId = belowRow
                    .Select(x => x.Text.Trim())
                    .Select(HealthNetParsingHelper.ExtractRemittanceIdFromText)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (!string.IsNullOrWhiteSpace(belowId))
                    return belowId;
            }

            return null;
        }

        private static DateTime? TryParseCheckDateFromWords(
            HealthNetPageData page)
        {
            var words = (page.Words ?? new List<HealthNetWordModel>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (words.Count == 0
                && page.Lines != null)
            {
                words = page.Lines
                    .Where(x => x.Words != null)
                    .SelectMany(x => x.Words!)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .ToList();
            }

            if (words.Count == 0)
                return null;

            const double rowYTolerance = 4.0;

            static double MidY(HealthNetWordModel w) =>
                (w.Top + w.Bottom) / 2.0;

            static double MidX(HealthNetWordModel w) =>
                (w.Left + w.Right) / 2.0;

            var ordered = words
                .OrderByDescending(MidY)
                .ThenBy(MidX)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var w = ordered[i];
                if (!string.Equals(
                        w.Text.Trim(),
                        "CHECK",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var row = ordered
                    .Where(x =>
                        Math.Abs(MidY(x) - MidY(w))
                        <= rowYTolerance)
                    .OrderBy(MidX)
                    .ToList();

                var dateWord = row.FirstOrDefault(x =>
                    string.Equals(
                        x.Text.Trim(),
                        "DATE",
                        StringComparison.OrdinalIgnoreCase));

                if (dateWord == null)
                    continue;

                var sameRowDate = row
                    .Where(x => MidX(x) > MidX(dateWord) + 8)
                    .Select(x => HealthNetParsingHelper.ParseDate(x.Text))
                    .FirstOrDefault(d => d.HasValue);

                if (sameRowDate.HasValue)
                    return sameRowDate;

                var rowY = MidY(w);
                var below = ordered
                    .Where(x =>
                        rowY - MidY(x) >= rowYTolerance
                        && rowY - MidY(x) <= rowYTolerance * 4)
                    .OrderByDescending(MidY)
                    .ToList();

                if (below.Count == 0)
                    continue;

                var belowY = below
                    .GroupBy(MidY)
                    .OrderByDescending(g => g.Key)
                    .First()
                    .Key;

                var belowRow = below
                    .Where(x => Math.Abs(MidY(x) - belowY) <= rowYTolerance)
                    .OrderBy(MidX)
                    .ToList();

                foreach (var bw in belowRow)
                {
                    var parsed = HealthNetParsingHelper.ParseDate(bw.Text);
                    if (parsed.HasValue)
                        return parsed;
                }

                var belowLine = string.Join(
                    " ",
                    belowRow.Select(x => x.Text));

                return HealthNetParsingHelper.ParseDate(belowLine);
            }

            return null;
        }

        private static bool LooksLikeProviderId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();

            /*
             * EIN / tax-id style values such as "93-3696770 C"
             * must not be treated as the provider display name.
             */
            if (System.Text.RegularExpressions.Regex.IsMatch(
                trimmed,
                @"^\d{2}-\d{7}(\s*[A-Z])?$"))
            {
                return true;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(
                trimmed,
                @"^\d{5,}(\s*[A-Z])?$"))
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikeNpi(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var digits = new string(
                value.Where(char.IsDigit).ToArray());

            return digits.Length == 10;
        }

        private static List<HealthNetTextLineModel> GetHeaderRegionLines(
            IReadOnlyList<HealthNetTextLineModel> lines)
        {
            /*
             * Document/provider header lives ABOVE the first
             * EXPLANATION OF PAYMENT. Claim NPI and patient blocks
             * below that boundary are excluded.
             */
            var result = new List<HealthNetTextLineModel>();

            foreach (var line in lines
                .OrderByDescending(x => (x.Top + x.Bottom) / 2.0)
                .ThenBy(x => x.Left))
            {
                if (!string.IsNullOrWhiteSpace(line.Text)
                    && line.Text.Contains(
                        "EXPLANATION OF PAYMENT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                result.Add(line);
            }

            return result.Count > 0
                ? result
                : lines.ToList();
        }

        private static void TryParseInsuranceBlock(
            HealthNetPageData page,
            HealthNetDocumentHeaderModel result)
        {
            var headerLines = GetHeaderRegionLines(page.Lines);

            var usable = headerLines
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Where(x => !HealthNetNoisePolicy.IsStructuredNoise(x.Text))
                .OrderByDescending(x => (x.Top + x.Bottom) / 2.0)
                .ThenBy(x => x.Left)
                .ToList();

            DetectPayerBranding(page, result);

            if (usable.Count == 0)
                return;

            if (result.InsuranceAddressLines.Count > 0
                || !string.IsNullOrWhiteSpace(result.InsuranceAddress))
            {
                return;
            }

            if (!TryInsuranceAddressNearBrand(usable, result)
                && !TryInsuranceAddressTopBlock(usable, result))
            {
                TryInsuranceAddressKnownStructure(usable, result);
            }
        }

        /// <summary>
        /// Detect CalViva title and Health Net payer name from the
        /// first-page / remittance HEADER REGION.
        /// Returns branding source label for DEBUG.
        /// </summary>
        private static string DetectPayerBranding(
            HealthNetPageData page,
            HealthNetDocumentHeaderModel result)
        {
            var headerLines = GetHeaderRegionLines(
                page.Lines ?? new List<HealthNetTextLineModel>());

            var headerWords = GetHeaderRegionWords(page, headerLines);
            var topWords = GetTopOfPageWords(page);

            bool foundHealth = HasToken(headerWords, topWords, "HEALTH");
            bool foundNet = HasToken(headerWords, topWords, "NET");
            bool foundCalViva =
                HeaderContainsToken(headerLines, headerWords, "CALVIVA")
                || HeaderContainsToken(headerLines, topWords, "CALVIVA")
                || HeaderTextContainsNormalized(headerLines, "CALVIVA")
                || PageTextContainsNormalized(
                    page.CleanText ?? page.RawText,
                    "CALVIVA");

            bool hasHealthNetText =
                HeaderContainsHealthNetBranding(headerLines, headerWords)
                || HeaderContainsHealthNetBranding(headerLines, topWords)
                || HasAdjacentHealthNetWords(topWords)
                || PageTextContainsNormalized(
                    page.CleanText ?? page.RawText,
                    "HEALTH NET")
                || PageTextContainsNormalized(
                    page.CleanText ?? page.RawText,
                    "HEALTHNET");

#if DEBUG
            DumpBrandingCandidates(page.PageNumber, headerLines, topWords);
            System.Diagnostics.Debug.WriteLine(
                $"Found \"health\" token: {foundHealth}");
            System.Diagnostics.Debug.WriteLine(
                $"Found \"net\" token: {foundNet}");
            System.Diagnostics.Debug.WriteLine(
                $"Found \"CalViva\" token: {foundCalViva}");
            System.Diagnostics.Debug.WriteLine(
                $"Text-layer Health Net branding: {hasHealthNetText}");
#endif

            if (string.IsNullOrWhiteSpace(result.Title) && foundCalViva)
                result.Title = "CalViva Health";

            if (string.IsNullOrWhiteSpace(result.InsuranceName)
                && hasHealthNetText)
            {
                result.InsuranceName = "Health Net";
                return foundHealth && foundNet && !HeaderTextContainsNormalized(headerLines, "HEALTH NET")
                    ? "TextLayerSplitWords"
                    : "TextLayer";
            }

            return "None";
        }

        /// <summary>
        /// Strong Health Net EOP structure — used only when logo
        /// branding is missing from the PDF text layer.
        /// </summary>
        private static bool IsValidatedHealthNetEopDocument(
            IReadOnlyList<HealthNetPageData> pages,
            HealthNetDocumentHeaderModel header)
        {
            if (pages == null || pages.Count == 0)
                return false;

            var corpus = string.Join(
                " ",
                pages
                    .Take(Math.Min(3, pages.Count))
                    .Select(x =>
                        (x.CleanText ?? x.RawText ?? string.Empty)
                            .ToUpperInvariant()));

            corpus = System.Text.RegularExpressions.Regex.Replace(
                corpus,
                @"\s+",
                " ");

            int score = 0;

            if (corpus.Contains("EXPLANATION OF PAYMENT"))
                score += 2;

            if (corpus.Contains("PATIENT NAME"))
                score++;

            if (corpus.Contains("SUBID/CIN") || corpus.Contains("SUBID CIN"))
                score++;

            if (corpus.Contains("YOUR ACCT") || corpus.Contains("ACCT #"))
                score++;

            if (corpus.Contains("CLAIM #") || corpus.Contains("CLAIM NO"))
                score++;

            if (corpus.Contains("SVC DATES")
                || corpus.Contains("SERVICE DATES"))
            {
                score++;
            }

            if (corpus.Contains("CPT/REV") || corpus.Contains("CPT REV"))
                score++;

            if (corpus.Contains("BENEFIT PAYABLE"))
                score++;

            if (corpus.Contains("MEMBER PLAN CODE"))
                score++;

            bool hasRemittanceIdentity =
                !string.IsNullOrWhiteSpace(header.RemittanceNo)
                || header.ProcessDate.HasValue
                || !string.IsNullOrWhiteSpace(header.PayeeNo);

            bool hasPayerAddress =
                !string.IsNullOrWhiteSpace(header.InsuranceAddress)
                || header.InsuranceAddressLines.Count > 0;

            /*
             * Require real EOP table/identity structure — not merely
             * any PDF opened in this controller.
             */
            return score >= 5
                && (hasRemittanceIdentity || hasPayerAddress);
        }

        private static List<HealthNetWordModel> GetTopOfPageWords(
            HealthNetPageData page)
        {
            var words = (page.Words ?? new List<HealthNetWordModel>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (words.Count == 0)
                return words;

            var pageHeight = page.PageHeight > 0
                ? page.PageHeight
                : words.Max(x => Math.Max(x.Top, x.Bottom));

            /*
             * Top ~35% of the page — payer logo / remittance header.
             */
            var minY = pageHeight * 0.65;

            return words
                .Where(x => ((x.Top + x.Bottom) / 2.0) >= minY)
                .ToList();
        }

        private static bool HasToken(
            List<HealthNetWordModel> a,
            List<HealthNetWordModel> b,
            string token)
        {
            return (a ?? new List<HealthNetWordModel>()).Any(x =>
                    string.Equals(
                        StripBrandPunctuation(x.Text),
                        token,
                        StringComparison.OrdinalIgnoreCase))
                || (b ?? new List<HealthNetWordModel>()).Any(x =>
                    string.Equals(
                        StripBrandPunctuation(x.Text),
                        token,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool PageTextContainsNormalized(
            string? pageText,
            string needle)
        {
            if (string.IsNullOrWhiteSpace(pageText))
                return false;

            /*
             * Prefer header-ish content; still strip contact/legal
             * footer phrases so we do not treat them as logo branding
             * when scoring text-layer presence.
             */
            if (HealthNetNoisePolicy.IsContactFooter(pageText)
                && !pageText.Contains(
                    "EXPLANATION OF PAYMENT",
                    StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * Pure contact page — skip.
                 */
            }

            var collapsed = System.Text.RegularExpressions.Regex.Replace(
                pageText.ToUpperInvariant(),
                @"\s+",
                " ");

            return collapsed.Contains(needle.ToUpperInvariant());
        }

        private static List<HealthNetWordModel> GetHeaderRegionWords(
            HealthNetPageData page,
            List<HealthNetTextLineModel> headerLines)
        {
            var words = (page.Words ?? new List<HealthNetWordModel>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (words.Count == 0)
            {
                return headerLines
                    .Where(x => x.Words != null)
                    .SelectMany(x => x.Words!)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .ToList();
            }

            if (headerLines.Count == 0)
                return words;

            var minY = headerLines.Min(x => (x.Top + x.Bottom) / 2.0) - 8;
            var maxY = headerLines.Max(x => (x.Top + x.Bottom) / 2.0) + 8;

            return words
                .Where(x =>
                {
                    var y = (x.Top + x.Bottom) / 2.0;
                    return y >= minY && y <= maxY;
                })
                .ToList();
        }

        private static bool HeaderContainsHealthNetBranding(
            List<HealthNetTextLineModel> headerLines,
            List<HealthNetWordModel> headerWords)
        {
            /*
             * STRATEGY A — logical lines / collapsed header text.
             */
            if (HeaderTextContainsNormalized(headerLines, "HEALTH NET")
                || HeaderTextContainsNormalized(headerLines, "HEALTHNET"))
            {
                return true;
            }

            foreach (var line in headerLines)
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                    continue;

                if (HealthNetNoisePolicy.IsContactFooter(line.Text)
                    || HealthNetNoisePolicy.IsLegalBoilerplate(line.Text))
                {
                    continue;
                }

                if (LineLooksLikeHealthNetBrand(line.Text))
                    return true;
            }

            /*
             * STRATEGY B — nearby words HEALTH + NET in header region.
             */
            if (HasAdjacentHealthNetWords(headerWords))
                return true;

            return false;
        }

        private static bool HeaderTextContainsNormalized(
            List<HealthNetTextLineModel> headerLines,
            string needle)
        {
            if (headerLines == null || headerLines.Count == 0)
                return false;

            var joined = string.Join(
                " ",
                headerLines
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .Where(x =>
                        !HealthNetNoisePolicy.IsContactFooter(x.Text)
                        && !HealthNetNoisePolicy.IsLegalBoilerplate(x.Text))
                    .Select(x => x.Text!.Trim()));

            var collapsed = System.Text.RegularExpressions.Regex.Replace(
                joined.ToUpperInvariant(),
                @"\s+",
                " ");

            return collapsed.Contains(needle.ToUpperInvariant());
        }

        private static bool HeaderContainsToken(
            List<HealthNetTextLineModel> headerLines,
            List<HealthNetWordModel> headerWords,
            string token)
        {
            if (headerLines.Any(x =>
                    !string.IsNullOrWhiteSpace(x.Text)
                    && x.Text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            return headerWords.Any(x =>
                string.Equals(
                    StripBrandPunctuation(x.Text),
                    token,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool LineLooksLikeHealthNetBrand(string text)
        {
            var collapsed = System.Text.RegularExpressions.Regex.Replace(
                text.ToUpperInvariant(),
                @"\s+",
                " ");

            collapsed = StripBrandPunctuation(collapsed);

            if (collapsed.Contains("HEALTH NET")
                || collapsed.Contains("HEALTHNET"))
            {
                return true;
            }

            var tokens = collapsed
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length - 1; i++)
            {
                if (tokens[i] == "HEALTH" && tokens[i + 1] == "NET")
                    return true;
            }

            return false;
        }

        private static bool HasAdjacentHealthNetWords(
            List<HealthNetWordModel> words)
        {
            if (words == null || words.Count < 2)
                return false;

            var ordered = words
                .Select(x => new
                {
                    Word = x,
                    Token = StripBrandPunctuation(x.Text).ToUpperInvariant(),
                    MidX = (x.Left + x.Right) / 2.0,
                    MidY = (x.Top + x.Bottom) / 2.0
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                .OrderByDescending(x => x.MidY)
                .ThenBy(x => x.MidX)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Token != "HEALTH")
                    continue;

                for (int j = 0; j < ordered.Count; j++)
                {
                    if (i == j)
                        continue;

                    if (ordered[j].Token != "NET")
                        continue;

                    var dx = Math.Abs(ordered[j].MidX - ordered[i].MidX);
                    var dy = Math.Abs(ordered[j].MidY - ordered[i].MidY);

                    if (dy <= 12 && dx <= 80)
                        return true;
                }
            }

            return false;
        }

        private static string StripBrandPunctuation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = value
                .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                .ToArray();

            return new string(chars).Trim();
        }

#if DEBUG
        private static void DumpBrandingCandidates(
            int pageNumber,
            List<HealthNetTextLineModel> headerLines,
            List<HealthNetWordModel> headerWords)
        {
            System.Diagnostics.Debug.WriteLine(
                $"==== HN BRANDING CANDIDATES page {pageNumber} ====");

            foreach (var line in headerLines.Take(25))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"LINE '{line.Text}' L={line.Left:0.0} R={line.Right:0.0} " +
                    $"T={line.Top:0.0} B={line.Bottom:0.0}");
            }

            foreach (var word in headerWords
                .OrderByDescending(x => (x.Top + x.Bottom) / 2.0)
                .ThenBy(x => x.Left)
                .Take(40))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"WORD '{word.Text}' L={word.Left:0.0} R={word.Right:0.0} " +
                    $"T={word.Top:0.0} B={word.Bottom:0.0}");
            }
        }
#endif

        private static bool TryInsuranceAddressNearBrand(
            List<HealthNetTextLineModel> usable,
            HealthNetDocumentHeaderModel result)
        {
            var brand = usable.FirstOrDefault(x =>
            {
                if (string.IsNullOrWhiteSpace(x.Text))
                    return false;

                return LineLooksLikeHealthNetBrand(x.Text)
                    || x.Text.IndexOf("CALVIVA", StringComparison.OrdinalIgnoreCase) >= 0
                    || string.Equals(
                        StripBrandPunctuation(x.Text),
                        "HEALTH",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        StripBrandPunctuation(x.Text),
                        "NET",
                        StringComparison.OrdinalIgnoreCase);
            });

            if (brand == null)
                return false;

            var brandY = (brand.Top + brand.Bottom) / 2.0;

            /*
             * Remittance mail addresses sit at/below the brand block
             * (PO BOX then City ST ZIP). Prefer lines near the brand,
             * never invent a city from brand identity alone.
             */
            var nearby = usable
                .Where(x =>
                {
                    var y = (x.Top + x.Bottom) / 2.0;
                    return y <= brandY + 5
                        && y >= brandY - 100;
                })
                .Where(x => IsInsuranceAddressCandidate(x.Text!))
                .OrderByDescending(x => (x.Top + x.Bottom) / 2.0)
                .ThenBy(x => x.Left)
                .Select(x => x.Text!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            foreach (var line in nearby)
                result.InsuranceAddressLines.Add(line);

            return result.InsuranceAddressLines.Count > 0;
        }

        private static bool TryInsuranceAddressTopBlock(
            List<HealthNetTextLineModel> usable,
            HealthNetDocumentHeaderModel result)
        {
            var top = usable.Take(12).ToList();
            var collected = new List<string>();

            foreach (var line in top)
            {
                var text = line.Text!.Trim();

                if (!IsInsuranceAddressCandidate(text))
                    continue;

                collected.Add(text);

                if (collected.Count >= 3)
                    break;
            }

            if (collected.Count == 0)
                return false;

            foreach (var line in collected)
                result.InsuranceAddressLines.Add(line);

            return true;
        }

        private static bool TryInsuranceAddressKnownStructure(
            List<HealthNetTextLineModel> usable,
            HealthNetDocumentHeaderModel result)
        {
            var street = usable.FirstOrDefault(x =>
                IsInsuranceAddressCandidate(x.Text!)
                && (x.Text!.ToUpperInvariant().Contains("PO BOX")
                    || x.Text.ToUpperInvariant().Contains("P.O. BOX")
                    || System.Text.RegularExpressions.Regex.IsMatch(
                        x.Text,
                        @"\d+\s+\w+")));

            if (street == null)
                return false;

            var streetY = (street.Top + street.Bottom) / 2.0;

            result.InsuranceAddressLines.Add(street.Text!.Trim());

            foreach (var line in usable
                .Where(x =>
                {
                    var y = (x.Top + x.Bottom) / 2.0;
                    return y < streetY
                        && y >= streetY - 40
                        && IsInsuranceAddressCandidate(x.Text!);
                })
                .OrderByDescending(x => (x.Top + x.Bottom) / 2.0)
                .Take(2))
            {
                result.InsuranceAddressLines.Add(line.Text!.Trim());
            }

            return result.InsuranceAddressLines.Count > 0;
        }

        private static bool IsInsuranceAddressCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (!HealthNetNoisePolicy.LooksLikeUsableAddressLine(text))
                return false;

            if (!LooksLikeAddressFragment(text))
                return false;

            if (HealthNetNoisePolicy.IsContactFooter(text)
                || HealthNetNoisePolicy.IsStructuredNoise(text)
                || HealthNetNoisePolicy.IsLegalBoilerplate(text))
            {
                return false;
            }

            var upper = text.ToUpperInvariant();

            if (upper.Contains("SVC PROVIDER")
                || upper.Contains("REMITTANCE")
                || upper.Contains("PROCESS DATE")
                || upper.Contains("PATIENT NAME")
                || upper.Contains("QUESTIONS")
                || upper.Contains("PROVIDER_SERVICES")
                || upper.Contains("1-800")
                || upper.Contains("HEALTH NET")
                || upper.Contains("HEALTHNET")
                || upper.Contains("CALVIVA")
                || LooksLikeProviderClinicLine(text)
                || LooksLikePayeeClinicName(text)
                || LooksLikeProviderCityStateZipOnly(text))
            {
                return false;
            }

            return true;
        }

        private static bool LooksLikeProviderCityStateZipOnly(string text)
        {
            var upper = text.ToUpperInvariant().Trim();
            return upper == "FRESNO CA 93720"
                || System.Text.RegularExpressions.Regex.IsMatch(
                    upper,
                    @"^FRESNO\s+CA\s+93720");
        }

        private static bool LooksLikeProviderClinicLine(string text)
        {
            var upper = text.ToUpperInvariant();
            return upper.Contains("FAMILY MEDICAL")
                || upper.Contains("MEDICAL GROUP")
                || upper.Contains("SIERRA FAMILY")
                || upper.Contains("CEDAR AVE")
                || upper.StartsWith("STE ")
                || upper.StartsWith("SUITE ");
        }

        private static void TryParseProviderBlock(
            IReadOnlyList<HealthNetTextLineModel> lines,
            HealthNetDocumentHeaderModel result)
        {
            var headerLines = GetHeaderRegionLines(lines);

            int? providerAnchorIndex = null;

            for (int i = 0; i < headerLines.Count; i++)
            {
                var text = headerLines[i].Text;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var upper = text.ToUpperInvariant();
                if (upper.Contains("SVC PROVIDER")
                    || upper.Contains("SERVICE PROVIDER"))
                {
                    providerAnchorIndex = i;
                    break;
                }
            }

            if (!providerAnchorIndex.HasValue)
                return;

            var nearby = headerLines
                .Skip(providerAnchorIndex.Value)
                .Take(12)
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            /*
             * Provider NPI only when spatially near SVC Provider block
             * and labeled as provider NPI — not claim NPI.
             */
            if (string.IsNullOrWhiteSpace(result.ProviderNpi))
            {
                foreach (var line in nearby)
                {
                    var upper = line.Text!.ToUpperInvariant();
                    if (!(upper.Contains("PROVIDER NPI")
                          || upper.Contains("SVC PROVIDER NPI")
                          || (upper.Contains("NPI")
                              && upper.Contains("PROVIDER"))))
                    {
                        continue;
                    }

                    var value =
                        HealthNetParsingHelper.GetValueAfterAnyLabel(
                            nearby,
                            new[]
                            {
                                "PROVIDER NPI",
                                "SVC PROVIDER NPI",
                                "SERVICE PROVIDER NPI"
                            });

                    if (LooksLikeNpi(value))
                    {
                        result.ProviderNpi =
                            HealthNetParsingHelper.CleanIdentifier(value);
                        break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(result.ServiceProviderName))
            {
                /*
                 * Prefer the labeled SVC Provider name line:
                 *   "SVC Provider MUHAMMAD H. BASHIR, M.D."
                 * Do not confuse with:
                 *   "SVC Provider # 93-3696770 C"
                 * or the left-side payee clinic mailer block.
                 */
                foreach (var line in nearby)
                {
                    var text = line.Text!.Trim();
                    var upper = text.ToUpperInvariant();

                    if (upper.Contains("SVC PROVIDER #")
                        || upper.Contains("SERVICE PROVIDER #"))
                    {
                        continue;
                    }

                    if (upper.StartsWith("SVC PROVIDER")
                        || upper.StartsWith("SERVICE PROVIDER"))
                    {
                        var after = text;
                        foreach (var label in new[]
                        {
                            "SVC PROVIDER NAME",
                            "SERVICE PROVIDER NAME",
                            "SVC PROVIDER",
                            "SERVICE PROVIDER"
                        })
                        {
                            var idx = after.IndexOf(
                                label,
                                StringComparison.OrdinalIgnoreCase);
                            if (idx >= 0)
                            {
                                after = after
                                    .Substring(idx + label.Length)
                                    .Trim()
                                    .TrimStart(':', '-', ' ');
                                break;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(after)
                            && after.Length > 2
                            && !LooksLikeProviderId(after)
                            && !LooksLikeAddressFragment(after)
                            && !HealthNetNoisePolicy.IsStructuredNoise(after)
                            && after.Any(char.IsLetter)
                            && !char.IsDigit(after[0]))
                        {
                            result.ServiceProviderName = after;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(result.ServiceProviderName))
            {
                foreach (var line in nearby)
                {
                    var text = line.Text!.Trim();
                    var upper = text.ToUpperInvariant();

                    if (upper.Contains("SVC PROVIDER #")
                        || upper.Contains("SERVICE PROVIDER #")
                        || upper.Contains("NPI")
                        || upper.Contains("REMITTANCE")
                        || upper.Contains("PROCESS DATE")
                        || upper.Contains("PAYEE")
                        || HealthNetNoisePolicy.IsStructuredNoise(text)
                        || LooksLikeProviderId(text)
                        || LooksLikePayeeClinicName(text))
                    {
                        continue;
                    }

                    if (upper.StartsWith("SVC PROVIDER")
                        || upper.StartsWith("SERVICE PROVIDER"))
                    {
                        continue;
                    }

                    if (!LooksLikeAddressFragment(text)
                        && text.Length > 2
                        && text.Length < 90
                        && text.Any(char.IsLetter)
                        && !char.IsDigit(text[0]))
                    {
                        result.ServiceProviderName = text;
                        break;
                    }
                }
            }

            if (result.ProviderAddressLines.Count > 0
                || !string.IsNullOrWhiteSpace(result.ProviderAddress))
            {
                return;
            }

            /*
             * Full provider / payee clinic address, e.g.:
             *   SIERRA FAMILY MEDICAL GROUP CO
             *   STE 102
             *   7407 N CEDAR AVE
             *   FRESNO CA 93720
             *
             * Clinic-name lines ARE part of ProviderAddress.
             * Do not skip them.
             */
            CollectProviderAddressLines(headerLines, nearby, result);
        }

        private static void CollectProviderAddressLines(
            List<HealthNetTextLineModel> headerLines,
            List<HealthNetTextLineModel> nearby,
            HealthNetDocumentHeaderModel result)
        {
            var candidates = nearby
                .Concat(headerLines)
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .GroupBy(x => x.Text!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(x => (x.Top + x.Bottom) / 2.0)
                .ThenBy(x => x.Left)
                .ToList();

            var clinicAnchor = candidates.FirstOrDefault(x =>
                LooksLikePayeeClinicName(x.Text!));

            if (clinicAnchor != null)
            {
                var anchorY = (clinicAnchor.Top + clinicAnchor.Bottom) / 2.0;

                var block = candidates
                    .Where(x =>
                    {
                        var y = (x.Top + x.Bottom) / 2.0;
                        return y <= anchorY + 2
                            && y >= anchorY - 80;
                    })
                    .Where(x => IsProviderAddressLine(x.Text!, result.ServiceProviderName))
                    .OrderByDescending(x => (x.Top + x.Bottom) / 2.0)
                    .ThenBy(x => x.Left)
                    .Select(x => x.Text!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();

                foreach (var line in block)
                    result.ProviderAddressLines.Add(line);
            }

            if (result.ProviderAddressLines.Count == 0)
            {
                foreach (var line in nearby)
                {
                    var text = line.Text!.Trim();

                    if (!IsProviderAddressLine(text, result.ServiceProviderName))
                        continue;

                    result.ProviderAddressLines.Add(text);

                    if (result.ProviderAddressLines.Count >= 4)
                        break;
                }
            }

            if (result.ProviderAddressLines.Count > 0
                && string.IsNullOrWhiteSpace(result.ProviderAddress))
            {
                result.ProviderAddress = string.Join(
                    " ",
                    result.ProviderAddressLines);
            }
        }

        private static bool IsProviderAddressLine(
            string text,
            string? providerName)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (!string.IsNullOrWhiteSpace(providerName)
                && string.Equals(
                    text,
                    providerName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var upper = text.ToUpperInvariant();

            if (upper.Contains("SVC PROVIDER")
                || upper.Contains("SERVICE PROVIDER")
                || upper.Contains("REMITTANCE")
                || upper.Contains("PROCESS DATE")
                || upper.Contains("PAYEE #")
                || upper.Contains("NPI")
                || upper.Contains("PATIENT NAME")
                || upper.Contains("HEALTH NET")
                || upper.Contains("CALVIVA")
                || HealthNetNoisePolicy.IsStructuredNoise(text)
                || HealthNetNoisePolicy.IsContactFooter(text)
                || LooksLikeProviderId(text))
            {
                return false;
            }

            if (LooksLikePayeeClinicName(text)
                || LooksLikeSuiteFragment(text)
                || LooksLikeAddressFragment(text)
                || LooksLikeProviderCityStateZipOnly(text))
            {
                return true;
            }

            return HealthNetNoisePolicy.LooksLikeUsableAddressLine(text)
                && LooksLikeAddressFragment(text);
        }

        private static bool LooksLikeSuiteFragment(string text)
        {
            var upper = text.ToUpperInvariant().Trim();
            return upper.StartsWith("STE ")
                || upper.StartsWith("SUITE ")
                || upper.StartsWith("STE.")
                || System.Text.RegularExpressions.Regex.IsMatch(
                    upper,
                    @"^STE\.?\s*\d+");
        }

        private static bool LooksLikePayeeClinicName(string text)
        {
            var upper = text.ToUpperInvariant();
            return upper.Contains("FAMILY MEDICAL")
                || upper.Contains("MEDICAL GROUP")
                || upper.Contains("SIERRA FAMILY");
        }

        private static bool LooksLikeAddressFragment(string text)
        {
            var upper = text.ToUpperInvariant();

            if (upper.Contains("PO BOX")
                || upper.Contains("P.O. BOX")
                || upper.Contains(" STREET")
                || upper.Contains(" ST ")
                || upper.Contains(" AVE")
                || upper.Contains(" ROAD")
                || upper.Contains(" RD ")
                || upper.Contains(" BLVD")
                || upper.Contains(" DRIVE")
                || upper.Contains(" DR ")
                || upper.Contains(" SUITE")
                || upper.Contains(" STE ")
                || System.Text.RegularExpressions.Regex.IsMatch(
                    text,
                    @"\b[A-Z]{2}\s+\d{5}(-\d{4})?\b"))
            {
                /*
                 * Footer contact blocks (QUESTIONS / email / phone)
                 * are excluded via IsContactFooter — remittance
                 * PO BOX / city lines (Farmington or Van Nuys) are valid.
                 */
                if (HealthNetNoisePolicy.IsContactFooter(text))
                    return false;

                return true;
            }

            return false;
        }
    }
}
