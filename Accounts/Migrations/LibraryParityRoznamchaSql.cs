namespace Accounts.Migrations;

internal static class RoznamchaListSql
{
    internal static readonly string ConfiguredPayment = WithConfiguredStatuses(Payment);
    internal static readonly string ConfiguredReceipt = WithConfiguredStatuses(Receipt);
    internal static readonly string ConfiguredUnified = WithConfiguredStatuses(Unified);
    // New deployments must keep the two ledger sides separate even when
    // AccountsModuleSettings has no row or its optional type is unset.
    internal static readonly string TypedPayment = WithTransferMetadata(ConfiguredPayment).Replace(
        "AND (@PaymentRozTypeId IS NULL OR e.RoznamchaTypeId=@PaymentRozTypeId)",
        "AND (@PaymentRozTypeId IS NULL OR e.RoznamchaTypeId=@PaymentRozTypeId) AND UPPER(rt.Code)=N'PAYMENT'", StringComparison.Ordinal);
    internal static readonly string TypedReceipt = WithTransferMetadata(ConfiguredReceipt).Replace(
        "AND (@ReceiptRozTypeId IS NULL OR e.RoznamchaTypeId=@ReceiptRozTypeId)",
        "AND (@ReceiptRozTypeId IS NULL OR e.RoznamchaTypeId=@ReceiptRozTypeId) AND UPPER(rt.Code)=N'RECEIPT'", StringComparison.Ordinal);
    internal static readonly string TransferUnified = WithTransferMetadata(ConfiguredUnified);

    private static string WithTransferMetadata(string sql) => sql
        .Replace("SELECT e.Id, e.SNo", "SELECT e.Id, e.InternalTransferGroupId, e.SNo", StringComparison.Ordinal)
        .Replace("AND (@BillingCategoryId IS NULL OR e.CategoryId=@BillingCategoryId",
            "AND (@BillingCategoryId IS NULL OR e.CategoryId=@BillingCategoryId OR e.InternalTransferGroupId IS NOT NULL", StringComparison.Ordinal);

    private static string WithConfiguredStatuses(string sql) => sql
        .Replace("st.Name AS StatusName,", "st.Name AS StatusName, e.EnterStatusId AS StatusId, statusMap.Id AS ProcessStatusId, statusColor.ColorCode AS StatusColorCode, statusColor.FontColor AS StatusFontColor,", StringComparison.Ordinal)
        .Replace("LEFT JOIN dbo.AccountsEntryStatuses st ON st.Id=e.EnterStatusId AND (st.TenantId IS NULL OR st.TenantId=e.TenantId)",
            """
            LEFT JOIN PlatformSettings.Statuses st ON st.Id=e.EnterStatusId
            LEFT JOIN PlatformSettings.Processes statusProcess
              ON statusProcess.Name=CASE UPPER(rt.Code) WHEN N'PAYMENT' THEN N'Payment' WHEN N'RECEIPT' THEN N'Receipt' ELSE rt.Name END
            LEFT JOIN PlatformSettings.ProcessStatuses statusMap ON statusMap.ProcessId=statusProcess.Id AND statusMap.StatusId=e.EnterStatusId
            LEFT JOIN PlatformSettings.Colors statusColor ON statusColor.Id=statusMap.ColorId
            """, StringComparison.Ordinal);

