namespace Accounts.Data;

internal static class SaleRoznamchaSql
{
    internal const string MasterHierarchyProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_MasterHierarchy
            @TenantId int,
            @EntityType nvarchar(30) = NULL,
            @ParentId int = NULL,
            @ActiveOnly bit = 0
        AS
        BEGIN
            SET NOCOUNT ON;

            ;WITH MasterRows AS
            (
                SELECT category.Id, CAST(N'category' AS nvarchar(30)) EntityType, category.Name,
                       CAST(NULL AS nvarchar(20)) Code,
                       CAST(NULL AS int) ParentId, CAST(NULL AS nvarchar(160)) ParentName,
                       category.Id CategoryId, category.Name CategoryName,
                       CAST(NULL AS int) CompanyId, CAST(NULL AS nvarchar(160)) CompanyName,
                       CAST(NULL AS int) PlatformId, CAST(NULL AS nvarchar(160)) PlatformName,
                       category.IsActive, category.CreatedOnUtc, category.UpdatedOnUtc
                FROM dbo.SaleRoznamchaCategories category
                WHERE category.TenantId = @TenantId

                UNION ALL

                SELECT company.Id, CAST(N'company' AS nvarchar(30)), company.Name,
                       CAST(NULL AS nvarchar(20)),
                       company.CategoryId, category.Name,
                       category.Id, category.Name, company.Id, company.Name,
                       CAST(NULL AS int), CAST(NULL AS nvarchar(160)),
                       company.IsActive, company.CreatedOnUtc, company.UpdatedOnUtc
                FROM dbo.SaleRoznamchaCompanies company
                INNER JOIN dbo.SaleRoznamchaCategories category
                    ON category.TenantId = company.TenantId AND category.Id = company.CategoryId
                WHERE company.TenantId = @TenantId

                UNION ALL

                SELECT platform.Id, CAST(N'platform' AS nvarchar(30)), platform.Name, platform.Code,
                       platform.CompanyId, company.Name,
                       category.Id, category.Name, company.Id, company.Name,
                       platform.Id, platform.Name,
                       platform.IsActive, platform.CreatedOnUtc, platform.UpdatedOnUtc
                FROM dbo.SaleRoznamchaPlatforms platform
                INNER JOIN dbo.SaleRoznamchaCompanies company
                    ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
                INNER JOIN dbo.SaleRoznamchaCategories category
                    ON category.TenantId = company.TenantId AND category.Id = company.CategoryId
                WHERE platform.TenantId = @TenantId

                UNION ALL

                SELECT product.Id, CAST(N'product-category' AS nvarchar(30)), product.Name, product.Code,
                       product.PlatformId, platform.Name,
                       category.Id, category.Name, company.Id, company.Name,
                       platform.Id, platform.Name,
                       product.IsActive, product.CreatedOnUtc, product.UpdatedOnUtc
                FROM dbo.SaleRoznamchaProductCategories product
                INNER JOIN dbo.SaleRoznamchaPlatforms platform
                    ON platform.TenantId = product.TenantId AND platform.Id = product.PlatformId
                INNER JOIN dbo.SaleRoznamchaCompanies company
                    ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
                INNER JOIN dbo.SaleRoznamchaCategories category
                    ON category.TenantId = company.TenantId AND category.Id = company.CategoryId
                WHERE product.TenantId = @TenantId
            )
            SELECT Id, EntityType, Name, Code, ParentId, ParentName,
                   CategoryId, CategoryName, CompanyId, CompanyName, PlatformId, PlatformName,
                   IsActive, CreatedOnUtc, UpdatedOnUtc
            FROM MasterRows
            WHERE (@EntityType IS NULL OR EntityType = @EntityType)
              AND (@ParentId IS NULL OR ParentId = @ParentId)
              AND (@ActiveOnly = 0 OR IsActive = 1)
            ORDER BY CASE EntityType WHEN N'category' THEN 1 WHEN N'company' THEN 2
                                     WHEN N'platform' THEN 3 WHEN N'product-category' THEN 4 ELSE 5 END,
                     CategoryName, CompanyName, PlatformName, Name;
        END;
        """;

    internal const string InventoryListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryList
            @TenantId int,
            @CategoryId int = NULL,
            @CompanyId int = NULL,
            @PlatformId int = NULL,
            @ProductCategoryId int = NULL,
            @ActiveOnly bit = 0
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT product.Id,
                   product.ProductCode,
                   product.Name ProductName,
                   category.Id CategoryId,
                   category.Name CategoryName,
                   company.Id CompanyId,
                   company.Name CompanyName,
                   platform.Id PlatformId,
                   platform.Name PlatformName,
                   productCategory.Id ProductCategoryId,
                   productCategory.Name ProductCategoryName,
                   product.QuantityOnHand,
                   COALESCE(receiptTotals.TotalPurchasedQuantity, 0) TotalPurchasedQuantity,
                   COALESCE(saleTotals.SoldQuantity, 0) SoldQuantity,
                   product.PurchasePrice,
                   product.CurrencyId,
                   currency.Code CurrencyCode,
                   COALESCE(latestCurrencyValue.TotalStockValue, 0) TotalStockValue,
                   COALESCE(valueBreakdown.StockValueBreakdownJson, N'[]') StockValueBreakdownJson,
                   CASE WHEN product.QuantityOnHand > 0 THEN N'In Stock' ELSE N'Out of Stock' END StockStatus,
                   receiptTotals.LastPurchasedOnUtc,
                   product.IsActive,
                   product.CreatedOnUtc,
                   product.UpdatedOnUtc
            FROM dbo.SaleRoznamchaProducts product
            INNER JOIN dbo.SaleRoznamchaProductCategories productCategory
                ON productCategory.TenantId = product.TenantId
               AND productCategory.Id = product.ProductCategoryId
            INNER JOIN dbo.SaleRoznamchaPlatforms platform
                ON platform.TenantId = productCategory.TenantId
               AND platform.Id = productCategory.PlatformId
            INNER JOIN dbo.SaleRoznamchaCompanies company
                ON company.TenantId = platform.TenantId
               AND company.Id = platform.CompanyId
            INNER JOIN dbo.SaleRoznamchaCategories category
                ON category.TenantId = company.TenantId
               AND category.Id = company.CategoryId
            INNER JOIN dbo.AccountsCurrencies currency
                ON currency.Id = product.CurrencyId
               AND (currency.TenantId IS NULL OR currency.TenantId = product.TenantId)
            OUTER APPLY
            (
                SELECT SUM(receipt.PurchasedQuantity) TotalPurchasedQuantity,
                       MAX(receipt.PurchasedOnUtc) LastPurchasedOnUtc
                FROM dbo.SaleRoznamchaStockReceipts receipt
                WHERE receipt.TenantId = product.TenantId AND receipt.ProductId = product.Id
            ) receiptTotals
            OUTER APPLY
            (
                SELECT SUM(CASE WHEN sale.InventoryDelta < 0 THEN -sale.InventoryDelta ELSE 0 END) SoldQuantity
                FROM dbo.SaleRoznamchaDailySales sale
                WHERE sale.TenantId = product.TenantId AND sale.ProductId = product.Id
            ) saleTotals
            OUTER APPLY
            (
                SELECT CAST(COALESCE(SUM(receipt.RemainingQuantity * receipt.UnitCost), 0) AS decimal(38,2)) TotalStockValue
                FROM dbo.SaleRoznamchaStockReceipts receipt
                WHERE receipt.TenantId = product.TenantId AND receipt.ProductId = product.Id
                  AND receipt.CurrencyId = product.CurrencyId AND receipt.RemainingQuantity > 0
            ) latestCurrencyValue
            OUTER APPLY
            (
                SELECT
                (
                    SELECT receipt.CurrencyId currencyId, stockCurrency.Code currencyCode,
                           CAST(SUM(receipt.RemainingQuantity * receipt.UnitCost) AS decimal(38,2)) amount
                    FROM dbo.SaleRoznamchaStockReceipts receipt
                    INNER JOIN dbo.AccountsCurrencies stockCurrency ON stockCurrency.Id = receipt.CurrencyId
                    WHERE receipt.TenantId = product.TenantId AND receipt.ProductId = product.Id
                      AND receipt.RemainingQuantity > 0
                    GROUP BY receipt.CurrencyId, stockCurrency.Code
                    ORDER BY stockCurrency.Code
                    FOR JSON PATH
                ) StockValueBreakdownJson
            ) valueBreakdown
            WHERE product.TenantId = @TenantId
              AND product.IsDeleted = 0
              AND (@CategoryId IS NULL OR category.Id = @CategoryId)
              AND (@CompanyId IS NULL OR company.Id = @CompanyId)
              AND (@PlatformId IS NULL OR platform.Id = @PlatformId)
              AND (@ProductCategoryId IS NULL OR productCategory.Id = @ProductCategoryId)
              AND (@ActiveOnly = 0 OR product.IsActive = 1)
            ORDER BY category.Name, company.Name, platform.Name, productCategory.Name, product.Name;
        END;
        """;

