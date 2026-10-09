using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Accounts.Services.Services;

public sealed class SaleRoznamchaMasterService(
    ApplicationDbContext db,
    ICurrentUserService current) : ISaleRoznamchaMasterService
{
    private const string Category = "category";
    private const string Company = "company";
    private const string Platform = "platform";
    private const string ProductCategory = "product-category";

    public async Task<IReadOnlyList<SaleRoznamchaMasterRow>> ListAsync(
        string? entityType,
        int? parentId,
        bool activeOnly,
        CancellationToken ct = default)
    {
        var normalizedType = string.IsNullOrWhiteSpace(entityType) ? null : Normalize(entityType);
        return await SpListQuery.ExecAsync<SaleRoznamchaMasterRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_MasterHierarchy @TenantId, @EntityType, @ParentId, @ActiveOnly",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.NVarChar("@EntityType", normalizedType),
            SpListQuery.IntNullable("@ParentId", parentId),
            SpListQuery.Bit("@ActiveOnly", activeOnly));
    }

    public async Task<SaleRoznamchaMasterRow> SaveAsync(
        string entityType,
        int? id,
        SaveSaleRoznamchaMasterRequest request,
        CancellationToken ct = default)
    {
        var type = Normalize(entityType);
        var name = Required(request.Name);

        var savedId = type switch
        {
            Category => await SaveCategoryAsync(id, name, request.IsActive, ct),
            Company => await SaveCompanyAsync(id, RequiredParent(request.ParentId, "Category"), name, request.IsActive, ct),
            Platform => await SavePlatformAsync(id, RequiredParent(request.ParentId, "Company"), name, request.Code, request.IsActive, ct),
            ProductCategory => await SaveProductCategoryAsync(id, RequiredParent(request.ParentId, "Platform"), name, request.Code, request.IsActive, ct),
            _ => throw new InvalidOperationException("Unknown Sale Roznamcha master type."),
        };

        var saved = (await ListAsync(type, null, false, ct)).FirstOrDefault(x => x.Id == savedId);
        return saved ?? throw new InvalidOperationException("Saved master record could not be reloaded.");
    }

    public async Task DeleteAsync(string entityType, int id, CancellationToken ct = default)
    {
        switch (Normalize(entityType))
        {
            case Category:
            {
                var row = await db.SaleRoznamchaCategories.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Category was not found.");
                if (await db.SaleRoznamchaCompanies.AnyAsync(x => x.CategoryId == id, ct))
                    throw new InvalidOperationException("Category cannot be deleted because companies use it.");
                db.SaleRoznamchaCategories.Remove(row);
                break;
            }
            case Company:
            {
                var row = await db.SaleRoznamchaCompanies.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Company was not found.");
                if (await db.SaleRoznamchaPlatforms.AnyAsync(x => x.CompanyId == id, ct))
                    throw new InvalidOperationException("Company cannot be deleted because platforms use it.");
                db.SaleRoznamchaCompanies.Remove(row);
                break;
            }
            case Platform:
            {
                var row = await db.SaleRoznamchaPlatforms.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Platform was not found.");
                if (await db.SaleRoznamchaProductCategories.AnyAsync(x => x.PlatformId == id, ct))
                    throw new InvalidOperationException("Platform cannot be deleted because product categories use it.");
                db.SaleRoznamchaPlatforms.Remove(row);
                break;
            }
            case ProductCategory:
            {
                var row = await db.SaleRoznamchaProductCategories.FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Product category was not found.");
                if (await db.SaleRoznamchaProducts.AnyAsync(x => x.ProductCategoryId == id && !x.IsDeleted, ct))
                    throw new InvalidOperationException("Product category cannot be deleted because inventory products use it.");
                db.SaleRoznamchaProductCategories.Remove(row);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<int> SaveCategoryAsync(int? id, string name, bool isActive, CancellationToken ct)
    {
        if (await db.SaleRoznamchaCategories.AsNoTracking()
                .AnyAsync(x => x.Name == name && (!id.HasValue || x.Id != id), ct))
            throw new InvalidOperationException("Category already exists.");

        SaleRoznamchaCategory row;
        if (id.HasValue)
        {
            row = await db.SaleRoznamchaCategories.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Category was not found.");
            StampUpdate(row);
        }
        else
        {
            row = new SaleRoznamchaCategory
            {
                TenantId = current.TenantId,
                CreatedByUserId = current.UserId,
            };
            db.SaleRoznamchaCategories.Add(row);
        }
        row.Name = name;
        row.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task<int> SaveCompanyAsync(int? id, int categoryId, string name, bool isActive, CancellationToken ct)
    {
        if (!await db.SaleRoznamchaCategories.AnyAsync(x => x.Id == categoryId && x.IsActive, ct))
            throw new InvalidOperationException("Selected category is invalid or inactive.");
        if (await db.SaleRoznamchaCompanies.AsNoTracking()
                .AnyAsync(x => x.CategoryId == categoryId && x.Name == name && (!id.HasValue || x.Id != id), ct))
            throw new InvalidOperationException("Company already exists in the selected category.");

        SaleRoznamchaCompany row;
        if (id.HasValue)
        {
            row = await db.SaleRoznamchaCompanies.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Company was not found.");
            StampUpdate(row);
        }
        else
        {
            row = new SaleRoznamchaCompany
            {
                TenantId = current.TenantId,
                CreatedByUserId = current.UserId,
            };
            db.SaleRoznamchaCompanies.Add(row);
        }
        row.CategoryId = categoryId;
        row.Name = name;
        row.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task<int> SavePlatformAsync(int? id, int companyId, string name, string? requestedCode, bool isActive, CancellationToken ct)
    {
        if (!await db.SaleRoznamchaCompanies.AnyAsync(x => x.Id == companyId && x.IsActive, ct))
            throw new InvalidOperationException("Selected company is invalid or inactive.");
        if (await db.SaleRoznamchaPlatforms.AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Name == name && (!id.HasValue || x.Id != id), ct))
            throw new InvalidOperationException("Platform already exists in the selected company.");

        SaleRoznamchaPlatform row;
        if (id.HasValue)
        {
            row = await db.SaleRoznamchaPlatforms.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Platform was not found.");
            StampUpdate(row);
        }
        else
        {
            row = new SaleRoznamchaPlatform
            {
                TenantId = current.TenantId,
                CreatedByUserId = current.UserId,
            };
            db.SaleRoznamchaPlatforms.Add(row);
        }
        var code = await ResolvePlatformCodeAsync(companyId, id, requestedCode, name, ct);
        row.CompanyId = companyId;
        row.Code = code;
        row.Name = name;
        row.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task<int> SaveProductCategoryAsync(int? id, int platformId, string name, string? requestedCode, bool isActive, CancellationToken ct)
    {
        if (!await db.SaleRoznamchaPlatforms.AnyAsync(x => x.Id == platformId && x.IsActive, ct))
            throw new InvalidOperationException("Selected platform is invalid or inactive.");
        if (await db.SaleRoznamchaProductCategories.AsNoTracking()
                .AnyAsync(x => x.PlatformId == platformId && x.Name == name && (!id.HasValue || x.Id != id), ct))
            throw new InvalidOperationException("Product category already exists in the selected platform.");

        SaleRoznamchaProductCategory row;
        if (id.HasValue)
        {
            row = await db.SaleRoznamchaProductCategories.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Product category was not found.");
            StampUpdate(row);
        }
        else
        {
            row = new SaleRoznamchaProductCategory
            {
                TenantId = current.TenantId,
                CreatedByUserId = current.UserId,
            };
            db.SaleRoznamchaProductCategories.Add(row);
        }
        var code = await ResolveProductCategoryCodeAsync(platformId, id, requestedCode, name, ct);
        row.PlatformId = platformId;
        row.Code = code;
        row.Name = name;
        row.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private void StampUpdate(SaleRoznamchaCategory row)
    {
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
    }

    private void StampUpdate(SaleRoznamchaCompany row)
    {
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
    }

    private void StampUpdate(SaleRoznamchaPlatform row)
    {
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
    }

    private void StampUpdate(SaleRoznamchaProductCategory row)
    {
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant() switch
    {
        Category => Category,
        Company => Company,
        Platform => Platform,
        ProductCategory or "productcategory" => ProductCategory,
        _ => throw new InvalidOperationException("Unknown Sale Roznamcha master type."),
    };

    private static string Required(string? value)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            throw new InvalidOperationException("Name is required.");
        if (cleaned.Length > 160)
            throw new InvalidOperationException("Name cannot exceed 160 characters.");
        return cleaned;
    }

    private static int RequiredParent(int? parentId, string label) =>
        parentId is > 0 ? parentId.Value : throw new InvalidOperationException($"{label} is required.");

    private async Task<string> ResolvePlatformCodeAsync(
        int companyId, int? id, string? requestedCode, string name, CancellationToken ct)
    {
        var code = NormalizeCode(requestedCode, name);
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            if (await db.SaleRoznamchaPlatforms.AsNoTracking().AnyAsync(x =>
                    x.CompanyId == companyId && x.Code == code && (!id.HasValue || x.Id != id), ct))
                throw new InvalidOperationException("Platform code already exists in the selected company.");
            return code;
        }

        return await MakeUniqueCodeAsync(code, candidate => db.SaleRoznamchaPlatforms.AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Code == candidate && (!id.HasValue || x.Id != id), ct));
    }

    private async Task<string> ResolveProductCategoryCodeAsync(
        int platformId, int? id, string? requestedCode, string name, CancellationToken ct)
    {
        var code = NormalizeCode(requestedCode, name);
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            if (await db.SaleRoznamchaProductCategories.AsNoTracking().AnyAsync(x =>
                    x.PlatformId == platformId && x.Code == code && (!id.HasValue || x.Id != id), ct))
                throw new InvalidOperationException("Product category code already exists in the selected platform.");
            return code;
        }

        return await MakeUniqueCodeAsync(code, candidate => db.SaleRoznamchaProductCategories.AsNoTracking()
            .AnyAsync(x => x.PlatformId == platformId && x.Code == candidate && (!id.HasValue || x.Id != id), ct));
    }

    private static async Task<string> MakeUniqueCodeAsync(string baseCode, Func<string, Task<bool>> exists)
    {
        if (!await exists(baseCode)) return baseCode;
        for (var suffix = 2; suffix < 10000; suffix++)
        {
            var suffixText = suffix.ToString();
            var candidate = $"{baseCode[..Math.Min(baseCode.Length, 20 - suffixText.Length)]}{suffixText}";
            if (!await exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("A unique short code could not be generated.");
    }

    private static string NormalizeCode(string? requestedCode, string name)
    {
        var source = string.IsNullOrWhiteSpace(requestedCode) ? name : requestedCode;
        var builder = new StringBuilder(20);
        foreach (var character in source!.Trim().ToUpperInvariant())
        {
            if (!char.IsLetterOrDigit(character)) continue;
            builder.Append(character);
            if (builder.Length == 20) break;
        }
        var code = builder.ToString();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Code must contain at least one letter or number.");
        return string.IsNullOrWhiteSpace(requestedCode) && code.Length > 3 ? code[..3] : code;
    }
}