    internal const string Payment =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Accounts_PaymentRoz_List
            @TenantId INT, @DateFrom DATE, @DateTo DATE
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @BillingCategoryId INT=NULL, @PaymentRozTypeId INT=NULL;
            SELECT @BillingCategoryId=BillingCategoryId, @PaymentRozTypeId=PaymentRozTypeId
            FROM dbo.AccountsModuleSettings WHERE TenantId=@TenantId;
            SELECT e.Id, e.SNo, e.Ref, e.OldRef, rt.Name AS TypeName,
                p.Name AS Project, e.ProjectId, toMap.ToCategoryId,
                fromCat.Name AS CategoryName, fromAcct.AccountName AS AccountName, e.ToAccountId,
                fromAcct.AccountNumber AS FromAccountNumber, toMap.ToCategoryName,
                toMap.ToAccountName, toMap.ToAccountNumber,
                tt.Name AS TransTypeName, tm.Name AS TransModeName, e.CreatedOnUtc AS CreatedDate,
                e.BankRef, e.InstrumentNo, e.Descriptions, e.TransDate, e.Adjustment,
                e.Amount AS Debit, e.UsdAmount AS UsdDebit, e.Qty, e.Rate, st.Name AS StatusName,
                e.IsDeleted, e.IsApproved, e.IsLocked, e.Attachment, e.ImagePath AS [Image],
                COALESCE(linked.LibRef,e.LibRef) AS LibRef, e.Remarks,
                CAST(NULL AS DATETIME2) AS ProcessDate
            FROM dbo.RoznamchaEntries e
            LEFT JOIN dbo.AccountsRoznamchaTypes rt ON rt.Id=e.RoznamchaTypeId
            LEFT JOIN dbo.AccountsProjects p ON p.Id=e.ProjectId AND p.TenantId=e.TenantId
            LEFT JOIN dbo.vw_AccountsChartAccountHierarchy fromAcct ON fromAcct.Id=e.FromAccountId AND fromAcct.TenantId=e.TenantId
            LEFT JOIN dbo.AccountsCategories fromCat ON fromCat.Id=fromAcct.CategoryId AND fromCat.TenantId=e.TenantId
            LEFT JOIN dbo.vw_RoznamchaToAccountCategory toMap ON toMap.ToAccountId=e.ToAccountId AND toMap.TenantId=e.TenantId
            LEFT JOIN dbo.AccountsTransTypes tt ON tt.Id=e.TransTypeId
            LEFT JOIN dbo.AccountsTransModes tm ON tm.Id=e.TransModeId
            LEFT JOIN dbo.AccountsEntryStatuses st ON st.Id=e.EnterStatusId AND (st.TenantId IS NULL OR st.TenantId=e.TenantId)
            OUTER APPLY (
                SELECT STRING_AGG(CAST(COALESCE(d.DocumentReference,CONCAT(N'Doc-',d.Id)) AS NVARCHAR(MAX)),N', ')
                    WITHIN GROUP (ORDER BY d.Id) AS LibRef
                FROM dbo.RoznamchaLibraryDocuments l
                JOIN dbo.LibraryDocuments d ON d.Id=l.LibraryDocumentId AND d.TenantId=l.TenantId
                WHERE l.TenantId=e.TenantId AND l.RoznamchaEntryId=e.Id
            ) linked
            WHERE e.TenantId=@TenantId AND e.IsDeleted=0 AND e.IsShow=1
              AND e.TransDate>=@DateFrom AND e.TransDate<=@DateTo
              AND (@BillingCategoryId IS NULL OR e.CategoryId=@BillingCategoryId
                  OR EXISTS (
                    SELECT 1 FROM dbo.PaymentRoznamcha receipt
                    WHERE receipt.TenantId=e.TenantId AND receipt.RoznamchaEntryId=e.Id
                  ))
              AND (@PaymentRozTypeId IS NULL OR e.RoznamchaTypeId=@PaymentRozTypeId)
            ORDER BY e.TransDate,e.Id;
        END
        """;

    internal const string Receipt =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Accounts_ReceiptRoz_List
            @TenantId INT, @DateFrom DATE, @DateTo DATE
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @BillingCategoryId INT=NULL, @ReceiptRozTypeId INT=NULL;
            SELECT @BillingCategoryId=BillingCategoryId, @ReceiptRozTypeId=ReceiptRozTypeId
            FROM dbo.AccountsModuleSettings WHERE TenantId=@TenantId;
            SELECT e.Id, e.SNo, e.Ref, e.OldRef, rt.Name AS TypeName,
                p.Name AS Project, e.ProjectId, toMap.ToCategoryId,
                fromCat.Name AS CategoryName, fromAcct.AccountName AS AccountName, e.ToAccountId,
                fromAcct.AccountNumber AS FromAccountNumber, toMap.ToCategoryName,
                toMap.ToAccountName, toMap.ToAccountNumber,
                tt.Name AS TransTypeName, tm.Name AS TransModeName, e.CreatedOnUtc AS CreatedDate,
                e.BankRef, e.InstrumentNo, e.Descriptions, e.TransDate, e.Adjustment,
                e.Amount AS Debit, e.UsdAmount AS UsdDebit, e.Qty, e.Rate, st.Name AS StatusName,
                e.IsDeleted, e.IsApproved, e.IsLocked, e.Attachment, e.ImagePath AS [Image],
                COALESCE(linked.LibRef,e.LibRef) AS LibRef, e.Remarks,
                CAST(NULL AS DATETIME2) AS ProcessDate
            FROM dbo.RoznamchaEntries e
            LEFT JOIN dbo.AccountsRoznamchaTypes rt ON rt.Id=e.RoznamchaTypeId
            LEFT JOIN dbo.AccountsProjects p ON p.Id=e.ProjectId AND p.TenantId=e.TenantId
            LEFT JOIN dbo.vw_AccountsChartAccountHierarchy fromAcct ON fromAcct.Id=e.FromAccountId AND fromAcct.TenantId=e.TenantId
            LEFT JOIN dbo.AccountsCategories fromCat ON fromCat.Id=fromAcct.CategoryId AND fromCat.TenantId=e.TenantId
            LEFT JOIN dbo.vw_RoznamchaToAccountCategory toMap ON toMap.ToAccountId=e.ToAccountId AND toMap.TenantId=e.TenantId
            LEFT JOIN dbo.AccountsTransTypes tt ON tt.Id=e.TransTypeId
            LEFT JOIN dbo.AccountsTransModes tm ON tm.Id=e.TransModeId
            LEFT JOIN dbo.AccountsEntryStatuses st ON st.Id=e.EnterStatusId AND (st.TenantId IS NULL OR st.TenantId=e.TenantId)
            OUTER APPLY (
                SELECT STRING_AGG(CAST(COALESCE(d.DocumentReference,CONCAT(N'Doc-',d.Id)) AS NVARCHAR(MAX)),N', ')
                    WITHIN GROUP (ORDER BY d.Id) AS LibRef
                FROM dbo.RoznamchaLibraryDocuments l
                JOIN dbo.LibraryDocuments d ON d.Id=l.LibraryDocumentId AND d.TenantId=l.TenantId
                WHERE l.TenantId=e.TenantId AND l.RoznamchaEntryId=e.Id
            ) linked
            WHERE e.TenantId=@TenantId AND e.IsDeleted=0 AND e.IsShow=1
              AND e.TransDate>=@DateFrom AND e.TransDate<=@DateTo
              AND (@BillingCategoryId IS NULL OR e.CategoryId=@BillingCategoryId)
              AND (@ReceiptRozTypeId IS NULL OR e.RoznamchaTypeId=@ReceiptRozTypeId)
              AND (rt.Code IS NULL OR UPPER(rt.Code) <> N'PAYMENT')
            ORDER BY e.TransDate,e.Id;
        END
        """;