    internal const string InventoryProductCreateProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryProductCreate
            @TenantId int,
            @ProductCategoryId int,
            @ProductCode nvarchar(80) = NULL,
            @ProductName nvarchar(200),
            @Quantity decimal(18,4),
            @UnitCost decimal(18,2),
            @CurrencyId int,
            @IsActive bit,
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;
            IF @UnitCost < 0 THROW 51000, 'Purchase price cannot be negative.', 1;

            BEGIN TRANSACTION;
            BEGIN TRY
                DECLARE @ResolvedCode nvarchar(80) = NULLIF(UPPER(LTRIM(RTRIM(@ProductCode))), N''),
                        @Prefix nvarchar(45), @Sequence int, @ProductId bigint, @ReceiptId bigint;

                IF @ResolvedCode IS NULL
                BEGIN
                    SELECT @Prefix = CONCAT(platform.Code, N'-', productCategory.Code, N'-')
                    FROM dbo.SaleRoznamchaProductCategories productCategory
                    INNER JOIN dbo.SaleRoznamchaPlatforms platform
                        ON platform.TenantId = productCategory.TenantId AND platform.Id = productCategory.PlatformId
                    WHERE productCategory.TenantId = @TenantId AND productCategory.Id = @ProductCategoryId;
                    IF @Prefix IS NULL THROW 51000, 'Selected product category is invalid.', 1;

