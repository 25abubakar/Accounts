using Accounts.Models;

namespace Accounts.Services.Interfaces;

public interface ILibraryService
{
    Task<string?> ValidateHierarchyAsync(int typeId, int? subTypeId, int? categoryId, CancellationToken ct = default);
    Task<IReadOnlyList<LibraryPickerItemDto>> SearchDocumentsAsync(string? search, int? typeId, int? subTypeId, int take, CancellationToken ct = default);
    Task<LibraryHardCopyLocationDto?> GetHardCopyAsync(long documentId, CancellationToken ct = default);
    Task<LibraryHardCopyLocationDto> SaveHardCopyAsync(long documentId, SaveLibraryHardCopyLocationDto request, string? userId, CancellationToken ct = default);
    Task DeleteHardCopyAsync(long documentId, CancellationToken ct = default);
    Task<(string Path, string ContentType, string FileName)?> GetDocumentFileAsync(long documentId, bool activeOnly, CancellationToken ct = default);
    Task<bool> HasRoznamchaLinksAsync(long documentId, CancellationToken ct = default);
}

public sealed record LibraryPickerItemDto(
    long Id, string DocumentReference, string Title, int LibraryTypeId, string LibraryTypeName,
    int? LibrarySubTypeId, string? LibrarySubTypeName, string OriginalFileName, string ContentType,
    long FileSizeBytes, string DownloadUrl);

public sealed record SaveLibraryHardCopyLocationDto(
    string Store, string? Rack, string? FolderReference, string? PageNumber,
    string? IssuedTo, DateTime? IssuedOn, DateTime? ReturnedOn);

public sealed record LibraryHardCopyLocationDto(
    long Id, long LibraryDocumentId, string Store, string? Rack, string? FolderReference,
    string? PageNumber, string? IssuedTo, DateTime? IssuedOn, DateTime? ReturnedOn,
    DateTime CreatedOnUtc, DateTime? UpdatedOnUtc);
