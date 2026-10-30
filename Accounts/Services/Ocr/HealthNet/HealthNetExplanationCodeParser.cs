using Accounts.Services.Ocr.HealthNet.Models;

namespace Accounts.Services.Ocr.HealthNet
{
    /// <summary>
    /// Document-level Payment/Denial/Adjustment Explanation Code
    /// Description legend parser.
    ///
    /// Intentionally returns an empty list for structured OCR results.
    /// Service-line ExCodes come from the Ex Code column only.
    /// </summary>
    public class HealthNetExplanationCodeParser
    {
        public List<HealthNetExplanationCodeModel>
            Parse(
                IEnumerable<HealthNetPageData> pages)
        {
            /*
             * Legend / glossary pages must not populate
             * result.ExplanationCodes or the preview UI.
             */
            _ = pages;
            return new List<HealthNetExplanationCodeModel>();
        }
    }
}
