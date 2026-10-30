using Accounts.Services.Ocr.HealthNet.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Accounts.Services.Ocr.HealthNet
{
    public interface IHealthNetPdfReader
    {
        Task<List<HealthNetPageData>> ReadPagesAsync(
            Stream pdfStream,
            CancellationToken cancellationToken = default);
    }
}