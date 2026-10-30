using Accounts.Services.Ocr.HealthNet.Models;
using Accounts.Services.Ocr.HealthNet;
using System;

namespace Accounts.Services.Ocr.HealthNet
{
    public class HealthNetPageClassifier
        : IHealthNetPageClassifier
    {
        private readonly IHealthNetTextNormalizer
            _normalizer;

        public HealthNetPageClassifier(
            IHealthNetTextNormalizer normalizer)
        {
            _normalizer = normalizer;
        }

        public HealthNetPageType Classify(
            HealthNetPageData page)
        {
            if (page == null)
                return HealthNetPageType.Unknown;

            var sourceText =
                !string.IsNullOrWhiteSpace(
                    page.CleanText)
                    ? page.CleanText
                    : page.RawText;

            var text =
                _normalizer.NormalizeSearchText(
                    sourceText);

            if (string.IsNullOrWhiteSpace(text))
            {
                return HealthNetPageType.Unknown;
            }

            /*
             * ORDER IS IMPORTANT.
             *
             * Hybrid remittance pages can contain BOTH
             * final claim service rows AND document summary.
             * Service-table evidence must win so the page
             * loop still parses claim lines/totals.
             * HealthNetSummaryParser scans all pages later.
             */

            if (HasServiceTableEvidence(text) || IsClaimPage(text))
            {
                return HealthNetPageType
                    .ClaimEop;
            }

            if (IsSummaryPage(text))
            {
                return HealthNetPageType
                    .RemittanceSummary;
            }

            if (IsTermsPage(text))
            {
                return HealthNetPageType
                    .TermsInformation;
            }

            if (IsHeaderPage(text))
            {
                return HealthNetPageType
                    .RemittanceHeader;
            }

            return HealthNetPageType.Unknown;
        }

        /// <summary>
        /// Strong service-table anchors. A hybrid page that also
        /// has Member Plan Code / Totals must still parse as claim.
        /// </summary>
        private static bool HasServiceTableEvidence(string text)
        {
            int score = 0;

            if (Contains(text, "SVC DATES")
                || Contains(text, "SERVICE DATES"))
            {
                score++;
            }

            if (Contains(text, "CPT/REV")
                || Contains(text, "CPT REV"))
            {
                score++;
            }

            if (Contains(text, "BENEFIT PAYABLE")
                || Contains(text, "BENEFIT"))
            {
                score++;
            }

            if (Contains(text, "BILLED AMOUNT")
                || Contains(text, "BILLED"))
            {
                score++;
            }

            return score >= 2;
        }

        private bool IsClaimPage(string text)
        {
            int score = 0;

            if (Contains(
                text,
                "PATIENT NAME"))
            {
                score++;
            }

            if (Contains(
                text,
                "YOUR ACCT"))
            {
                score++;
            }

            if (Contains(
                    text,
                    "SUBID/CIN") ||
                Contains(
                    text,
                    "SUBID CIN"))
            {
                score++;
            }

            if (Contains(
                    text,
                    "CLAIM #") ||
                Contains(
                    text,
                    "CLAIM NO"))
            {
                score++;
            }

            if (Contains(
                    text,
                    "SVC DATES") ||
                Contains(
                    text,
                    "SERVICE DATES"))
            {
                score++;
            }

            if (Contains(
                    text,
                    "CPT/REV") ||
                Contains(
                    text,
                    "CPT REV"))
            {
                score++;
            }

            if (Contains(
                text,
                "BENEFIT PAYABLE"))
            {
                score++;
            }

            return score >= 3;
        }

        private bool IsSummaryPage(
            string text)
        {
            int score = 0;

            if (Contains(
                text,
                "MEMBER PLAN CODE"))
            {
                score++;
            }

            if (Contains(
                text,
                "TOTAL CLAIMS PAYABLE"))
            {
                score++;
            }

            if (Contains(
                text,
                "TOTAL CHECK AMOUNT"))
            {
                score++;
            }

            if (Contains(text, "PAYMENT") &&
                Contains(text, "DENIAL") &&
                Contains(text, "ADJUSTMENT"))
            {
                score++;
            }

            return score >= 2;
        }

        private bool IsTermsPage(
            string text)
        {
            /*
             * Never classify a real claim page
             * as Terms.
             */
            if (IsClaimPage(text) || HasServiceTableEvidence(text))
                return false;

            if (HealthNetNoisePolicy.IsProviderInstructionSection(text)
                || HealthNetNoisePolicy.IsExplanationLegendSection(text)
                || HealthNetNoisePolicy.IsLegalBoilerplate(text))
            {
                return true;
            }

            int score = 0;

            if (Contains(
                text,
                "TERMS"))
            {
                score++;
            }

            if (Contains(
                text,
                "INFORMATION"))
            {
                score++;
            }

            if (Contains(
                text,
                "EXPLANATION CODES"))
            {
                score++;
            }

            if (Contains(
                text,
                "IMPORTANT INFORMATION"))
            {
                score++;
            }

            if (Contains(
                text,
                "CALVIVA HEALTH MEDI-CAL"))
            {
                score++;
            }

            return score >= 2;
        }

        private bool IsHeaderPage(
            string text)
        {
            int score = 0;

            if (Contains(
                text,
                "EXPLANATION OF PAYMENT"))
            {
                score++;
            }

            if (Contains(
                text,
                "PROCESS DATE"))
            {
                score++;
            }

            if (Contains(
                    text,
                    "REMITTANCE #") ||
                Contains(
                    text,
                    "REMITTANCE NO"))
            {
                score++;
            }

            if (Contains(
                text,
                "PAYEE"))
            {
                score++;
            }

            if (Contains(
                    text,
                    "SVC PROVIDER") ||
                Contains(
                    text,
                    "SERVICE PROVIDER"))
            {
                score++;
            }

            return score >= 2;
        }

        private static bool Contains(
            string source,
            string value)
        {
            return source.Contains(
                value,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}