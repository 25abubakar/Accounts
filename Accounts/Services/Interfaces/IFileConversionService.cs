using Microsoft.AspNetCore.Http;

namespace Accounts.Services.Interfaces;

public interface IFileConversionService
{
    FileConversionCapabilities GetCapabilities();

    Task<ConvertedFile> ConvertAsync(
        IFormFile sourceFile,
        string targetFormat,
        CancellationToken cancellationToken = default);
}

public sealed record FileConversionCapabilities(
    bool HighFidelityEngineAvailable,
    string Engine,
    long MaximumFileSizeBytes,
    IReadOnlyList<FileConversionPair> SupportedPairs,
    string? UnavailableReason);

public sealed record FileConversionPair(string Source, string Target, string Quality);

public sealed record ConvertedFile(
    byte[] Content,
    string FileName,
    string ContentType,
    string Engine,
    string Quality);

public sealed class FileConversionException(string message) : Exception(message);
