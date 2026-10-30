namespace Accounts.Services.Ocr.HealthNet
{
    public interface IHealthNetTextNormalizer
    {
        string NormalizePageText(string? text);

        string NormalizeSearchText(string? text);

        string NormalizeIdentifier(string? value);

        string NormalizePatientName(string? value);
    }
}