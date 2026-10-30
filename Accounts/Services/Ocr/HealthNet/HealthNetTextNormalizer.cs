using Accounts.Services.Ocr.HealthNet;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Accounts.Services.Ocr.HealthNet
{
    public class HealthNetTextNormalizer
        : IHealthNetTextNormalizer
    {
        public string NormalizePageText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace('\u00A0', ' ');

            var lines = value
                .Split('\n')
                .Select(x =>
                    Regex.Replace(
                        x,
                        @"[ \t]+",
                        " ").Trim())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x));

            return string.Join(
                Environment.NewLine,
                lines);
        }

        public string NormalizeSearchText(
            string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text
                .Replace('\u00A0', ' ')
                .ToUpperInvariant();

            value = Regex.Replace(
                value,
                @"\s+",
                " ");

            return value.Trim();
        }

        public string NormalizeIdentifier(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value
                .Trim()
                .ToUpperInvariant();

            /*
             * Remove formatting characters only.
             *
             * IMPORTANT:
             * Never globally convert:
             *
             * O -> 0
             * I -> 1
             * S -> 5
             *
             * because Health Net identifiers may
             * legitimately contain letters.
             */
            value = Regex.Replace(
                value,
                @"[^A-Z0-9]",
                string.Empty);

            return value;
        }

        public string NormalizePatientName(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value
                .ToUpperInvariant()
                .Replace(",", " ")
                .Replace(".", " ")
                .Replace("-", " ")
                .Replace("'", " ");

            value = Regex.Replace(
                value,
                @"[^A-Z0-9 ]",
                " ");

            value = Regex.Replace(
                value,
                @"\s+",
                " ");

            return value.Trim();
        }
    }
}