                    SELECT @Sequence = COUNT(*) + 1
                    FROM dbo.SaleRoznamchaProducts WITH (UPDLOCK, HOLDLOCK)
                    WHERE TenantId = @TenantId AND ProductCode LIKE @Prefix + N'%';
                    SET @ResolvedCode = @Prefix + CASE WHEN @Sequence < 1000
                        THEN RIGHT(N'000' + CONVERT(nvarchar(10), @Sequence), 3)
                        ELSE CONVERT(nvarchar(10), @Sequence) END;
                    WHILE EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts WHERE TenantId = @TenantId AND ProductCode = @ResolvedCode)
                    BEGIN
                        SET @Sequence += 1;
                        SET @ResolvedCode = @Prefix + CASE WHEN @Sequence < 1000
                            THEN RIGHT(N'000' + CONVERT(nvarchar(10), @Sequence), 3)
                            ELSE CONVERT(nvarchar(10), @Sequence) END;
                    END
                END

                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts WITH (UPDLOCK, HOLDLOCK)
                           WHERE TenantId = @TenantId AND ProductCode = @ResolvedCode)
                    THROW 51000, 'Product code already exists.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts WITH (UPDLOCK, HOLDLOCK)
                           WHERE TenantId = @TenantId AND ProductCategoryId = @ProductCategoryId
                             AND Name = @ProductName AND IsDeleted = 0)
                    THROW 51000, 'This product already exists in the selected product category.', 1;

                INSERT INTO dbo.SaleRoznamchaProducts
                    (TenantId, ProductCategoryId, ProductCode, Name, QuantityOnHand, PurchasePrice, CurrencyId,
                     IsActive, IsDeleted, CreatedByUserId, CreatedOnUtc)
                VALUES
                    (@TenantId, @ProductCategoryId, @ResolvedCode, @ProductName, @Quantity, @UnitCost, @CurrencyId,
                     @IsActive, 0, @UserId, SYSUTCDATETIME());
                SET @ProductId = SCOPE_IDENTITY();

                IF @Quantity > 0
                BEGIN
                    INSERT INTO dbo.SaleRoznamchaStockReceipts
                        (TenantId, ProductId, PurchasedQuantity, RemainingQuantity, UnitCost, CurrencyId,
                         PurchasedOnUtc, CreatedByUserId, CreatedOnUtc)
                    VALUES (@TenantId, @ProductId, @Quantity, @Quantity, @UnitCost, @CurrencyId,
                            SYSUTCDATETIME(), @UserId, SYSUTCDATETIME());
                    SET @ReceiptId = SCOPE_IDENTITY();

                    INSERT INTO dbo.SaleRoznamchaInventoryMovements
                        (TenantId, ProductId, StockReceiptId, MovementType, QuantityDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)
                    VALUES (@TenantId, @ProductId, @ReceiptId, N'PURCHASE', @Quantity, @Quantity, @UserId, SYSUTCDATETIME());
                END

                COMMIT TRANSACTION;
                SELECT @ProductId Id;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
        END;
        """;

    internal const string InventoryProductUpdateProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryProductUpdate
            @TenantId int,
            @ProductId bigint,
            @ProductCategoryId int,
            @ProductCode nvarchar(80),
            @ProductName nvarchar(200),
            @IsActive bit,
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            DECLARE @ResolvedCode nvarchar(80) = UPPER(LTRIM(RTRIM(@ProductCode)));
            IF @ResolvedCode = N'' THROW 51000, 'Product code is required.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts
                           WHERE TenantId = @TenantId AND Id = @ProductId AND IsDeleted = 0)
                THROW 51000, 'Inventory product was not found.', 1;
            IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts
                       WHERE TenantId = @TenantId AND ProductCode = @ResolvedCode AND Id <> @ProductId)
                THROW 51000, 'Product code already exists.', 1;
            IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts
                       WHERE TenantId = @TenantId AND ProductCategoryId = @ProductCategoryId
                         AND Name = @ProductName AND IsDeleted = 0 AND Id <> @ProductId)
                THROW 51000, 'This product already exists in the selected product category.', 1;

            UPDATE dbo.SaleRoznamchaProducts
            SET ProductCategoryId = @ProductCategoryId, ProductCode = @ResolvedCode, Name = @ProductName,
                IsActive = @IsActive, UpdatedByUserId = @UserId, UpdatedOnUtc = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND Id = @ProductId AND IsDeleted = 0;
            SELECT @ProductId Id;
        END;
        """;

    internal const string InventoryStockAddProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryStockAdd
            @TenantId int,
            @ProductId bigint,
            @Quantity decimal(18,4),
            @UnitCost decimal(18,2),
            @CurrencyId int,
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            IF @Quantity <= 0 THROW 51000, 'Stock quantity must be greater than zero.', 1;
            IF @UnitCost < 0 THROW 51000, 'Purchase price cannot be negative.', 1;

            BEGIN TRANSACTION;
            BEGIN TRY
                DECLARE @CurrentBalance decimal(18,4), @BalanceAfter decimal(18,4), @ReceiptId bigint;
                SELECT @CurrentBalance = QuantityOnHand
                FROM dbo.SaleRoznamchaProducts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = @TenantId AND Id = @ProductId AND IsDeleted = 0;
                IF @CurrentBalance IS NULL THROW 51000, 'Inventory product was not found.', 1;
                SET @BalanceAfter = @CurrentBalance + @Quantity;

                INSERT INTO dbo.SaleRoznamchaStockReceipts
                    (TenantId, ProductId, PurchasedQuantity, RemainingQuantity, UnitCost, CurrencyId,
                     PurchasedOnUtc, CreatedByUserId, CreatedOnUtc)
                VALUES (@TenantId, @ProductId, @Quantity, @Quantity, @UnitCost, @CurrencyId,
                        SYSUTCDATETIME(), @UserId, SYSUTCDATETIME());
                SET @ReceiptId = SCOPE_IDENTITY();

                UPDATE dbo.SaleRoznamchaProducts
                SET QuantityOnHand = @BalanceAfter, PurchasePrice = @UnitCost, CurrencyId = @CurrencyId,
                    IsActive = 1, UpdatedByUserId = @UserId, UpdatedOnUtc = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND Id = @ProductId;

                INSERT INTO dbo.SaleRoznamchaInventoryMovements
                    (TenantId, ProductId, StockReceiptId, MovementType, QuantityDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)
                VALUES (@TenantId, @ProductId, @ReceiptId, N'PURCHASE', @Quantity, @BalanceAfter, @UserId, SYSUTCDATETIME());

                COMMIT TRANSACTION;
                SELECT @ProductId Id;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
        END;
        """;

    internal const string DailySaleListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_DailySaleList
            @TenantId int,
            @CategoryId int = NULL,
            @CompanyId int = NULL,
            @PlatformId int = NULL,
            @ProductCategoryId int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT sale.Id, product.Id ProductId, product.ProductCode, product.Name ProductName,
                   category.Id CategoryId, category.Name CategoryName,
                   company.Id CompanyId, company.Name CompanyName,
                   platform.Id PlatformId, platform.Name PlatformName,
                   productCategory.Id ProductCategoryId, productCategory.Name ProductCategoryName,
                   status.Id SaleStatusId, status.Code StatusCode, status.Name StatusName,
                   sale.Quantity, sale.InventoryDelta,
                   sale.BalanceAfter - sale.InventoryDelta BalanceBefore,
                   sale.BalanceAfter, sale.CreatedOnUtc
            FROM dbo.SaleRoznamchaDailySales sale
            INNER JOIN dbo.SaleRoznamchaProducts product
                ON product.TenantId = sale.TenantId AND product.Id = sale.ProductId
            INNER JOIN dbo.SaleRoznamchaProductCategories productCategory
                ON productCategory.TenantId = product.TenantId AND productCategory.Id = product.ProductCategoryId
            INNER JOIN dbo.SaleRoznamchaPlatforms platform
                ON platform.TenantId = productCategory.TenantId AND platform.Id = productCategory.PlatformId
            INNER JOIN dbo.SaleRoznamchaCompanies company
                ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
            INNER JOIN dbo.SaleRoznamchaCategories category
                ON category.TenantId = company.TenantId AND category.Id = company.CategoryId
            INNER JOIN dbo.SaleRoznamchaSaleStatuses status
                ON status.TenantId = sale.TenantId AND status.Id = sale.SaleStatusId
            WHERE sale.TenantId = @TenantId
              AND (@CategoryId IS NULL OR category.Id = @CategoryId)
              AND (@CompanyId IS NULL OR company.Id = @CompanyId)
              AND (@PlatformId IS NULL OR platform.Id = @PlatformId)
              AND (@ProductCategoryId IS NULL OR productCategory.Id = @ProductCategoryId)
            ORDER BY sale.CreatedOnUtc DESC, sale.Id DESC;
        END;
        """;

    internal const string DailySaleCreateProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_DailySaleCreate
            @TenantId int,
            @ProductId bigint,
            @SaleStatusId int,
            @Quantity decimal(18,4),
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            IF @Quantity <= 0 THROW 51000, 'Quantity must be greater than zero.', 1;

            DECLARE @InventoryEffect smallint, @StatusCode nvarchar(30),
                    @CurrentBalance decimal(18,4), @InventoryDelta decimal(18,4),
                    @BalanceAfter decimal(18,4), @DailySaleId bigint,
                    @RequiredStock decimal(18,4), @AvailableBatchStock decimal(18,4);
            DECLARE @Allocations TABLE
            (
                StockReceiptId bigint PRIMARY KEY,
                Quantity decimal(18,4) NOT NULL,
                UnitCost decimal(18,2) NOT NULL,
                CurrencyId int NOT NULL
            );

            SELECT @InventoryEffect = InventoryEffect, @StatusCode = Code
            FROM dbo.SaleRoznamchaSaleStatuses
            WHERE TenantId = @TenantId AND Id = @SaleStatusId AND IsActive = 1;

            IF @InventoryEffect IS NULL THROW 51000, 'Selected sale status is invalid or inactive.', 1;
            IF @InventoryEffect > 0 THROW 51000, 'Positive inventory statuses require the stock receipt workflow.', 1;

            BEGIN TRANSACTION;
            BEGIN TRY
                SELECT @CurrentBalance = QuantityOnHand
                FROM dbo.SaleRoznamchaProducts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = @TenantId AND Id = @ProductId AND IsActive = 1 AND IsDeleted = 0;

                IF @CurrentBalance IS NULL THROW 51000, 'Selected product is invalid or inactive.', 1;

                SET @InventoryDelta = CAST(@InventoryEffect AS decimal(18,4)) * @Quantity;
                SET @BalanceAfter = @CurrentBalance + @InventoryDelta;
                IF @BalanceAfter < 0 THROW 51001, 'Sale quantity exceeds available inventory.', 1;

                IF @InventoryDelta < 0
                BEGIN
                    SET @RequiredStock = -@InventoryDelta;
                    SELECT @AvailableBatchStock = COALESCE(SUM(RemainingQuantity), 0)
                    FROM dbo.SaleRoznamchaStockReceipts WITH (UPDLOCK, HOLDLOCK)
                    WHERE TenantId = @TenantId AND ProductId = @ProductId AND RemainingQuantity > 0;
                    IF @AvailableBatchStock < @RequiredStock
                        THROW 51001, 'Sale quantity exceeds available FIFO stock.', 1;

                    ;WITH OrderedReceipts AS
                    (
                        SELECT Id, RemainingQuantity, UnitCost, CurrencyId,
                               SUM(RemainingQuantity) OVER (ORDER BY PurchasedOnUtc, Id ROWS UNBOUNDED PRECEDING) RunningQuantity
                        FROM dbo.SaleRoznamchaStockReceipts
                        WHERE TenantId = @TenantId AND ProductId = @ProductId AND RemainingQuantity > 0
                    )
                    INSERT INTO @Allocations (StockReceiptId, Quantity, UnitCost, CurrencyId)
                    SELECT Id,
                           CASE
                               WHEN RunningQuantity <= @RequiredStock THEN RemainingQuantity
                               ELSE @RequiredStock - (RunningQuantity - RemainingQuantity)
                           END,
                           UnitCost,
                           CurrencyId
                    FROM OrderedReceipts
                    WHERE RunningQuantity - RemainingQuantity < @RequiredStock;

                    UPDATE receipt
                    SET RemainingQuantity = receipt.RemainingQuantity - allocation.Quantity
                    FROM dbo.SaleRoznamchaStockReceipts receipt
                    INNER JOIN @Allocations allocation ON allocation.StockReceiptId = receipt.Id
                    WHERE receipt.TenantId = @TenantId;
                END

                UPDATE dbo.SaleRoznamchaProducts
                SET QuantityOnHand = @BalanceAfter,
                    UpdatedByUserId = @UserId,
                    UpdatedOnUtc = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND Id = @ProductId;

                INSERT INTO dbo.SaleRoznamchaDailySales
                    (TenantId, ProductId, SaleStatusId, Quantity, InventoryDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)
                VALUES
                    (@TenantId, @ProductId, @SaleStatusId, @Quantity, @InventoryDelta, @BalanceAfter, @UserId, SYSUTCDATETIME());
                SET @DailySaleId = SCOPE_IDENTITY();

                INSERT INTO dbo.SaleRoznamchaDailySaleStockAllocations
                    (TenantId, DailySaleId, StockReceiptId, Quantity, UnitCost, CurrencyId, CreatedOnUtc)
                SELECT @TenantId, @DailySaleId, StockReceiptId, Quantity, UnitCost, CurrencyId, SYSUTCDATETIME()
                FROM @Allocations;

                INSERT INTO dbo.SaleRoznamchaInventoryMovements
                    (TenantId, ProductId, DailySaleId, MovementType, QuantityDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)
                VALUES
                    (@TenantId, @ProductId, @DailySaleId, LEFT(N'SALE:' + @StatusCode, 30),
                     @InventoryDelta, @BalanceAfter, @UserId, SYSUTCDATETIME());

                COMMIT TRANSACTION;
                SELECT @DailySaleId Id;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
        END;
        """;

    internal const string HistoryAggregateProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_HistoryAggregate
            @TenantId int,
            @FromDate date = NULL,
            @ToDate date = NULL,
            @CategoryId int = NULL,
            @CompanyId int = NULL,
            @PlatformId int = NULL,
            @ProductCategoryId int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;

            IF @FromDate IS NOT NULL AND @ToDate IS NOT NULL AND @FromDate > @ToDate
                THROW 51000, 'From date cannot be after To date.', 1;

            ;WITH Base AS
            (
                SELECT sale.Id, sale.ProductId, sale.Quantity, sale.InventoryDelta,
                       CONVERT(date, DATEADD(minute, 300, sale.CreatedOnUtc)) LocalSaleDate,
                       category.Id CategoryId, category.Name CategoryName,
                       company.Id CompanyId, company.Name CompanyName,
                       platform.Id PlatformId, platform.Name PlatformName,
                       productCategory.Id ProductCategoryId, productCategory.Name ProductCategoryName,
                       product.Name ProductName,
                       status.Id StatusId, status.Name StatusName
                FROM dbo.SaleRoznamchaDailySales sale
                INNER JOIN dbo.SaleRoznamchaProducts product
                    ON product.TenantId = sale.TenantId AND product.Id = sale.ProductId
                INNER JOIN dbo.SaleRoznamchaProductCategories productCategory
                    ON productCategory.TenantId = product.TenantId AND productCategory.Id = product.ProductCategoryId
                INNER JOIN dbo.SaleRoznamchaPlatforms platform
                    ON platform.TenantId = productCategory.TenantId AND platform.Id = productCategory.PlatformId
                INNER JOIN dbo.SaleRoznamchaCompanies company
                    ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
                INNER JOIN dbo.SaleRoznamchaCategories category
                    ON category.TenantId = company.TenantId AND category.Id = company.CategoryId
                INNER JOIN dbo.SaleRoznamchaSaleStatuses status
                    ON status.TenantId = sale.TenantId AND status.Id = sale.SaleStatusId
                WHERE sale.TenantId = @TenantId
                  AND (@FromDate IS NULL OR sale.CreatedOnUtc >= DATEADD(minute, -300, CONVERT(datetime2, @FromDate)))
                  AND (@ToDate IS NULL OR sale.CreatedOnUtc < DATEADD(minute, -300, DATEADD(day, 1, CONVERT(datetime2, @ToDate))))
                  AND (@CategoryId IS NULL OR category.Id = @CategoryId)
                  AND (@CompanyId IS NULL OR company.Id = @CompanyId)
                  AND (@PlatformId IS NULL OR platform.Id = @PlatformId)
                  AND (@ProductCategoryId IS NULL OR productCategory.Id = @ProductCategoryId)
            )
            SELECT CAST(N'SUMMARY' AS nvarchar(30)) Dimension,
                   CAST(N'TOTAL' AS nvarchar(200)) GroupKey,
                   CAST(N'Total' AS nvarchar(200)) Label,
                   CAST(NULL AS datetime2) PeriodStart,
                   CAST(0 AS bigint) SortOrder,
                   COUNT_BIG(*) TransactionCount,
                   COALESCE(SUM(Quantity), 0) Quantity,
                   COALESCE(SUM(InventoryDelta), 0) InventoryDelta,
                   COUNT(DISTINCT ProductId) ProductCount
            FROM Base

            UNION ALL
            SELECT N'TREND_DAY', CONVERT(nvarchar(200), LocalSaleDate, 23),
                   CONVERT(nvarchar(200), LocalSaleDate, 23), CONVERT(datetime2, LocalSaleDate),
                   CONVERT(bigint, CONVERT(char(8), LocalSaleDate, 112)),
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY LocalSaleDate

            UNION ALL
            SELECT N'TREND_MONTH', CONVERT(nvarchar(200), DATEFROMPARTS(YEAR(LocalSaleDate), MONTH(LocalSaleDate), 1), 23),
                   LEFT(DATENAME(month, LocalSaleDate), 3) + N' ' + CONVERT(nvarchar(4), YEAR(LocalSaleDate)),
                   CONVERT(datetime2, DATEFROMPARTS(YEAR(LocalSaleDate), MONTH(LocalSaleDate), 1)),
                   CONVERT(bigint, YEAR(LocalSaleDate) * 100 + MONTH(LocalSaleDate)),
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY YEAR(LocalSaleDate), MONTH(LocalSaleDate), DATENAME(month, LocalSaleDate)

            UNION ALL
            SELECT N'CATEGORY', CONVERT(nvarchar(200), CategoryId), CategoryName, NULL, CategoryId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY CategoryId, CategoryName

            UNION ALL
            SELECT N'COMPANY', CONVERT(nvarchar(200), CompanyId), CompanyName, NULL, CompanyId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY CompanyId, CompanyName

            UNION ALL
            SELECT N'PLATFORM', CONVERT(nvarchar(200), PlatformId), PlatformName, NULL, PlatformId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY PlatformId, PlatformName

            UNION ALL
            SELECT N'PRODUCT_CATEGORY', CONVERT(nvarchar(200), ProductCategoryId), ProductCategoryName, NULL, ProductCategoryId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY ProductCategoryId, ProductCategoryName

            UNION ALL
            SELECT N'PRODUCT', CONVERT(nvarchar(200), ProductId), ProductName, NULL, ProductId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), 1
            FROM Base GROUP BY ProductId, ProductName

            UNION ALL
            SELECT N'STATUS', CONVERT(nvarchar(200), StatusId), StatusName, NULL, StatusId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY StatusId, StatusName

            ORDER BY Dimension, SortOrder, Quantity DESC;
        END;
        """;
}