    internal static readonly string LegacyPayment = Payment
        .Replace("COALESCE(linked.LibRef,e.LibRef) AS LibRef", "e.LibRef", StringComparison.Ordinal)
        .Replace(
            """
            OUTER APPLY (
                SELECT STRING_AGG(CAST(COALESCE(d.DocumentReference,CONCAT(N'Doc-',d.Id)) AS NVARCHAR(MAX)),N', ')
                    WITHIN GROUP (ORDER BY d.Id) AS LibRef
                FROM dbo.RoznamchaLibraryDocuments l
                JOIN dbo.LibraryDocuments d ON d.Id=l.LibraryDocumentId AND d.TenantId=l.TenantId
                WHERE l.TenantId=e.TenantId AND l.RoznamchaEntryId=e.Id
            ) linked
            """, string.Empty, StringComparison.Ordinal);

    internal static readonly string LegacyReceipt = LegacyPayment
        .Replace("usp_Accounts_PaymentRoz_List", "usp_Accounts_ReceiptRoz_List", StringComparison.Ordinal)
        .Replace("PaymentRozTypeId", "ReceiptRozTypeId", StringComparison.Ordinal)
        .Replace(
            """
                  OR EXISTS (
                    SELECT 1 FROM dbo.PaymentRoznamcha receipt
                    WHERE receipt.TenantId=e.TenantId AND receipt.RoznamchaEntryId=e.Id
                  )
            """,
            string.Empty,
            StringComparison.Ordinal);

