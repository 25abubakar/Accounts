using Accounts.Services.Ocr.HealthNet.Models;

namespace Accounts.Services.Ocr.HealthNet
{
    public class HealthNetSummaryParser
    {
        public HealthNetSummaryModel Parse(
            IEnumerable<HealthNetPageData> pages)
        {
            var result =
                new HealthNetSummaryModel();

            foreach (var page in pages)
            {
                if (page.Lines == null ||
                    page.Lines.Count == 0)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                    result.MemberPlanCode))
                {
                    result.MemberPlanCode =
                        HealthNetParsingHelper
                            .GetValueAfterLabel(
                                page.Lines,
                                "MEMBER PLAN CODE",
                                "TOTAL CLAIMS",
                                "TOTAL CHECK");
                }

                if (!result.TotalClaims.HasValue)
                {
                    var totalClaimsText =
                        HealthNetParsingHelper
                            .GetValueAfterAnyLabel(
                                page.Lines,
                                new[]
                                {
                                    "TOTAL CLAIMS",
                                    "NUMBER OF CLAIMS"
                                },
                                "TOTAL CLAIMS PAYABLE",
                                "TOTAL CHECK AMOUNT");

                    if (int.TryParse(
                        totalClaimsText,
                        out var totalClaims))
                    {
                        result.TotalClaims =
                            totalClaims;
                    }
                }

                if (!result
                    .TotalClaimsPayable
                    .HasValue)
                {
                    var payable =
                        HealthNetParsingHelper
                            .GetValueAfterLabel(
                                page.Lines,
                                "TOTAL CLAIMS PAYABLE",
                                "TOTAL CHECK AMOUNT");

                    result.TotalClaimsPayable =
                        HealthNetParsingHelper
                            .ParseDecimal(
                                payable);
                }

                if (!result
                    .TotalCheckAmount
                    .HasValue)
                {
                    var check =
                        HealthNetParsingHelper
                            .GetValueAfterLabel(
                                page.Lines,
                                "TOTAL CHECK AMOUNT");

                    result.TotalCheckAmount =
                        HealthNetParsingHelper
                            .ParseDecimal(
                                check);
                }
            }

            return result;
        }
    }
}