using System.Text.RegularExpressions;

namespace Accounts.Services.Ocr.HealthNet
{
    /// <summary>
    /// Central exclusion rules for Health Net structured extraction.
    /// Applied to business fields only â€” does not destroy RawText
    /// used for DEBUG diagnostics.
    /// </summary>
    public static class HealthNetNoisePolicy
    {
        /*
         * Contact-footer markers must NOT include remittance PO BOX /
         * city lines â€” CalViva + Health Net headers legitimately use
         * PO BOX 10196 / Van Nuys as InsuranceAddress.
         * Address rejection is contextual (near QUESTIONS / email / phone).
         */
        private static readonly string[] ContactFooterMarkers =
        {
            "QUESTIONS? CONTACT US",
            "CONTACT US AT",
            "PROVIDER_SERVICES@HEALTHNET.COM",
            "PROVIDER_SERVICES@HEALTHNET",
            "1-800-929-9224"
        };

        private static readonly string[] LegalBoilerplateMarkers =
        {
            "CONFIDENTIALITY NOTE",
            "CONFIDENTIALITY NOTE FOR FAX",
            "INTENDED ONLY FOR THE USE",
            "DISCLOSURE, COPYING, DISTRIBUTION",
            "RECEIVED THIS TRANSMISSION IN ERROR",
            "ALL RIGHTS RESERVED",
            "LICENSED HEALTH PLAN",
            "HEALTH NET COMMUNITY SOLUTIONS",
            "THE SENDER IMMEDIATELY BY TELEPHONE",
            "FACSIMILE MAY CONTAIN CONFIDENTIAL",
            "INFORMATION CONTAINED IN THIS INTENDED RECIPIENT",
            "RESPONSIBLE FOR FACSIMILE",
            "SERVICE MARKS REMAIN THE PROPERTY"
        };

        private static readonly string[] ProviderInstructionMarkers =
        {
            "CALVIVA HEALTH MEDI-CAL PROVIDERS",
            "PARTICIPATING AND NON-PARTICIPATING PHYSICIAN GROUP",
            "OTHER HEALTH CARE SERVICE PROVIDERS",
            "CLAIM STATUS OR OTHER INQUIRIES",
            "CONTESTED CLAIMS",
            "ABBREVIATIONS THAT MAY BE USED ON THIS DOCUMENT"
        };

        private static readonly string[] ExplanationLegendMarkers =
        {
            "PAYMENT/DENIAL/ADJUSTMENT EXPLANATION CODE DESCRIPTION",
            "PAYMENT DENIAL ADJUSTMENT EXPLANATION CODE DESCRIPTION",
            "EXPLANATION CODE DESCRIPTION"
        };

        private static readonly string[] TermsInformationMarkers =
        {
            "IMPORTANT INFORMATION",
            "TERMS AND CONDITIONS",
            "ACKNOWLEDGMENT"
        };

        private static readonly Regex EmailRegex =
            new Regex(
                @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsContactFooter(string? text)
        {
            return ContainsAny(text, ContactFooterMarkers)
                || ContainsEmailFooter(text);
        }

        public static bool IsLegalBoilerplate(string? text)
        {
            return ContainsAny(text, LegalBoilerplateMarkers);
        }

        public static bool IsProviderInstructionSection(string? text)
        {
            return ContainsAny(text, ProviderInstructionMarkers);
        }

        public static bool IsExplanationLegendSection(string? text)
        {
            return ContainsAny(text, ExplanationLegendMarkers);
        }

        public static bool IsTermsInformation(string? text)
        {
            if (ContainsAny(text, TermsInformationMarkers)
                && (ContainsAny(text, ProviderInstructionMarkers)
                    || ContainsAny(text, LegalBoilerplateMarkers)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// True when the text must not populate structured
        /// insurance/provider/patient/claim fields.
        /// </summary>
        public static bool IsStructuredNoise(string? text)
        {
            return IsContactFooter(text)
                || IsLegalBoilerplate(text)
                || IsProviderInstructionSection(text)
                || IsExplanationLegendSection(text)
                || IsTermsInformation(text);
        }

        public static bool LooksLikeUsableAddressLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (IsStructuredNoise(text))
                return false;

            var trimmed = text.Trim();

            if (trimmed.Length < 5)
                return false;

            if (EmailRegex.IsMatch(trimmed))
                return false;

            var upper = trimmed.ToUpperInvariant();

            if (upper.StartsWith("QUESTIONS")
                || upper.StartsWith("HTTP")
                || upper.Contains("WWW."))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsEmailFooter(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var upper = text.ToUpperInvariant();
            return upper.Contains("PROVIDER_SERVICES")
                || (upper.Contains("@HEALTHNET")
                    && upper.Contains("CONTACT"));
        }

        private static bool ContainsAny(
            string? text,
            string[] markers)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var upper = text.ToUpperInvariant();

            foreach (var marker in markers)
            {
                if (upper.Contains(marker))
                    return true;
            }

            return false;
        }
    }
}