    internal const string Unified =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Accounts_Roznamcha_List
            @TenantId INT, @RozTypeId INT=NULL, @DateFrom DATE, @DateTo DATE
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @BillingCategoryId INT=NULL;
            SELECT @BillingCategoryId=BillingCategoryId FROM dbo.AccountsModuleSettings WHERE TenantId=@TenantId;
            SELECT e.Id, e.SNo, e.Ref, e.OldRef, rt.Name AS TypeName, p.Name AS Project,
                e.ProjectId, toMap.ToCategoryId, fromCat.Name AS CategoryName,
                fromAcct.AccountName AS AccountName, e.ToAccountId,
                fromAcct.AccountNumber AS FromAccountNumber, toMap.ToCategoryName,
                toMap.ToAccountName, toMap.ToAccountNumber,
                tt.Name AS TransTypeName, tm.Name AS TransModeName, e.CreatedOnUtc AS CreatedDate,
                e.BankRef, e.InstrumentNo, e.Descriptions, e.TransDate, e.Adjustment,
                e.Amount AS Debit, e.UsdAmount AS UsdDebit, e.Qty, e.Rate, st.Name AS StatusName,
                e.IsDeleted, e.IsApproved, e.IsLocked, e.IsSettled, e.Attachment,
                e.ImagePath AS [Image], COALESCE(linked.LibRef,e.LibRef) AS LibRef,
                e.Remarks, procLog.ProcessDate
            FROM dbo.RoznamchaEntries e
            LEFT JOIN dbo.AccountsRoznamchaTypes rt ON rt.Id=e.RoznamchaTypeId
            LEFT JOIN dbo.AccountsProjects p ON p.Id=e.ProjectId AND p.TenantId=e.TenantId
            LEFT JOIN dbo.vw_AccountsChartAccountHierarchy fromAcct ON fromAcct.Id=e.FromAccountId AND fromAcct.TenantId=e.TenantId
            LEFT JOIN dbo.AccountsCategories fromCat ON fromCat.Id=fromAcct.CategoryId AND fromCat.TenantId=e.TenantId
            LEFT JOIN dbo.vw_RoznamchaToAccountCategory toMap ON toMap.ToAccountId=e.ToAccountId AND toMap.TenantId=e.TenantId
            LEFT JOIN dbo.AccountsTransTypes tt ON tt.Id=e.TransTypeId
            LEFT JOIN dbo.AccountsTransModes tm ON tm.Id=e.TransModeId
            LEFT JOIN dbo.AccountsEntryStatuses st ON st.Id=e.EnterStatusId AND (st.TenantId IS NULL OR st.TenantId=e.TenantId)
            OUTER APPLY (
                SELECT TOP(1) l.ProcessedOnUtc AS ProcessDate
                FROM dbo.RoznamchaEntryProcessLogs l
                WHERE l.TenantId=e.TenantId AND l.RoznamchaEntryId=e.Id
                ORDER BY l.ProcessedOnUtc DESC,l.Id DESC
            ) procLog
            OUTER APPLY (
                SELECT STRING_AGG(CAST(COALESCE(d.DocumentReference,CONCAT(N'Doc-',d.Id)) AS NVARCHAR(MAX)),N', ')
                    WITHIN GROUP (ORDER BY d.Id) AS LibRef
                FROM dbo.RoznamchaLibraryDocuments l
                JOIN dbo.LibraryDocuments d ON d.Id=l.LibraryDocumentId AND d.TenantId=l.TenantId
                WHERE l.TenantId=e.TenantId AND l.RoznamchaEntryId=e.Id
            ) linked
            WHERE e.TenantId=@TenantId AND e.IsDeleted=0 AND e.IsShow=1
              AND e.TransDate>=@DateFrom AND e.TransDate<=@DateTo
              AND (@BillingCategoryId IS NULL OR e.CategoryId=@BillingCategoryId
                  OR EXISTS (
                    SELECT 1 FROM dbo.PaymentRoznamcha receipt
                    WHERE receipt.TenantId=e.TenantId AND receipt.RoznamchaEntryId=e.Id
                  ))
              AND (@RozTypeId IS NULL OR e.RoznamchaTypeId=@RozTypeId)
            ORDER BY e.TransDate DESC,e.Id DESC;
        END
        """;

    internal static readonly string LegacyUnified = Unified
        .Replace("COALESCE(linked.LibRef,e.LibRef) AS LibRef", "e.LibRef", StringComparison.Ordinal)
        .Replace(
            """
            OUTER APPLY (
                SELECT STRING_AGG(CAST(COALESCE(d.DocumentReference,CONCAT(N'Doc-',d.Id)) AS NVARCHAR(MAX)),N', ')
                    WITHIN GROUP (ORDER BY d.Id) AS LibRef
                FROM dbo.RoznamchaLibraryDocuments l
                JOIN dbo.LibraryDocuments d ON d.Id=l.LibraryDocumentId AND d.TenantId=l.TenantId
                WHERE l.TenantId=e.TenantId AND l.RoznamchaEntryId=e.Id
            ) linked
            """, string.Empty, StringComparison.Ordinal);
}
