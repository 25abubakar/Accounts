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
                SELECT company.Id, CAST(N'company' AS nvarchar(30)) EntityType, company.Name,
                       CAST(NULL AS nvarchar(20)) Code,
                       CAST(NULL AS int) ParentId, CAST(NULL AS nvarchar(160)) ParentName,
                       company.Id CompanyId, company.Name CompanyName,
                       CAST(NULL AS int) PlatformId, CAST(NULL AS nvarchar(160)) PlatformName,
                       company.IsActive, company.CreatedOnUtc, company.UpdatedOnUtc
                FROM dbo.SaleRoznamchaCompanies company
                WHERE company.TenantId = @TenantId

                UNION ALL

                SELECT platform.Id, CAST(N'platform' AS nvarchar(30)), platform.Name, platform.Code,
                       platform.CompanyId, company.Name,
                       company.Id, company.Name, platform.Id, platform.Name,
                       platform.IsActive, platform.CreatedOnUtc, platform.UpdatedOnUtc
                FROM dbo.SaleRoznamchaPlatforms platform
                INNER JOIN dbo.SaleRoznamchaCompanies company
                    ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
                WHERE platform.TenantId = @TenantId

                UNION ALL

                SELECT product.Id, CAST(N'product-category' AS nvarchar(30)), product.Name, product.Code,
                       product.PlatformId, platform.Name,
                       company.Id, company.Name, platform.Id, platform.Name,
                       product.IsActive, product.CreatedOnUtc, product.UpdatedOnUtc
                FROM dbo.SaleRoznamchaProductCategories product
                INNER JOIN dbo.SaleRoznamchaPlatforms platform
                    ON platform.TenantId = product.TenantId AND platform.Id = product.PlatformId
                INNER JOIN dbo.SaleRoznamchaCompanies company
                    ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
                WHERE product.TenantId = @TenantId
            )
            SELECT Id, EntityType, Name, Code, ParentId, ParentName,
                   CompanyId, CompanyName, PlatformId, PlatformName,
                   IsActive, CreatedOnUtc, UpdatedOnUtc
            FROM MasterRows
            WHERE (@EntityType IS NULL OR EntityType = @EntityType)
              AND (@ParentId IS NULL OR ParentId = @ParentId)
              AND (@ActiveOnly = 0 OR IsActive = 1)
            ORDER BY CASE EntityType WHEN N'company' THEN 1
                                     WHEN N'platform' THEN 2 WHEN N'product-category' THEN 3 ELSE 4 END,
                     CompanyName, PlatformName, Name;
        END;
        """;

    internal const string InventoryListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryList
            @TenantId int,
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
                   product.Description,
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
            LEFT JOIN dbo.AccountsCurrencies currency
                ON currency.Id = product.CurrencyId
            OUTER APPLY
            (
                SELECT SUM(receipt.PurchasedQuantity) TotalPurchasedQuantity,
                       MAX(receipt.PurchasedOnUtc) LastPurchasedOnUtc
                FROM dbo.PaymentRoznamcha receipt
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
                FROM dbo.PaymentRoznamcha receipt
                WHERE receipt.TenantId = product.TenantId AND receipt.ProductId = product.Id
                  AND receipt.CurrencyId = product.CurrencyId AND receipt.RemainingQuantity > 0
            ) latestCurrencyValue
            OUTER APPLY
            (
                SELECT
                (
                    SELECT receipt.CurrencyId currencyId, stockCurrency.Code currencyCode,
                           CAST(SUM(receipt.RemainingQuantity * receipt.UnitCost) AS decimal(38,2)) amount
                    FROM dbo.PaymentRoznamcha receipt
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
              AND (@CompanyId IS NULL OR company.Id = @CompanyId)
              AND (@PlatformId IS NULL OR platform.Id = @PlatformId)
              AND (@ProductCategoryId IS NULL OR productCategory.Id = @ProductCategoryId)
              AND (@ActiveOnly = 0 OR product.IsActive = 1)
            ORDER BY company.Name, platform.Name, productCategory.Name, product.Name;
        END;
        """;

    internal const string ProductMasterCreateProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_ProductCreate
            @TenantId int,
            @ProductCategoryId int,
            @ProductCode nvarchar(80) = NULL,
            @ProductName nvarchar(200),
            @Description nvarchar(1000) = NULL,
            @IsActive bit,
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            BEGIN TRANSACTION;
            BEGIN TRY
                DECLARE @ResolvedCode nvarchar(80) = NULLIF(UPPER(LTRIM(RTRIM(@ProductCode))), N''),
                        @Prefix nvarchar(45), @Sequence int, @ProductId bigint;

                IF @ResolvedCode IS NULL
                BEGIN
                    SELECT @Prefix = CONCAT(platform.Code, N'-', productCategory.Code, N'-')
                    FROM dbo.SaleRoznamchaProductCategories productCategory
                    INNER JOIN dbo.SaleRoznamchaPlatforms platform
                        ON platform.TenantId = productCategory.TenantId AND platform.Id = productCategory.PlatformId
                    WHERE productCategory.TenantId = @TenantId AND productCategory.Id = @ProductCategoryId
                      AND productCategory.IsActive = 1 AND platform.IsActive = 1;
                    IF @Prefix IS NULL THROW 51000, 'Selected product category is invalid or inactive.', 1;

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
                    (TenantId, ProductCategoryId, ProductCode, Name, Description, QuantityOnHand,
                     PurchasePrice, CurrencyId, IsActive, IsDeleted, CreatedByUserId, CreatedOnUtc)
                VALUES
                    (@TenantId, @ProductCategoryId, @ResolvedCode, @ProductName,
                     NULLIF(LTRIM(RTRIM(@Description)), N''), 0, NULL, NULL,
                     @IsActive, 0, @UserId, SYSUTCDATETIME());
                SET @ProductId = SCOPE_IDENTITY();

                COMMIT TRANSACTION;
                SELECT @ProductId Id;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
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
            @FromAccountId int,
            @ToAccountId int = NULL,
            @Description nvarchar(1000) = NULL,
            @IsActive bit,
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;
            IF @UnitCost < 0 THROW 51000, 'Purchase price cannot be negative.', 1;
            IF @FromAccountId IS NOT NULL AND @FromAccountId = @ToAccountId
                THROW 51000, 'From Account and To Account must be different.', 1;
            IF @FromAccountId IS NOT NULL AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @FromAccountId AND IsActive = 1)
                THROW 51000, 'Selected From Account was not found or is inactive.', 1;
            IF @ToAccountId IS NOT NULL AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @ToAccountId AND IsActive = 1)
                THROW 51000, 'Selected To Account was not found or is inactive.', 1;

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
                    (TenantId, ProductCategoryId, ProductCode, Name, Description, QuantityOnHand, PurchasePrice, CurrencyId,
                     IsActive, IsDeleted, CreatedByUserId, CreatedOnUtc)
                VALUES
                    (@TenantId, @ProductCategoryId, @ResolvedCode, @ProductName,
                     NULLIF(LTRIM(RTRIM(@Description)), N''), @Quantity, @UnitCost, @CurrencyId,
                     @IsActive, 0, @UserId, SYSUTCDATETIME());
                SET @ProductId = SCOPE_IDENTITY();

                IF @Quantity > 0
                BEGIN
                    INSERT INTO dbo.PaymentRoznamcha
                        (TenantId, ProductId, PurchasedQuantity, RemainingQuantity, UnitCost, CurrencyId,
                         FromAccountId, ToAccountId, Description,
                         PurchasedOnUtc, CreatedByUserId, CreatedOnUtc)
                    VALUES (@TenantId, @ProductId, @Quantity, @Quantity, @UnitCost, @CurrencyId,
                            @FromAccountId, @ToAccountId,
                            NULLIF(LTRIM(RTRIM(@Description)), N''),
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
            @Description nvarchar(1000) = NULL,
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
                Description = NULLIF(LTRIM(RTRIM(@Description)), N''),
                IsActive = @IsActive, UpdatedByUserId = @UserId, UpdatedOnUtc = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND Id = @ProductId AND IsDeleted = 0;
            SELECT @ProductId Id;
        END;
        """;

    internal const string PostInventoryPurchasePaymentProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_PostInventoryPurchasePayment
            @TenantId int,
            @ReceiptId bigint,
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE
                @ExistingEntryId bigint,
                @ProductId bigint,
                @Reference nvarchar(100),
                @Quantity decimal(18,4),
                @UnitCost decimal(18,2),
                @Debit decimal(18,2),
                @UsdAmt decimal(18,2),
                @CurrencyId int,
                @FromAccountId int,
                @ToAccountId int,
                @PurchasedDate date,
                @Description nvarchar(1000),
                @Attachment nvarchar(260),
                @CategoryId int,
                @PaymentRozTypeId int,
                @TransTypeId int,
                @ProductName nvarchar(200),
                @TenantCode nvarchar(50),
                @TenantName nvarchar(150),
                @Prefix nvarchar(20) = N'',
                @Index int = 1,
                @NameLen int,
                @InWord bit = 1,
                @Char nchar(1),
                @Starts nvarchar(40),
                @Next int,
                @SNo int,
                @EntryId bigint,
                @Descriptions nvarchar(2000);

            SELECT
                @ExistingEntryId = receipt.RoznamchaEntryId,
                @ProductId = receipt.ProductId,
                @Reference = receipt.Reference,
                @Quantity = receipt.PurchasedQuantity,
                @UnitCost = receipt.UnitCost,
                @Debit = receipt.Debit,
                @UsdAmt = receipt.UsdAmt,
                @CurrencyId = receipt.CurrencyId,
                @FromAccountId = receipt.FromAccountId,
                @ToAccountId = receipt.ToAccountId,
                @PurchasedDate = receipt.PurchasedDate,
                @Description = receipt.Description,
                @Attachment = receipt.AttachmentOriginalName
            FROM dbo.PaymentRoznamcha receipt WITH (UPDLOCK, HOLDLOCK)
            WHERE receipt.TenantId = @TenantId AND receipt.Id = @ReceiptId;

            IF @ProductId IS NULL THROW 51000, 'Inventory purchase was not found.', 1;
            IF @ExistingEntryId IS NOT NULL RETURN;
            IF @FromAccountId IS NULL THROW 51000, 'From Account is required before a purchase can post to Payment Roznamcha.', 1;

            SELECT @CategoryId = fromAccount.CategoryId
            FROM dbo.AccountsChartAccounts fromAccount
            WHERE fromAccount.TenantId = @TenantId AND fromAccount.Id = @FromAccountId AND fromAccount.IsActive = 1;
            IF @CategoryId IS NULL THROW 51000, 'Selected From Account was not found or is inactive.', 1;

            IF @ToAccountId IS NOT NULL AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @ToAccountId AND IsActive = 1)
                THROW 51000, 'Selected To Account was not found or is inactive.', 1;

            SELECT @PaymentRozTypeId = COALESCE(
                settings.PaymentRozTypeId,
                (
                    SELECT TOP (1) rozType.Id
                    FROM dbo.AccountsRoznamchaTypes rozType
                    WHERE rozType.IsActive = 1
                      AND rozType.Code = N'PAYMENT'
                    ORDER BY rozType.Id
                ))
            FROM (SELECT @TenantId TenantId) seed
            LEFT JOIN dbo.AccountsModuleSettings settings ON settings.TenantId = seed.TenantId;
            IF @PaymentRozTypeId IS NULL THROW 51000, 'Payment Roznamcha type is not configured.', 1;

            SELECT TOP (1) @TransTypeId = transType.Id
            FROM dbo.AccountsTransTypes transType
            WHERE transType.IsActive = 1
              AND (transType.Code = N'PAYMENT' OR transType.Name = N'Payment')
            ORDER BY transType.Id;

            SELECT @ProductName = product.Name
            FROM dbo.SaleRoznamchaProducts product
            WHERE product.TenantId = @TenantId AND product.Id = @ProductId;
            IF @ProductName IS NULL THROW 51000, 'Inventory product was not found.', 1;

            SELECT @TenantCode = tenant.TenantCode, @TenantName = tenant.TenantName
            FROM dbo.Tenants tenant
            WHERE tenant.Id = @TenantId;

            SET @Prefix = UPPER(LTRIM(RTRIM(COALESCE(@TenantCode, N''))));
            IF @Prefix = N''
            BEGIN
                SET @NameLen = LEN(COALESCE(@TenantName, N''));
                WHILE @Index <= @NameLen
                BEGIN
                    SET @Char = SUBSTRING(@TenantName, @Index, 1);
                    IF @Char = N' ' SET @InWord = 1;
                    ELSE IF @InWord = 1
                    BEGIN
                        SET @Prefix = @Prefix + UPPER(@Char);
                        SET @InWord = 0;
                    END;
                    SET @Index += 1;
                END;
            END;
            IF @Prefix = N'' SET @Prefix = N'ORG';
            SET @Starts = @Prefix + N'DB-';

            SELECT
                @SNo = COUNT(*) + 1,
                @Next = COALESCE(MAX(
                    CASE
                        WHEN entry.Ref LIKE @Starts + N'%'
                        THEN TRY_CONVERT(int, RIGHT(entry.Ref, NULLIF(CHARINDEX(N'-', REVERSE(entry.Ref)), 0) - 1))
                    END), 0) + 1
            FROM dbo.RoznamchaEntries entry WITH (UPDLOCK, HOLDLOCK)
            WHERE entry.TenantId = @TenantId;

            SET @Descriptions = LEFT(
                CONCAT(
                    @ProductName,
                    CASE WHEN NULLIF(LTRIM(RTRIM(@Description)), N'') IS NULL THEN N'' ELSE N' — ' + LTRIM(RTRIM(@Description)) END),
                2000);

            INSERT INTO dbo.RoznamchaEntries
                (TenantId, SNo, Ref, OldRef, RoznamchaTypeId, CategoryId, FromAccountId, ToAccountId,
                 TransTypeId, Descriptions, CurrencyId, Amount, BalanceAmount, Qty, Rate,
                 Attachment, Remarks, TransDate, UsdAmount, IsShow, IsApproved, IsSettled,
                 IsFromLibrary, IsLocked, IsLedger, IsManual, IsDeleted, CreatedByUserId, CreatedOnUtc)
            VALUES
                (@TenantId, @SNo, @Starts + RIGHT(N'00000' + CONVERT(nvarchar(12), @Next), 5),
                 NULLIF(LTRIM(RTRIM(@Reference)), N''), @PaymentRozTypeId, @CategoryId, @FromAccountId, @ToAccountId,
                 @TransTypeId, @Descriptions, @CurrencyId, @Debit, @Debit, @Quantity, @UnitCost,
                 NULLIF(LTRIM(RTRIM(@Attachment)), N''), N'Inventory purchase', @PurchasedDate, @UsdAmt,
                 1, 0, 0, 0, 1, 1, 0, 0, @UserId, SYSUTCDATETIME());
            SET @EntryId = SCOPE_IDENTITY();

            UPDATE dbo.PaymentRoznamcha
            SET RoznamchaEntryId = @EntryId
            WHERE TenantId = @TenantId AND Id = @ReceiptId;

            INSERT INTO dbo.RoznamchaEntryProcessLogs
                (TenantId, RoznamchaEntryId, Action, Notes, ProcessedByUserId, ProcessedOnUtc)
            VALUES
                (@TenantId, @EntryId, N'InventoryPurchase', N'Posted from inventory stock receipt.', @UserId, SYSUTCDATETIME());
        END;
        """;

    internal const string InventoryStockAddProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryStockAdd
            @TenantId int,
            @ProductId bigint,
            @Reference nvarchar(100) = NULL,
            @Quantity decimal(18,4),
            @UnitCost decimal(18,2),
            @TotalPurchaseCost decimal(18,2),
            @UsdAmount decimal(18,2) = NULL,
            @ExchangeRate decimal(18,6) = NULL,
            @CurrencyId int,
            @FromAccountId int,
            @ToAccountId int = NULL,
            @PurchasedDate date = NULL,
            @Description nvarchar(1000) = NULL,
            @UserId nvarchar(450) = NULL,
            @RoznamchaEntryId bigint = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;
            IF @ExchangeRate < 0 THROW 51000, 'Exchange rate cannot be negative.', 1;
            IF @FromAccountId IS NULL THROW 51000, 'From Account is required.', 1;
            IF @FromAccountId = @ToAccountId
                THROW 51000, 'From Account and To Account must be different.', 1;
            IF NOT EXISTS
               (SELECT 1 FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @FromAccountId AND IsActive = 1)
                THROW 51000, 'Selected From Account was not found or is inactive.', 1;
            IF @ToAccountId IS NOT NULL AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @ToAccountId AND IsActive = 1)
                THROW 51000, 'Selected To Account was not found or is inactive.', 1;

            BEGIN TRANSACTION;
            BEGIN TRY
                DECLARE @CurrentBalance decimal(18,4), @BalanceAfter decimal(18,4), @ReceiptId bigint;
                SELECT @CurrentBalance = QuantityOnHand
                FROM dbo.SaleRoznamchaProducts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = @TenantId AND Id = @ProductId AND IsDeleted = 0;
                IF @CurrentBalance IS NULL THROW 51000, 'Inventory product was not found.', 1;
                SET @BalanceAfter = @CurrentBalance + @Quantity;

                INSERT INTO dbo.PaymentRoznamcha
                    (TenantId, ProductId, Reference, PurchasedQuantity, RemainingQuantity, UnitCost,
                     Debit, UsdAmt, ExchangeRate, CurrencyId,
                     FromAccountId, ToAccountId, RoznamchaEntryId, PurchasedDate, Description,
                     PurchasedOnUtc, CreatedByUserId, CreatedOnUtc)
                VALUES (@TenantId, @ProductId, NULLIF(LTRIM(RTRIM(@Reference)), N''), @Quantity, @Quantity, @UnitCost,
                        @TotalPurchaseCost, @UsdAmount, @ExchangeRate, @CurrencyId,
                        @FromAccountId, @ToAccountId, @RoznamchaEntryId,
                        COALESCE(@PurchasedDate, CONVERT(date, SYSUTCDATETIME())),
                        NULLIF(LTRIM(RTRIM(@Description)), N''),
                        SYSUTCDATETIME(), @UserId, SYSUTCDATETIME());
                SET @ReceiptId = SCOPE_IDENTITY();

                UPDATE dbo.SaleRoznamchaProducts
                SET PurchasePrice = @UnitCost, CurrencyId = @CurrencyId,
                    QuantityOnHand = CASE WHEN @Quantity > 0 THEN @BalanceAfter ELSE QuantityOnHand END,
                    IsActive = 1, UpdatedByUserId = @UserId, UpdatedOnUtc = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND Id = @ProductId;

                IF @Quantity > 0
                    INSERT INTO dbo.SaleRoznamchaInventoryMovements
                        (TenantId, ProductId, StockReceiptId, MovementType, QuantityDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)
                    VALUES (@TenantId, @ProductId, @ReceiptId, N'PURCHASE', @Quantity, @BalanceAfter, @UserId, SYSUTCDATETIME());

                IF @RoznamchaEntryId IS NULL
                    EXEC dbo.usp_SaleRoznamcha_PostInventoryPurchasePayment
                        @TenantId = @TenantId,
                        @ReceiptId = @ReceiptId,
                        @UserId = @UserId;
                ELSE
                    UPDATE dbo.RoznamchaEntries
                    SET IsLocked = 1, UpdatedByUserId = @UserId, UpdatedOnUtc = SYSUTCDATETIME()
                    WHERE TenantId = @TenantId AND Id = @RoznamchaEntryId;

                COMMIT TRANSACTION;
                SELECT @ProductId Id;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
        END;
        """;

    internal const string AccountHierarchyView =
        """
        CREATE OR ALTER VIEW dbo.vw_AccountsChartAccountHierarchy
        AS
        WITH AccountTree AS
        (
            SELECT account.Id,
                   account.TenantId,
                   account.CategoryId,
                   account.ParentId,
                   account.AccountNumber,
                   account.AccountCode,
                   account.AccountName,
                   account.IsActive,
                   CAST(account.AccountName AS nvarchar(max)) AccountPath
            FROM dbo.AccountsChartAccounts account
            WHERE account.ParentId IS NULL

            UNION ALL

            SELECT child.Id,
                   child.TenantId,
                   parent.CategoryId,
                   child.ParentId,
                   child.AccountNumber,
                   child.AccountCode,
                   child.AccountName,
                   child.IsActive,
                   CAST(parent.AccountPath + N' / ' + child.AccountName AS nvarchar(max)) AccountPath
            FROM dbo.AccountsChartAccounts child
            INNER JOIN AccountTree parent
                ON parent.TenantId = child.TenantId
               AND parent.Id = child.ParentId
        )
        SELECT Id, TenantId, CategoryId, ParentId, AccountNumber, AccountCode,
               AccountName, IsActive, AccountPath
        FROM AccountTree;
        """;

    internal const string ToAccountCategoryView =
        """
        CREATE OR ALTER VIEW dbo.vw_RoznamchaToAccountCategory
        AS
        SELECT chart.Id ToAccountId,
               chart.TenantId,
               COALESCE(chart.CategoryId, hierarchy.CategoryId) ToCategoryId,
               category.Name ToCategoryName,
               category.Code ToCategoryCode,
               chart.AccountName ToAccountName,
               chart.AccountNumber ToAccountNumber,
               chart.AccountCode ToAccountCode,
               chart.ParentId,
               chart.IsActive
        FROM dbo.AccountsChartAccounts chart
        LEFT JOIN dbo.vw_AccountsChartAccountHierarchy hierarchy
            ON hierarchy.Id = chart.Id AND hierarchy.TenantId = chart.TenantId
        LEFT JOIN dbo.AccountsCategories category
            ON category.TenantId = chart.TenantId
           AND category.Id = COALESCE(chart.CategoryId, hierarchy.CategoryId);
        """;

    internal const string InventoryPurchaseDetailsView =
        """
        CREATE OR ALTER VIEW dbo.vw_SaleRoznamcha_InventoryPurchaseDetails
        AS
        SELECT receipt.TenantId,
               receipt.Id,
               product.Id ProductId,
               receipt.Reference,
               product.ProductCode,
               product.Name ProductName,
               company.Id CompanyId,
               company.Name CompanyName,
               platform.Id PlatformId,
               platform.Name PlatformName,
               productCategory.Id ProductCategoryId,
               productCategory.Name ProductCategoryName,
               product.IsDeleted ProductIsDeleted,
               receipt.PurchasedQuantity,
               receipt.PurchasedQuantity - receipt.RemainingQuantity SoldQuantity,
               receipt.RemainingQuantity,
               receipt.UnitCost,
               receipt.CurrencyId,
               currency.Code CurrencyCode,
               fromAccount.CategoryId FromCategoryId,
               fromCategory.Code FromCategoryCode,
               fromCategory.Name FromCategoryName,
               receipt.FromAccountId,
               COALESCE(fromAccount.AccountCode, fromAccount.AccountNumber) FromAccountCode,
               fromAccount.AccountName FromAccountName,
               fromAccount.AccountPath FromAccountPath,
               receipt.ToAccountId,
               COALESCE(toAccount.AccountCode, toAccount.AccountNumber) ToAccountCode,
               toAccount.AccountName ToAccountName,
               receipt.Debit TotalPurchaseCost,
               receipt.UsdAmt UsdAmount,
               receipt.ExchangeRate,
               CAST(receipt.RemainingQuantity * receipt.UnitCost AS decimal(38,2)) RemainingStockValue,
               receipt.PurchasedDate,
               receipt.PurchasedOnUtc,
               receipt.Description,
               CONVERT(bit, CASE WHEN receipt.AttachmentStoredName IS NULL THEN 0 ELSE 1 END) HasAttachment,
               receipt.AttachmentOriginalName AttachmentName,
               CAST(CASE
                   WHEN receipt.PurchasedQuantity = 0 THEN N'Charge / Adjustment'
                   WHEN receipt.RemainingQuantity <= 0 THEN N'Fully Sold'
                   WHEN receipt.RemainingQuantity < receipt.PurchasedQuantity THEN N'Partially Sold'
                   ELSE N'Available'
               END AS nvarchar(30)) BatchStatus
        FROM dbo.PaymentRoznamcha receipt
        INNER JOIN dbo.SaleRoznamchaProducts product
            ON product.TenantId = receipt.TenantId AND product.Id = receipt.ProductId
        INNER JOIN dbo.SaleRoznamchaProductCategories productCategory
            ON productCategory.TenantId = product.TenantId AND productCategory.Id = product.ProductCategoryId
        INNER JOIN dbo.SaleRoznamchaPlatforms platform
            ON platform.TenantId = productCategory.TenantId AND platform.Id = productCategory.PlatformId
        INNER JOIN dbo.SaleRoznamchaCompanies company
            ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
        INNER JOIN dbo.AccountsCurrencies currency
            ON currency.Id = receipt.CurrencyId
        LEFT JOIN dbo.vw_AccountsChartAccountHierarchy fromAccount
            ON fromAccount.TenantId = receipt.TenantId AND fromAccount.Id = receipt.FromAccountId
        LEFT JOIN dbo.AccountsCategories fromCategory
            ON fromCategory.TenantId = fromAccount.TenantId AND fromCategory.Id = fromAccount.CategoryId
        LEFT JOIN dbo.AccountsChartAccounts toAccount
            ON toAccount.TenantId = receipt.TenantId AND toAccount.Id = receipt.ToAccountId;
        """;

    internal const string InventoryPurchaseListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryPurchaseList
            @TenantId int,
            @CompanyId int = NULL,
            @PlatformId int = NULL,
            @ProductCategoryId int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT Id, ProductId, Reference, ProductCode, ProductName,
                   CompanyId, CompanyName, PlatformId, PlatformName,
                   ProductCategoryId, ProductCategoryName, PurchasedQuantity, SoldQuantity,
                   RemainingQuantity, UnitCost, CurrencyId, CurrencyCode,
                   FromCategoryId, FromCategoryCode, FromCategoryName,
                   FromAccountId, FromAccountCode, FromAccountName, FromAccountPath,
                   ToAccountId, ToAccountCode, ToAccountName,
                   TotalPurchaseCost, UsdAmount, ExchangeRate, RemainingStockValue,
                   PurchasedDate, PurchasedOnUtc, Description, HasAttachment, AttachmentName, BatchStatus
            FROM dbo.vw_SaleRoznamcha_InventoryPurchaseDetails
            WHERE TenantId = @TenantId
              AND ProductIsDeleted = 0
              AND (@CompanyId IS NULL OR CompanyId = @CompanyId)
              AND (@PlatformId IS NULL OR PlatformId = @PlatformId)
              AND (@ProductCategoryId IS NULL OR ProductCategoryId = @ProductCategoryId)
            ORDER BY PurchasedOnUtc DESC, Id DESC;
        END;
        """;

    internal const string DailySaleListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_DailySaleList
            @TenantId int,
            @CompanyId int = NULL,
            @PlatformId int = NULL,
            @ProductCategoryId int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT sale.Id, product.Id ProductId, product.ProductCode, product.Name ProductName,
                   company.Id CompanyId, company.Name CompanyName,
                   platform.Id PlatformId, platform.Name PlatformName,
                   productCategory.Id ProductCategoryId, productCategory.Name ProductCategoryName,
                   status.Id SaleStatusId, status.Code StatusCode, status.Name StatusName,
                   sale.MarketplaceCompanyId, sale.OrderId, sale.Quantity,
                   sale.UnitCost, sale.UnitCostCurrencyId, unitCostCurrency.Code UnitCostCurrencyCode,
                   sale.SaleCurrencyId, saleCurrency.Code SaleCurrencyCode,
                   sale.PayChargeEffectId, payChargeEffect.Name PayChargeEffectName,
                   payChargeEffect.Multiplier PayChargeMultiplier,
                   sale.UnitSalePrice, sale.BaseSaleAmount, sale.TotalAdditions,
                   sale.TotalDeductions, sale.NetSaleTotal,
                   sale.GrossTotal, sale.TotalRevenue, sale.CostAmount,
                   sale.PayCharges, sale.DeliveryFee, sale.OtherAmount,
                   sale.PromotionAmount, sale.RtoChargeAmount,
                   sale.TransactionTypeId, transactionType.Name TransactionTypeName,
                   sale.PaidStatusId, paidStatus.Name PaidStatusName,
                   sale.ToAccountId, account.AccountName ToAccountName,
                   sale.InterProReference, sale.OrderReference, sale.SaleDate, sale.Description,
                   COALESCE(chargeTotals.ChargeCount, 0) ChargeCount,
                   COALESCE(chargeTotals.AdditionalChargeTotal, 0) AdditionalChargeTotal,
                   COALESCE(chargeTotals.ChargeDetails, N'') ChargeDetails,
                   COALESCE(dateTotals.TransactionDateCount, 0) TransactionDateCount,
                   COALESCE(dateTotals.TransactionDateDetails, N'') TransactionDateDetails,
                   sale.InventoryDelta,
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
            INNER JOIN dbo.SaleRoznamchaSaleStatuses status
                ON status.TenantId = sale.TenantId AND status.Id = sale.SaleStatusId
            LEFT JOIN dbo.AccountsTransTypes transactionType
                ON transactionType.Id = sale.TransactionTypeId
            LEFT JOIN dbo.SaleRoznamchaPaidStatuses paidStatus
                ON paidStatus.TenantId = sale.TenantId AND paidStatus.Id = sale.PaidStatusId
            LEFT JOIN dbo.AccountsChartAccounts account
                ON account.TenantId = sale.TenantId AND account.Id = sale.ToAccountId
            LEFT JOIN dbo.AccountsCurrencies unitCostCurrency
                ON unitCostCurrency.Id = sale.UnitCostCurrencyId
            INNER JOIN dbo.AccountsCurrencies saleCurrency
                ON saleCurrency.Id = sale.SaleCurrencyId
            INNER JOIN dbo.SaleRoznamchaAmountEffects payChargeEffect
                ON payChargeEffect.TenantId = sale.TenantId AND payChargeEffect.Id = sale.PayChargeEffectId
            OUTER APPLY
            (
                SELECT COUNT(*) ChargeCount,
                       COALESCE(SUM(charge.Amount), 0) AdditionalChargeTotal,
                       STRING_AGG(CONVERT(nvarchar(max), CONCAT(chargeLabel.Name, N': ', CONVERT(nvarchar(50), charge.Amount))), N'; ') ChargeDetails
                FROM dbo.SaleRoznamchaDailySaleCharges charge
                INNER JOIN dbo.SaleRoznamchaChargeLabels chargeLabel
                    ON chargeLabel.TenantId = charge.TenantId AND chargeLabel.Id = charge.ChargeLabelId
                WHERE charge.TenantId = sale.TenantId AND charge.DailySaleId = sale.Id
            ) chargeTotals
            OUTER APPLY
            (
                SELECT COUNT(*) TransactionDateCount,
                       STRING_AGG(CONVERT(nvarchar(max), CONCAT(dateLabel.Name, N': ', CONVERT(nvarchar(10), transactionDate.Value, 23))), N'; ') TransactionDateDetails
                FROM dbo.SaleRoznamchaDailySaleTransactionDates transactionDate
                INNER JOIN dbo.SaleRoznamchaTransactionDateLabels dateLabel
                    ON dateLabel.TenantId = transactionDate.TenantId AND dateLabel.Id = transactionDate.DateLabelId
                WHERE transactionDate.TenantId = sale.TenantId AND transactionDate.DailySaleId = sale.Id
            ) dateTotals
            WHERE sale.TenantId = @TenantId
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
            @MarketplaceCompanyId nvarchar(100) = NULL,
            @OrderId nvarchar(100) = NULL,
            @UnitSalePrice decimal(18,2),
            @SaleCurrencyId int,
            @PayChargeEffectId int,
            @PayCharges decimal(18,2) = 0,
            @DeliveryFee decimal(18,2) = 0,
            @OtherAmount decimal(18,2) = 0,
            @PromotionAmount decimal(18,2) = 0,
            @RtoChargeAmount decimal(18,2) = 0,
            @TransactionTypeId int = NULL,
            @PaidStatusId int = NULL,
            @ToAccountId int = NULL,
            @InterProReference nvarchar(100) = NULL,
            @OrderReference nvarchar(100) = NULL,
            @SaleDate date = NULL,
            @Description nvarchar(2000) = NULL,
            @ChargesJson nvarchar(max) = N'[]',
            @TransactionDatesJson nvarchar(max) = N'[]',
            @UserId nvarchar(450) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            IF @Quantity <= 0 THROW 51000, 'Quantity must be greater than zero.', 1;
            IF @UnitSalePrice <= 0 OR @PayCharges < 0
               OR @DeliveryFee < 0 OR @OtherAmount < 0 OR @PromotionAmount < 0 OR @RtoChargeAmount < 0
                THROW 51000, 'Amounts cannot be negative.', 1;
            IF ISJSON(COALESCE(@ChargesJson, N'[]')) <> 1 OR ISJSON(COALESCE(@TransactionDatesJson, N'[]')) <> 1
                THROW 51000, 'Daily sale detail rows are invalid.', 1;

            DECLARE @InventoryEffect smallint, @StatusCode nvarchar(30),
                    @CurrentBalance decimal(18,4), @InventoryDelta decimal(18,4),
                    @BalanceAfter decimal(18,4), @DailySaleId bigint,
                    @RequiredStock decimal(18,4), @AvailableBatchStock decimal(18,4),
                    @UnitCost decimal(18,2), @UnitCostCurrencyId int,
                    @PayChargeMultiplier smallint, @AdditionalChargeTotal decimal(18,2),
                    @BaseSaleAmount decimal(18,2), @TotalAdditions decimal(18,2),
                    @TotalDeductions decimal(18,2), @NetSaleTotal decimal(18,2);
            DECLARE @Allocations TABLE
            (
                StockReceiptId bigint PRIMARY KEY,
                Quantity decimal(18,4) NOT NULL,
                UnitCost decimal(18,2) NOT NULL,
                CurrencyId int NOT NULL
            );
            DECLARE @Charges TABLE (ChargeLabelId int NOT NULL, Amount decimal(18,2) NOT NULL);
            DECLARE @TransactionDates TABLE (DateLabelId int NOT NULL, Value date NOT NULL);

            INSERT INTO @Charges (ChargeLabelId, Amount)
            SELECT ChargeLabelId, Amount
            FROM OPENJSON(COALESCE(@ChargesJson, N'[]'))
            WITH (ChargeLabelId int '$.ChargeLabelId', Amount decimal(18,2) '$.Amount');

            INSERT INTO @TransactionDates (DateLabelId, Value)
            SELECT DateLabelId, Value
            FROM OPENJSON(COALESCE(@TransactionDatesJson, N'[]'))
            WITH (DateLabelId int '$.DateLabelId', Value date '$.Value');

            IF EXISTS (SELECT 1 FROM @Charges WHERE ChargeLabelId <= 0 OR Amount < 0)
                THROW 51000, 'Every charge row requires a valid label and non-negative amount.', 1;
            IF EXISTS (SELECT 1 FROM @TransactionDates WHERE DateLabelId <= 0 OR Value IS NULL)
                THROW 51000, 'Every transaction date row requires a valid label and date.', 1;
            IF EXISTS
            (
                SELECT 1 FROM @Charges item
                LEFT JOIN dbo.SaleRoznamchaChargeLabels label
                    ON label.TenantId = @TenantId AND label.Id = item.ChargeLabelId AND label.IsActive = 1
                WHERE label.Id IS NULL
            ) THROW 51000, 'A selected charge label is invalid or inactive.', 1;
            IF EXISTS
            (
                SELECT 1 FROM @TransactionDates item
                LEFT JOIN dbo.SaleRoznamchaTransactionDateLabels label
                    ON label.TenantId = @TenantId AND label.Id = item.DateLabelId AND label.IsActive = 1
                WHERE label.Id IS NULL
            ) THROW 51000, 'A selected transaction date label is invalid or inactive.', 1;

            IF @TransactionTypeId IS NOT NULL AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountsTransTypes WHERE Id = @TransactionTypeId AND IsActive = 1)
                THROW 51000, 'Selected transaction type is invalid or inactive.', 1;
            IF @PaidStatusId IS NOT NULL AND NOT EXISTS
               (SELECT 1 FROM dbo.SaleRoznamchaPaidStatuses WHERE TenantId = @TenantId AND Id = @PaidStatusId AND IsActive = 1)
                THROW 51000, 'Selected paid status is invalid or inactive.', 1;
            IF @ToAccountId IS NOT NULL AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @ToAccountId AND IsActive = 1)
                THROW 51000, 'Selected account is invalid or inactive.', 1;
            IF NOT EXISTS
               (SELECT 1 FROM dbo.AccountsCurrencies WHERE Id = @SaleCurrencyId AND IsActive = 1)
                THROW 51000, 'Selected sale currency is invalid or inactive.', 1;

            SELECT @PayChargeMultiplier = Multiplier
            FROM dbo.SaleRoznamchaAmountEffects
            WHERE TenantId = @TenantId AND Id = @PayChargeEffectId AND IsActive = 1;
            IF @PayChargeMultiplier NOT IN (-1, 1)
                THROW 51000, 'Selected Pay Charge treatment is invalid or inactive.', 1;

            SELECT @AdditionalChargeTotal = COALESCE(SUM(Amount), 0) FROM @Charges;
            SET @BaseSaleAmount = ROUND(@Quantity * @UnitSalePrice, 2);
            SET @TotalAdditions = ROUND(@DeliveryFee + @OtherAmount + @AdditionalChargeTotal
                                  + CASE WHEN @PayChargeMultiplier = 1 THEN @PayCharges ELSE 0 END, 2);
            SET @TotalDeductions = ROUND(@PromotionAmount + @RtoChargeAmount
                                   + CASE WHEN @PayChargeMultiplier = -1 THEN @PayCharges ELSE 0 END, 2);
            SET @NetSaleTotal = ROUND(@BaseSaleAmount + @TotalAdditions - @TotalDeductions, 2);
            IF @NetSaleTotal < 0 THROW 51000, 'Net sale total cannot be negative.', 1;

            SELECT @InventoryEffect = InventoryEffect, @StatusCode = Code
            FROM dbo.SaleRoznamchaSaleStatuses
            WHERE TenantId = @TenantId AND Id = @SaleStatusId AND IsActive = 1;

            IF @InventoryEffect IS NULL THROW 51000, 'Selected sale status is invalid or inactive.', 1;
            IF @InventoryEffect > 0 THROW 51000, 'Positive inventory statuses require the stock receipt workflow.', 1;

            BEGIN TRANSACTION;
            BEGIN TRY
                SELECT @CurrentBalance = QuantityOnHand,
                       @UnitCost = PurchasePrice,
                       @UnitCostCurrencyId = CurrencyId
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
                    FROM dbo.PaymentRoznamcha WITH (UPDLOCK, HOLDLOCK)
                    WHERE TenantId = @TenantId AND ProductId = @ProductId AND RemainingQuantity > 0;
                    IF @AvailableBatchStock < @RequiredStock
                        THROW 51001, 'Sale quantity exceeds available FIFO stock.', 1;

                    ;WITH OrderedReceipts AS
                    (
                        SELECT Id, RemainingQuantity, UnitCost, CurrencyId,
                               SUM(RemainingQuantity) OVER (ORDER BY PurchasedOnUtc, Id ROWS UNBOUNDED PRECEDING) RunningQuantity
                        FROM dbo.PaymentRoznamcha
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
                    FROM dbo.PaymentRoznamcha receipt
                    INNER JOIN @Allocations allocation ON allocation.StockReceiptId = receipt.Id
                    WHERE receipt.TenantId = @TenantId;
                END

                UPDATE dbo.SaleRoznamchaProducts
                SET QuantityOnHand = @BalanceAfter,
                    UpdatedByUserId = @UserId,
                    UpdatedOnUtc = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND Id = @ProductId;

                INSERT INTO dbo.SaleRoznamchaDailySales
                    (TenantId, ProductId, SaleStatusId, MarketplaceCompanyId, OrderId, Quantity,
                     UnitCost, UnitCostCurrencyId,
                     SaleCurrencyId, PayChargeEffectId, UnitSalePrice, BaseSaleAmount,
                     TotalAdditions, TotalDeductions, NetSaleTotal,
                     GrossTotal, TotalRevenue, CostAmount, PayCharges, DeliveryFee, OtherAmount,
                     PromotionAmount, RtoChargeAmount, TransactionTypeId, PaidStatusId, ToAccountId,
                     InterProReference, OrderReference, SaleDate, Description,
                     InventoryDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)
                VALUES
                    (@TenantId, @ProductId, @SaleStatusId, NULLIF(LTRIM(RTRIM(@MarketplaceCompanyId)), N''),
                     NULLIF(LTRIM(RTRIM(@OrderId)), N''), @Quantity,
                     @UnitCost, @UnitCostCurrencyId,
                     @SaleCurrencyId, @PayChargeEffectId, @UnitSalePrice, @BaseSaleAmount,
                     @TotalAdditions, @TotalDeductions, @NetSaleTotal,
                     @BaseSaleAmount, @NetSaleTotal, 0, @PayCharges, @DeliveryFee, @OtherAmount,
                     @PromotionAmount, @RtoChargeAmount, @TransactionTypeId, @PaidStatusId, @ToAccountId,
                     NULLIF(LTRIM(RTRIM(@InterProReference)), N''), NULLIF(LTRIM(RTRIM(@OrderReference)), N''),
                     @SaleDate, NULLIF(LTRIM(RTRIM(@Description)), N''),
                     @InventoryDelta, @BalanceAfter, @UserId, SYSUTCDATETIME());
                SET @DailySaleId = SCOPE_IDENTITY();

                INSERT INTO dbo.SaleRoznamchaDailySaleCharges
                    (TenantId, DailySaleId, ChargeLabelId, Amount, CreatedOnUtc)
                SELECT @TenantId, @DailySaleId, ChargeLabelId, Amount, SYSUTCDATETIME()
                FROM @Charges;

                INSERT INTO dbo.SaleRoznamchaDailySaleTransactionDates
                    (TenantId, DailySaleId, DateLabelId, Value, CreatedOnUtc)
                SELECT @TenantId, @DailySaleId, DateLabelId, Value, SYSUTCDATETIME()
                FROM @TransactionDates;

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
                       company.Id CompanyId, company.Name CompanyName,
                       platform.Id PlatformId, platform.Name PlatformName,
                       productCategory.Id ProductCategoryId, productCategory.Name ProductCategoryName,
                       product.ProductCode, product.Name ProductName,
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
                INNER JOIN dbo.SaleRoznamchaSaleStatuses status
                    ON status.TenantId = sale.TenantId AND status.Id = sale.SaleStatusId
                WHERE sale.TenantId = @TenantId
                  AND (@FromDate IS NULL OR sale.CreatedOnUtc >= DATEADD(minute, -300, CONVERT(datetime2, @FromDate)))
                  AND (@ToDate IS NULL OR sale.CreatedOnUtc < DATEADD(minute, -300, DATEADD(day, 1, CONVERT(datetime2, @ToDate))))
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
            SELECT N'TREND_DAY', CONVERT(nvarchar(200), ProductId),
                   CONVERT(nvarchar(200), LEFT(CONCAT(ProductCode, N' - ', ProductName), 200)), CONVERT(datetime2, LocalSaleDate),
                   CONVERT(bigint, CONVERT(char(8), LocalSaleDate, 112)),
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), 1
            FROM Base GROUP BY LocalSaleDate, ProductId, ProductCode, ProductName

            UNION ALL
            SELECT N'TREND_MONTH', CONVERT(nvarchar(200), ProductId),
                   CONVERT(nvarchar(200), LEFT(CONCAT(ProductCode, N' - ', ProductName), 200)),
                   CONVERT(datetime2, DATEFROMPARTS(YEAR(LocalSaleDate), MONTH(LocalSaleDate), 1)),
                   CONVERT(bigint, YEAR(LocalSaleDate) * 100 + MONTH(LocalSaleDate)),
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), 1
            FROM Base GROUP BY YEAR(LocalSaleDate), MONTH(LocalSaleDate), ProductId, ProductCode, ProductName

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
            SELECT N'PRODUCT', CONVERT(nvarchar(200), ProductId), CONVERT(nvarchar(200), LEFT(CONCAT(ProductCode, N' - ', ProductName), 200)), NULL, ProductId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), 1
            FROM Base GROUP BY ProductId, ProductCode, ProductName

            UNION ALL
            SELECT N'STATUS', CONVERT(nvarchar(200), StatusId), StatusName, NULL, StatusId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY StatusId, StatusName

            ORDER BY Dimension, SortOrder, Quantity DESC;
        END;
        """;

    internal const string SettingsListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_SettingList
            @TenantId int,
            @SettingType nvarchar(30)
        AS
        BEGIN
            SET NOCOUNT ON;
            SET @SettingType = LOWER(LTRIM(RTRIM(@SettingType)));

            IF @SettingType = N'transaction-type'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name, CAST(0 AS int) SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.AccountsTransTypes
                ORDER BY Name;
                RETURN;
            END;

            IF @SettingType = N'charge-type'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name, SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaChargeLabels WHERE TenantId = @TenantId
                ORDER BY SortOrder, Name;
                RETURN;
            END;

            IF @SettingType = N'transaction-date'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name, SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaTransactionDateLabels WHERE TenantId = @TenantId
                ORDER BY SortOrder, Name;
                RETURN;
            END;

            IF @SettingType = N'paid-status'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name, CAST(0 AS int) SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaPaidStatuses WHERE TenantId = @TenantId
                ORDER BY Name;
                RETURN;
            END;

            IF @SettingType = N'status'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name, CAST(0 AS int) SortOrder,
                       InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaSaleStatuses WHERE TenantId = @TenantId
                ORDER BY Name;
                RETURN;
            END;

            THROW 51000, 'Unknown Sale Roznamcha setting type.', 1;
        END;
        """;

    internal const string SettingsSaveProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_SettingSave
            @TenantId int,
            @SettingType nvarchar(30),
            @Id int = NULL,
            @Code nvarchar(30),
            @Name nvarchar(100),
            @SortOrder int = 0,
            @InventoryEffect smallint = 0,
            @IsActive bit = 1
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            SET @SettingType = LOWER(LTRIM(RTRIM(@SettingType)));
            SET @Code = UPPER(LTRIM(RTRIM(@Code)));
            SET @Name = LTRIM(RTRIM(@Name));

            IF NULLIF(@Code, N'') IS NULL OR NULLIF(@Name, N'') IS NULL
                THROW 51000, 'Code and name are required.', 1;

            IF @SettingType = N'transaction-type'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.AccountsTransTypes WHERE Id = @Id)
                    THROW 51000, 'Transaction type was not found.', 1;
                IF EXISTS (
                    SELECT 1 FROM dbo.AccountsTransTypes
                    WHERE (Code = @Code OR Name = @Name)
                      AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A transaction type with this code or name already exists.', 1;

                IF @Id IS NULL
                BEGIN
                    INSERT dbo.AccountsTransTypes (Code, Name, IsActive)
                    VALUES (@Code, @Name, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE
                    UPDATE dbo.AccountsTransTypes SET Code = @Code, Name = @Name, IsActive = @IsActive
                    WHERE Id = @Id;

                SELECT Id, @SettingType SettingType, Code, Name, CAST(0 AS int) SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.AccountsTransTypes WHERE Id = @Id;
                RETURN;
            END;

            IF @SettingType = N'charge-type'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaChargeLabels WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Charge type was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaChargeLabels WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A charge type with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaChargeLabels (TenantId, Code, Name, SortOrder, IsActive)
                    VALUES (@TenantId, @Code, @Name, @SortOrder, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaChargeLabels SET Code = @Code, Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, SortOrder, CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaChargeLabels WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            IF @SettingType = N'transaction-date'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaTransactionDateLabels WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Transaction date label was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaTransactionDateLabels WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A transaction date label with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaTransactionDateLabels (TenantId, Code, Name, SortOrder, IsActive)
                    VALUES (@TenantId, @Code, @Name, @SortOrder, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaTransactionDateLabels SET Code = @Code, Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, SortOrder, CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaTransactionDateLabels WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            IF @SettingType = N'paid-status'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaPaidStatuses WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Paid status was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaPaidStatuses WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A paid status with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaPaidStatuses (TenantId, Code, Name, IsActive)
                    VALUES (@TenantId, @Code, @Name, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaPaidStatuses SET Code = @Code, Name = @Name, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, CAST(0 AS int) SortOrder, CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaPaidStatuses WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            IF @SettingType = N'status'
            BEGIN
                IF @InventoryEffect NOT IN (-1, 0)
                    THROW 51000, 'Inventory effect must be 0 or -1.', 1;
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaSaleStatuses WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Status was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaSaleStatuses WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A status with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaSaleStatuses (TenantId, Code, Name, InventoryEffect, IsActive, CreatedByUserId, CreatedOnUtc)
                    VALUES (@TenantId, @Code, @Name, @InventoryEffect, @IsActive, NULL, SYSUTCDATETIME());
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaSaleStatuses SET Code = @Code, Name = @Name, InventoryEffect = @InventoryEffect, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, CAST(0 AS int) SortOrder, InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaSaleStatuses WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            THROW 51000, 'Unknown Sale Roznamcha setting type.', 1;
        END;
        """;

    internal const string SettingsDeactivateProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_SettingDeactivate
            @TenantId int,
            @SettingType nvarchar(30),
            @Id int
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            SET @SettingType = LOWER(LTRIM(RTRIM(@SettingType)));

            IF @SettingType = N'transaction-type'
                UPDATE dbo.AccountsTransTypes SET IsActive = 0 WHERE Id = @Id;
            ELSE IF @SettingType = N'charge-type'
                UPDATE dbo.SaleRoznamchaChargeLabels SET IsActive = 0 WHERE Id = @Id AND TenantId = @TenantId;
            ELSE IF @SettingType = N'transaction-date'
                UPDATE dbo.SaleRoznamchaTransactionDateLabels SET IsActive = 0 WHERE Id = @Id AND TenantId = @TenantId;
            ELSE IF @SettingType = N'paid-status'
                UPDATE dbo.SaleRoznamchaPaidStatuses SET IsActive = 0 WHERE Id = @Id AND TenantId = @TenantId;
            ELSE IF @SettingType = N'status'
                UPDATE dbo.SaleRoznamchaSaleStatuses SET IsActive = 0 WHERE Id = @Id AND TenantId = @TenantId;
            ELSE
                THROW 51000, 'Unknown Sale Roznamcha setting type.', 1;

            IF @@ROWCOUNT = 0
                THROW 51000, 'Setting was not found or cannot be changed.', 1;
        END;
        """;

    internal const string SettingsListWithColorsProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_SettingList
            @TenantId int,
            @SettingType nvarchar(30)
        AS
        BEGIN
            SET NOCOUNT ON;
            SET @SettingType = LOWER(LTRIM(RTRIM(@SettingType)));

            IF @SettingType = N'transaction-type'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name,
                       CAST(NULL AS nvarchar(20)) ColorCode, CAST(0 AS int) SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.AccountsTransTypes
                ORDER BY Name;
                RETURN;
            END;

            IF @SettingType = N'charge-type'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name,
                       CAST(NULL AS nvarchar(20)) ColorCode, SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaChargeLabels WHERE TenantId = @TenantId
                ORDER BY SortOrder, Name;
                RETURN;
            END;

            IF @SettingType = N'transaction-date'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name,
                       CAST(NULL AS nvarchar(20)) ColorCode, SortOrder,
                       CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaTransactionDateLabels WHERE TenantId = @TenantId
                ORDER BY SortOrder, Name;
                RETURN;
            END;

            IF @SettingType = N'paid-status'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name, ColorCode,
                       CAST(0 AS int) SortOrder, CAST(0 AS smallint) InventoryEffect,
                       IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaPaidStatuses WHERE TenantId = @TenantId
                ORDER BY Name;
                RETURN;
            END;

            IF @SettingType = N'status'
            BEGIN
                SELECT Id, @SettingType SettingType, Code, Name, ColorCode,
                       CAST(0 AS int) SortOrder, InventoryEffect,
                       IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaSaleStatuses WHERE TenantId = @TenantId
                ORDER BY Name;
                RETURN;
            END;

            THROW 51000, 'Unknown Sale Roznamcha setting type.', 1;
        END;
        """;

    internal const string SettingsSaveWithColorsProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_SettingSave
            @TenantId int,
            @SettingType nvarchar(30),
            @Id int = NULL,
            @Code nvarchar(30),
            @Name nvarchar(100),
            @ColorCode nvarchar(20) = NULL,
            @SortOrder int = 0,
            @InventoryEffect smallint = 0,
            @IsActive bit = 1
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            SET @SettingType = LOWER(LTRIM(RTRIM(@SettingType)));
            SET @Code = UPPER(LTRIM(RTRIM(@Code)));
            SET @Name = LTRIM(RTRIM(@Name));
            SET @ColorCode = UPPER(LTRIM(RTRIM(@ColorCode)));

            IF NULLIF(@Code, N'') IS NULL OR NULLIF(@Name, N'') IS NULL
                THROW 51000, 'Code and name are required.', 1;
            IF @SettingType IN (N'paid-status', N'status')
               AND (LEN(COALESCE(@ColorCode, N'')) <> 7 OR LEFT(@ColorCode, 1) <> N'#'
                    OR SUBSTRING(@ColorCode, 2, 6) LIKE N'%[^0-9A-F]%')
                THROW 51000, 'A valid badge color is required.', 1;

            IF @SettingType = N'transaction-type'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.AccountsTransTypes WHERE Id = @Id)
                    THROW 51000, 'Transaction type was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.AccountsTransTypes WHERE
                           (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A transaction type with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.AccountsTransTypes (Code, Name, IsActive) VALUES (@Code, @Name, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.AccountsTransTypes SET Code = @Code, Name = @Name, IsActive = @IsActive WHERE Id = @Id;
                SELECT Id, @SettingType SettingType, Code, Name, CAST(NULL AS nvarchar(20)) ColorCode,
                       CAST(0 AS int) SortOrder, CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.AccountsTransTypes WHERE Id = @Id;
                RETURN;
            END;

            IF @SettingType = N'charge-type'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaChargeLabels WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Charge type was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaChargeLabels WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A charge type with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaChargeLabels (TenantId, Code, Name, SortOrder, IsActive) VALUES (@TenantId, @Code, @Name, @SortOrder, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaChargeLabels SET Code = @Code, Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, CAST(NULL AS nvarchar(20)) ColorCode,
                       SortOrder, CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaChargeLabels WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            IF @SettingType = N'transaction-date'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaTransactionDateLabels WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Transaction date label was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaTransactionDateLabels WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A transaction date label with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaTransactionDateLabels (TenantId, Code, Name, SortOrder, IsActive) VALUES (@TenantId, @Code, @Name, @SortOrder, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaTransactionDateLabels SET Code = @Code, Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, CAST(NULL AS nvarchar(20)) ColorCode,
                       SortOrder, CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaTransactionDateLabels WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            IF @SettingType = N'paid-status'
            BEGIN
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaPaidStatuses WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Paid status was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaPaidStatuses WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A paid status with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaPaidStatuses (TenantId, Code, Name, ColorCode, IsActive) VALUES (@TenantId, @Code, @Name, @ColorCode, @IsActive);
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaPaidStatuses SET Code = @Code, Name = @Name, ColorCode = @ColorCode, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, ColorCode,
                       CAST(0 AS int) SortOrder, CAST(0 AS smallint) InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaPaidStatuses WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            IF @SettingType = N'status'
            BEGIN
                IF @InventoryEffect NOT IN (-1, 0) THROW 51000, 'Inventory effect must be 0 or -1.', 1;
                IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaSaleStatuses WHERE Id = @Id AND TenantId = @TenantId)
                    THROW 51000, 'Status was not found.', 1;
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaSaleStatuses WHERE TenantId = @TenantId AND (Code = @Code OR Name = @Name) AND (@Id IS NULL OR Id <> @Id))
                    THROW 51000, 'A status with this code or name already exists.', 1;
                IF @Id IS NULL
                BEGIN
                    INSERT dbo.SaleRoznamchaSaleStatuses (TenantId, Code, Name, ColorCode, InventoryEffect, IsActive, CreatedByUserId, CreatedOnUtc)
                    VALUES (@TenantId, @Code, @Name, @ColorCode, @InventoryEffect, @IsActive, NULL, SYSUTCDATETIME());
                    SET @Id = SCOPE_IDENTITY();
                END
                ELSE UPDATE dbo.SaleRoznamchaSaleStatuses SET Code = @Code, Name = @Name, ColorCode = @ColorCode, InventoryEffect = @InventoryEffect, IsActive = @IsActive WHERE Id = @Id AND TenantId = @TenantId;
                SELECT Id, @SettingType SettingType, Code, Name, ColorCode,
                       CAST(0 AS int) SortOrder, InventoryEffect, IsActive, CAST(0 AS bit) IsPlatform
                FROM dbo.SaleRoznamchaSaleStatuses WHERE Id = @Id AND TenantId = @TenantId;
                RETURN;
            END;

            THROW 51000, 'Unknown Sale Roznamcha setting type.', 1;
        END;
        """;
}
