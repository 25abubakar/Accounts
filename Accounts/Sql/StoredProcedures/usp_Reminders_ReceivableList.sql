-- Receivable reminder grid list. Keep in sync with the latest ReminderReceivableListRow projection.
CREATE OR ALTER PROCEDURE dbo.usp_Reminders_ReceivableList
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        r.Id,
        r.Ref,
        r.AccountTypeId,
        accountType.DisplayText AS AccountType,
        r.ReminderTypeId,
        reminderType.DisplayText AS ReminderType,
        r.InvoiceTypeId,
        r.CategoryId,
        category.Name AS Category,
        r.FromAccountId,
        fromAcct.AccountName AS FromAcctName,
        fromAcct.AccountNumber AS FromAcctNo,
        r.ToCategoryId,
        toCategory.Name AS ToCategory,
        r.ToAccountId,
        toAcct.AccountName AS ToAcctName,
        toAcct.AccountNumber AS ToAcctNo,
        r.CreatedOn,
        r.DueDate,
        r.RemindDay,
        r.RemindDate,
        r.ReceivedOn,
        r.LastPaidDate,
        r.PaidOn,
        r.Amount,
        r.FrequencyTypeId,
        freq.Name AS Frequency,
        COALESCE(invoiceType.DisplayText, r.InvoiceType) AS InvoiceType,
        r.TransTypeId,
        transType.Name AS TransactionType,
        r.Remarks,
        r.IsActive,
        r.AlertRoznamcha
    FROM dbo.ReminderReceivables r
    LEFT JOIN dbo.AppLookupValues accountType ON accountType.LookupValueId = r.AccountTypeId
    LEFT JOIN dbo.AppLookupValues reminderType ON reminderType.LookupValueId = r.ReminderTypeId
    LEFT JOIN dbo.AppLookupValues invoiceType ON invoiceType.LookupValueId = r.InvoiceTypeId
    LEFT JOIN dbo.AccountsCategories category ON category.Id = r.CategoryId AND category.TenantId = r.TenantId
    LEFT JOIN dbo.AccountsChartAccounts fromAcct ON fromAcct.Id = r.FromAccountId AND fromAcct.TenantId = r.TenantId
    LEFT JOIN dbo.AccountsCategories toCategory ON toCategory.Id = r.ToCategoryId AND toCategory.TenantId = r.TenantId
    LEFT JOIN dbo.AccountsChartAccounts toAcct ON toAcct.Id = r.ToAccountId AND toAcct.TenantId = r.TenantId
    LEFT JOIN PlatformTypes.FrequencyTypes freq ON freq.Id = r.FrequencyTypeId AND freq.TenantId = r.TenantId
    LEFT JOIN dbo.AccountsTransTypes transType ON transType.Id = r.TransTypeId AND (transType.TenantId IS NULL OR transType.TenantId = r.TenantId)
    WHERE r.TenantId = @TenantId
    ORDER BY r.Id DESC;
END
