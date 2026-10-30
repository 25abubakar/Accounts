using Accounts.Services.Ocr.HealthNet.Models;

namespace Accounts.Services.Ocr.HealthNet
{
    public interface IHealthNetPageClassifier
    {
        HealthNetPageType Classify(
            HealthNetPageData page);
    }
}