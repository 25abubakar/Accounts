using Accounts.Services.Ocr.HealthNet.Models;

namespace Accounts.Services.Ocr.HealthNet
{
    public interface IHealthNetDocumentParser
    {
        Task<HealthNetOCRResultViewModel> ParseAsync(
            Stream pdfStream,
            string? fileName = null,
            CancellationToken cancellationToken = default);
    }
}