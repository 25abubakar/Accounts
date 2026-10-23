using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class LibraryService(
    ApplicationDbContext db,
    ITenantService tenant,
    IWebHostEnvironment environment) : ILibraryService
{
    public async Task<string?> ValidateHierarchyAsync(
        int typeId, int? subTypeId, int? categoryId, CancellationToken ct = default)
    {
        var type = await db.LibraryTypes.AsNoTracking()
            .Where(x => x.Id == typeId && x.ParentId == null && x.IsActive)
            .Select(x => new { x.LibraryCategoryId })
            .FirstOrDefaultAsync(ct);
        if (type == null) return "Select an active library type.";
        if (categoryId.HasValue && type.LibraryCategoryId != categoryId)
            return "The selected library type does not belong to the selected category.";
        if (subTypeId.HasValue && !await db.LibraryTypes.AsNoTracking()
                .AnyAsync(x => x.Id == subTypeId && x.ParentId == typeId && x.IsActive, ct))
            return "The selected library sub type does not belong to the selected type.";
        return null;
    }

    public async Task<IReadOnlyList<LibraryPickerItemDto>> SearchDocumentsAsync(
        string? search, int? typeId, int? subTypeId, int take, CancellationToken ct = default)
    {
        var query = db.LibraryDocuments.AsNoTracking()
            .Where(x => x.IsActive && x.AssetKind == "Document");
        if (typeId.HasValue) query = query.Where(x => x.LibraryTypeId == typeId.Value);
        if (subTypeId.HasValue) query = query.Where(x => x.LibrarySubTypeId == subTypeId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Title.Contains(term) ||
                (x.DocumentReference != null && x.DocumentReference.Contains(term)) ||
                x.OriginalFileName.Contains(term));
        }
        return await query.OrderByDescending(x => x.CreatedOnUtc).Take(Math.Clamp(take, 1, 100))
            .Select(x => new LibraryPickerItemDto(
                x.Id, x.DocumentReference ?? ("Doc-" + x.Id), x.Title, x.LibraryTypeId,
                x.LibraryType == null ? string.Empty : x.LibraryType.Name,
                x.LibrarySubTypeId, x.LibrarySubType == null ? null : x.LibrarySubType.Name,
                x.OriginalFileName, x.ContentType, x.FileSizeBytes,
                "/api/library/documents/" + x.Id + "/download"))
            .ToListAsync(ct);
    }

    public async Task<LibraryHardCopyLocationDto?> GetHardCopyAsync(long documentId, CancellationToken ct = default)
    {
        var row = await db.LibraryHardCopyLocations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LibraryDocumentId == documentId, ct);
        return row == null ? null : Map(row);
    }

    public async Task<LibraryHardCopyLocationDto> SaveHardCopyAsync(
        long documentId, SaveLibraryHardCopyLocationDto request, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Store))
            throw new InvalidOperationException("Store is required.");
        if (request.ReturnedOn.HasValue && request.IssuedOn.HasValue && request.ReturnedOn < request.IssuedOn)
            throw new InvalidOperationException("Returned date cannot be earlier than issued date.");
        var document = await db.LibraryDocuments.AsNoTracking()
            .Where(x => x.Id == documentId && x.AssetKind == "Document")
            .Select(x => new { HardCopyRequired = x.LibraryType != null && x.LibraryType.IsHardCopyRequired })
            .FirstOrDefaultAsync(ct);
        if (document == null)
            throw new KeyNotFoundException("Library document not found.");
        if (!document.HardCopyRequired)
            throw new InvalidOperationException("The selected library type does not require hard-copy tracking.");

        var row = await db.LibraryHardCopyLocations.FirstOrDefaultAsync(x => x.LibraryDocumentId == documentId, ct);
        if (row == null)
        {
            row = new LibraryHardCopyLocation
            {
                TenantId = tenant.RequiredTenantId,
                LibraryDocumentId = documentId,
                CreatedByUserId = userId,
                CreatedOnUtc = DateTime.UtcNow
            };
            db.LibraryHardCopyLocations.Add(row);
        }
        else
        {
            row.UpdatedByUserId = userId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        row.Store = request.Store.Trim();
        row.Rack = Clean(request.Rack, 120);
        row.FolderReference = Clean(request.FolderReference, 120);
        row.PageNumber = Clean(request.PageNumber, 50);
        row.IssuedTo = Clean(request.IssuedTo, 200);
        row.IssuedOn = request.IssuedOn;
        row.ReturnedOn = request.ReturnedOn;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task DeleteHardCopyAsync(long documentId, CancellationToken ct = default)
    {
        var row = await db.LibraryHardCopyLocations.FirstOrDefaultAsync(x => x.LibraryDocumentId == documentId, ct)
            ?? throw new KeyNotFoundException("Hard-copy location not found.");
        db.LibraryHardCopyLocations.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(string Path, string ContentType, string FileName)?> GetDocumentFileAsync(
        long documentId, bool activeOnly, CancellationToken ct = default)
    {
        var row = await db.LibraryDocuments.AsNoTracking()
            .Where(x => x.Id == documentId && x.AssetKind == "Document" && (!activeOnly || x.IsActive))
            .Select(x => new { x.StoredFileName, x.ContentType, x.OriginalFileName })
            .FirstOrDefaultAsync(ct);
        if (row == null) return null;
        var safeName = Path.GetFileName(row.StoredFileName);
        if (!string.Equals(safeName, row.StoredFileName, StringComparison.Ordinal)) return null;
        return (Path.Combine(environment.ContentRootPath, "App_Data", "library",
            tenant.RequiredTenantId.ToString(), safeName), row.ContentType, row.OriginalFileName);
    }

    public Task<bool> HasRoznamchaLinksAsync(long documentId, CancellationToken ct = default) =>
        db.RoznamchaLibraryDocuments.AsNoTracking().AnyAsync(x => x.LibraryDocumentId == documentId, ct);

    private static LibraryHardCopyLocationDto Map(LibraryHardCopyLocation x) =>
        new(x.Id, x.LibraryDocumentId, x.Store, x.Rack, x.FolderReference, x.PageNumber,
            x.IssuedTo, x.IssuedOn, x.ReturnedOn, x.CreatedOnUtc, x.UpdatedOnUtc);

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
