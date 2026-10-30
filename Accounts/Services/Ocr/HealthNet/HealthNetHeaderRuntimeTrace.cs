using Accounts.Services.Ocr.HealthNet.Models;
using System.Globalization;
using System.Text;

namespace Accounts.Services.Ocr.HealthNet
{
    /// <summary>
    /// Development-only trace for Health Net header parsing failures.
    /// </summary>
    internal static class HealthNetHeaderRuntimeTrace
    {
        public static string BuildReport(
            IReadOnlyList<HealthNetPageData> pages,
            HealthNetDocumentHeaderModel header)
        {
            var sb = new StringBuilder();
            var ordered = pages.OrderBy(p => p.PageNumber).ToList();

            sb.AppendLine("===== HEALTH NET HEADER RUNTIME TRACE =====");
            sb.AppendLine($"Physical pages loaded: {ordered.Count}");

            for (int i = 0; i < ordered.Count; i++)
            {
                var page = ordered[i];
                var probe = page.CleanText ?? page.RawText ?? "";
                var skipInstruction =
                    HealthNetNoisePolicy.IsProviderInstructionSection(probe);
                var skipLegend =
                    HealthNetNoisePolicy.IsExplanationLegendSection(probe);

                sb.AppendLine(
                    $"Page {page.PageNumber}: Type={page.PageType} " +
                    $"Words={page.Words?.Count ?? 0} Lines={page.Lines?.Count ?? 0} " +
                    $"SkipInstruction={skipInstruction} SkipLegend={skipLegend} " +
                    $"IsScanned={page.IsScannedPage}");

                if (page.PageNumber == 1)
                {
                    DumpPageOneWords(sb, page);
                    DumpLabelProbe(sb, page);
                }
            }

            sb.AppendLine("----- FINAL HEADER (after HealthNetHeaderParser.Parse) -----");
            AppendFieldRow(sb, "ProcessDate", header.ProcessDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AppendFieldRow(sb, "RemittanceNo", header.RemittanceNo);
            AppendFieldRow(sb, "PayeeNo", header.PayeeNo);
            AppendFieldRow(sb, "ServiceProviderNo", header.ServiceProviderNo);
            AppendFieldRow(sb, "ServiceProviderName", header.ServiceProviderName);
            AppendFieldRow(sb, "InsuranceAddress", header.InsuranceAddress);
            AppendFieldRow(sb, "ProviderAddress", header.ProviderAddress);
            AppendFieldRow(sb, "InsuranceName", header.InsuranceName);
            AppendFieldRow(sb, "MemberPlanCode", header.MemberPlanCode);
            AppendFieldRow(sb, "CheckNumber(alias)", header.CheckNumber);
            AppendFieldRow(sb, "EobDate(alias)", header.EobDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            sb.AppendLine("===== END HEADER RUNTIME TRACE =====");
            return sb.ToString();
        }

        private static void DumpPageOneWords(
            StringBuilder sb,
            HealthNetPageData page)
        {
            sb.AppendLine("----- PAGE 1 WORDS (PdfPig order: Y desc, X asc) -----");

            var words = (page.Words ?? new List<HealthNetWordModel>())
                .OrderByDescending(w => (w.Top + w.Bottom) / 2.0)
                .ThenBy(w => w.Left)
                .ToList();

            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                var width = w.Right - w.Left;
                var height = w.Top - w.Bottom;
                sb.AppendLine(
                    $"[{i,4}] \"{w.Text}\" " +
                    $"L={w.Left:F1} R={w.Right:F1} T={w.Top:F1} B={w.Bottom:F1} " +
                    $"W={width:F1} H={height:F1}");
            }
        }

        private static void DumpLabelProbe(
            StringBuilder sb,
            HealthNetPageData page)
        {
            sb.AppendLine("----- PAGE 1 LABEL PROBE -----");

            var words = page.Words ?? new List<HealthNetWordModel>();
            ProbeLabel(sb, words, "Process Date", "PROCESS", "DATE");
            ProbeLabel(sb, words, "Remittance #", "REMITTANCE", "#");
            ProbeLabel(sb, words, "Payee #", "PAYEE", "#");
            ProbeLabel(sb, words, "SVC Provider #", "SVC", "PROVIDER", "#");
            ProbeLabel(sb, words, "SVC Provider", "SVC", "PROVIDER");
        }

        private static void ProbeLabel(
            StringBuilder sb,
            List<HealthNetWordModel> words,
            string displayName,
            params string[] tokens)
        {
            var rows = ClusterRows(words);
            bool found = false;

            foreach (var row in rows)
            {
                if (!RowHasTokens(row, tokens))
                    continue;

                found = true;
                var rowText = string.Join(
                    " ",
                    row.OrderBy(w => w.Left).Select(w => w.Text));
                var y = row.Average(w => (w.Top + w.Bottom) / 2.0);
                var labelRight = row.Max(w => w.Right);

                var rightText = string.Join(
                    " ",
                    words
                        .Where(w =>
                            Math.Abs(((w.Top + w.Bottom) / 2.0) - y) <= 4.0
                            && w.Left > labelRight + 1.5)
                        .OrderBy(w => w.Left)
                        .Select(w => w.Text));

                sb.AppendLine(
                    $"{displayName}: MATCH rowY={y:F1} row=\"{rowText}\" " +
                    $"rightOfLabel=\"{rightText}\"");
            }

            if (!found)
            {
                sb.AppendLine($"{displayName}: MATCH=false (no row with tokens: {string.Join(", ", tokens)})");
            }
        }

        private static List<List<HealthNetWordModel>> ClusterRows(
            List<HealthNetWordModel> words)
        {
            const double tol = 4.0;
            var sorted = words
                .OrderByDescending(w => (w.Top + w.Bottom) / 2.0)
                .ThenBy(w => w.Left)
                .ToList();

            var rows = new List<List<HealthNetWordModel>>();
            List<HealthNetWordModel>? cur = null;
            double? curY = null;

            foreach (var w in sorted)
            {
                var y = (w.Top + w.Bottom) / 2.0;
                if (cur == null || !curY.HasValue || Math.Abs(y - curY.Value) > tol)
                {
                    cur = new List<HealthNetWordModel> { w };
                    rows.Add(cur);
                    curY = y;
                }
                else
                {
                    cur.Add(w);
                }
            }

            return rows;
        }

        private static bool RowHasTokens(
            List<HealthNetWordModel> row,
            string[] tokens)
        {
            var norms = row
                .Select(w => w.Text.Trim().ToUpperInvariant())
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

                if (norms.Any(n => n == token || n.StartsWith(token, StringComparison.Ordinal)))
                    found++;
            }

            return found >= tokens.Length;
        }

        private static void AppendFieldRow(
            StringBuilder sb,
            string field,
            string? value)
        {
            sb.AppendLine(
                $"{field,-22} | {(string.IsNullOrWhiteSpace(value) ? "(null)" : value)}");
        }
    }
}
