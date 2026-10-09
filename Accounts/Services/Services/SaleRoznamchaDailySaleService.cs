using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class SaleRoznamchaDailySaleService(
    ApplicationDbContext db,
    ICurrentUserService current) : ISaleRoznamchaDailySaleService
{
    public async Task<IReadOnlyList<SaleRoznamchaDailySaleRow>> ListAsync(
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<SaleRoznamchaDailySaleRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_DailySaleList @TenantId, @CategoryId, @CompanyId, @PlatformId, @ProductCategoryId",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@CategoryId", categoryId),
            SpListQuery.IntNullable("@CompanyId", companyId),
            SpListQuery.IntNullable("@PlatformId", platformId),
            SpListQuery.IntNullable("@ProductCategoryId", productCategoryId));

    public async Task<IReadOnlyList<SaleRoznamchaSaleStatusRow>> StatusesAsync(CancellationToken ct = default) =>
        await db.SaleRoznamchaSaleStatuses.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new SaleRoznamchaSaleStatusRow
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                InventoryEffect = x.InventoryEffect,
            })
            .ToListAsync(ct);

    public async Task<SaleRoznamchaDailySaleRow> CreateAsync(
        CreateSaleRoznamchaDailySaleRequest request,
        CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");

        var hierarchyValid = await (
            from product in db.SaleRoznamchaProducts.AsNoTracking()
            join productCategory in db.SaleRoznamchaProductCategories.AsNoTracking()
                on product.ProductCategoryId equals productCategory.Id
            join platform in db.SaleRoznamchaPlatforms.AsNoTracking()
                on productCategory.PlatformId equals platform.Id
            join company in db.SaleRoznamchaCompanies.AsNoTracking()
                on platform.CompanyId equals company.Id
            join category in db.SaleRoznamchaCategories.AsNoTracking()
                on company.CategoryId equals category.Id
            where product.Id == request.ProductId && !product.IsDeleted && product.IsActive &&
                  productCategory.Id == request.ProductCategoryId && platform.Id == request.PlatformId &&
                  company.Id == request.CompanyId && category.Id == request.CategoryId
            select product.Id).AnyAsync(ct);
        if (!hierarchyValid)
            throw new InvalidOperationException("Selected product does not belong to the selected hierarchy.");

        try
        {
            var created = await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                db,
                "EXEC dbo.usp_SaleRoznamcha_DailySaleCreate @TenantId, @ProductId, @SaleStatusId, @Quantity, @UserId",
                ct,
                SpListQuery.TenantId(current.TenantId),
                SpListQuery.BigInt("@ProductId", request.ProductId),
                SpListQuery.Int("@SaleStatusId", request.SaleStatusId),
                SpListQuery.Decimal("@Quantity", request.Quantity),
                SpListQuery.NVarChar("@UserId", current.UserId));
            var id = created.SingleOrDefault()?.Id
                ?? throw new InvalidOperationException("Daily sale could not be created.");
            return (await ListAsync(null, null, null, null, ct)).First(x => x.Id == id);
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51001)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }
}
