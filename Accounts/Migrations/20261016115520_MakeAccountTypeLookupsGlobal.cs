using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class MakeAccountTypeLookupsGlobal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Consolidate any tenant copies by code before enforcing one global row.
            // Every known dependent column is remapped to the canonical row first.
            migrationBuilder.Sql(
                """
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                SELECT Id OldId,
                       FIRST_VALUE(Id) OVER (
                           PARTITION BY Code
                           ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id) KeepId
                INTO #RozTypeMap
                FROM dbo.AccountsRoznamchaTypes;

                UPDATE settings SET PaymentRozTypeId = map.KeepId
                FROM dbo.AccountsModuleSettings settings JOIN #RozTypeMap map ON map.OldId = settings.PaymentRozTypeId;
                UPDATE settings SET ReceiptRozTypeId = map.KeepId
                FROM dbo.AccountsModuleSettings settings JOIN #RozTypeMap map ON map.OldId = settings.ReceiptRozTypeId;
                UPDATE entryRow SET RoznamchaTypeId = map.KeepId
                FROM dbo.RoznamchaEntries entryRow JOIN #RozTypeMap map ON map.OldId = entryRow.RoznamchaTypeId;
                DELETE sourceRow FROM dbo.AccountsRoznamchaTypes sourceRow
                JOIN #RozTypeMap map ON map.OldId = sourceRow.Id WHERE map.OldId <> map.KeepId;

                SELECT Id OldId,
                       FIRST_VALUE(Id) OVER (
                           PARTITION BY Code
                           ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id) KeepId
                INTO #TransTypeMap
                FROM dbo.AccountsTransTypes;

                UPDATE entryRow SET TransTypeId = map.KeepId
                FROM dbo.RoznamchaEntries entryRow JOIN #TransTypeMap map ON map.OldId = entryRow.TransTypeId;
                UPDATE rowData SET TransTypeId = map.KeepId
                FROM dbo.ReminderReceivables rowData JOIN #TransTypeMap map ON map.OldId = rowData.TransTypeId;
                UPDATE rowData SET TransTypeId = map.KeepId
                FROM dbo.ReminderPayables rowData JOIN #TransTypeMap map ON map.OldId = rowData.TransTypeId;
                UPDATE rowData SET TransTypeId = map.KeepId
                FROM dbo.EMarketingSalesRozEntries rowData JOIN #TransTypeMap map ON map.OldId = rowData.TransTypeId;
                UPDATE rowData SET TransactionTypeId = map.KeepId
                FROM dbo.SaleRoznamchaDailySales rowData JOIN #TransTypeMap map ON map.OldId = rowData.TransactionTypeId;
                UPDATE rowData SET TransactionTypeId = map.KeepId
                FROM dbo.SaleRoznamchaInvoices rowData JOIN #TransTypeMap map ON map.OldId = rowData.TransactionTypeId;
                DELETE sourceRow FROM dbo.AccountsTransTypes sourceRow
                JOIN #TransTypeMap map ON map.OldId = sourceRow.Id WHERE map.OldId <> map.KeepId;

                SELECT Id OldId,
                       FIRST_VALUE(Id) OVER (
                           PARTITION BY Code
                           ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id) KeepId
                INTO #TransModeMap
                FROM dbo.AccountsTransModes;

                UPDATE entryRow SET TransModeId = map.KeepId
                FROM dbo.RoznamchaEntries entryRow JOIN #TransModeMap map ON map.OldId = entryRow.TransModeId;
                DELETE sourceRow FROM dbo.AccountsTransModes sourceRow
                JOIN #TransModeMap map ON map.OldId = sourceRow.Id WHERE map.OldId <> map.KeepId;

                SELECT Id OldId,
                       FIRST_VALUE(Id) OVER (
                           PARTITION BY Code
                           ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id) KeepId
                INTO #CurrencyMap
                FROM dbo.AccountsCurrencies;

                UPDATE settings SET DefaultCurrencyId = map.KeepId
                FROM dbo.AccountsModuleSettings settings JOIN #CurrencyMap map ON map.OldId = settings.DefaultCurrencyId;
                UPDATE entryRow SET CurrencyId = map.KeepId
                FROM dbo.RoznamchaEntries entryRow JOIN #CurrencyMap map ON map.OldId = entryRow.CurrencyId;
                UPDATE rowData SET CurrencyId = map.KeepId
                FROM dbo.ReminderReceivables rowData JOIN #CurrencyMap map ON map.OldId = rowData.CurrencyId;
                UPDATE rowData SET CurrencyId = map.KeepId
                FROM dbo.ReminderPayables rowData JOIN #CurrencyMap map ON map.OldId = rowData.CurrencyId;
                UPDATE rowData SET CurrencyId = map.KeepId
                FROM dbo.PaymentRoznamcha rowData JOIN #CurrencyMap map ON map.OldId = rowData.CurrencyId;
                UPDATE rowData SET CurrencyId = map.KeepId
                FROM dbo.SaleRoznamchaProducts rowData JOIN #CurrencyMap map ON map.OldId = rowData.CurrencyId;
                UPDATE rowData SET SaleCurrencyId = map.KeepId
                FROM dbo.SaleRoznamchaDailySales rowData JOIN #CurrencyMap map ON map.OldId = rowData.SaleCurrencyId;
                UPDATE rowData SET UnitCostCurrencyId = map.KeepId
                FROM dbo.SaleRoznamchaDailySales rowData JOIN #CurrencyMap map ON map.OldId = rowData.UnitCostCurrencyId;
                UPDATE rowData SET CurrencyId = map.KeepId
                FROM dbo.SaleRoznamchaDailySaleStockAllocations rowData JOIN #CurrencyMap map ON map.OldId = rowData.CurrencyId;
                UPDATE rowData SET SaleCurrencyId = map.KeepId
                FROM dbo.SaleRoznamchaInvoices rowData JOIN #CurrencyMap map ON map.OldId = rowData.SaleCurrencyId;
                DELETE sourceRow FROM dbo.AccountsCurrencies sourceRow
                JOIN #CurrencyMap map ON map.OldId = sourceRow.Id WHERE map.OldId <> map.KeepId;

                ;WITH duplicateDates AS
                (
                    SELECT Id,
                           FIRST_VALUE(Id) OVER (
                               PARTITION BY Code
                               ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id) KeepId
                    FROM dbo.AccountsDateLabels
                )
                DELETE sourceRow FROM dbo.AccountsDateLabels sourceRow
                JOIN duplicateDates map ON map.Id = sourceRow.Id WHERE map.Id <> map.KeepId;
                """);

            migrationBuilder.Sql(
                """
                DECLARE @LookupTables TABLE (TableName sysname);
                INSERT @LookupTables VALUES
                    (N'AccountsCurrencies'), (N'AccountsDateLabels'),
                    (N'AccountsRoznamchaTypes'), (N'AccountsTransModes'),
                    (N'AccountsTransTypes');

                DECLARE @TableName sysname, @ConstraintName sysname, @Sql nvarchar(max);
                DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @LookupTables;
                OPEN table_cursor;
                FETCH NEXT FROM table_cursor INTO @TableName;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    DECLARE constraint_cursor CURSOR LOCAL FAST_FORWARD FOR
                        SELECT fk.name
                        FROM sys.foreign_keys fk
                        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.' + @TableName)
                          AND EXISTS
                          (
                              SELECT 1 FROM sys.foreign_key_columns fkc
                              JOIN sys.columns columnRow
                                ON columnRow.object_id = fkc.parent_object_id
                               AND columnRow.column_id = fkc.parent_column_id
                              WHERE fkc.constraint_object_id = fk.object_id
                                AND columnRow.name = N'TenantId'
                          );
                    OPEN constraint_cursor;
                    FETCH NEXT FROM constraint_cursor INTO @ConstraintName;
                    WHILE @@FETCH_STATUS = 0
                    BEGIN
                        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName)
                                 + N' DROP CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
                        EXEC sys.sp_executesql @Sql;
                        FETCH NEXT FROM constraint_cursor INTO @ConstraintName;
                    END;
                    CLOSE constraint_cursor;
                    DEALLOCATE constraint_cursor;

                    SELECT @ConstraintName = i.name
                    FROM sys.indexes i
                    WHERE i.object_id = OBJECT_ID(N'dbo.' + @TableName)
                      AND i.name IN (N'IX_' + @TableName + N'_Tenant_Code',
                                     N'IX_' + @TableName + N'_TenantId_Code');
                    IF @ConstraintName IS NOT NULL
                    BEGIN
                        SET @Sql = N'DROP INDEX ' + QUOTENAME(@ConstraintName)
                                 + N' ON dbo.' + QUOTENAME(@TableName) + N';';
                        EXEC sys.sp_executesql @Sql;
                    END;
                    SET @ConstraintName = NULL;
                    FETCH NEXT FROM table_cursor INTO @TableName;
                END;
                CLOSE table_cursor;
                DEALLOCATE table_cursor;
                """);

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AccountsTransTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AccountsTransModes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AccountsRoznamchaTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AccountsDateLabels");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AccountsCurrencies");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsTransTypes_Code",
                table: "AccountsTransTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsTransModes_Code",
                table: "AccountsTransModes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsRoznamchaTypes_Code",
                table: "AccountsRoznamchaTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsDateLabels_Code",
                table: "AccountsDateLabels",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsCurrencies_Code",
                table: "AccountsCurrencies",
                column: "Code",
                unique: true);

            // Recreate database modules that joined these lookups by TenantId.
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Migrations.RoznamchaListSql.Payment));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Migrations.RoznamchaListSql.Receipt));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Migrations.RoznamchaListSql.Unified));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryListProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.PostInventoryPurchasePaymentProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseDetailsView));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.DailySaleListProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.DailySaleCreateProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.SettingsListWithColorsProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.SettingsSaveWithColorsProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.SettingsDeactivateProcedure));

            migrationBuilder.Sql(
                """
                DECLARE @ProcedureName sysname, @Definition nvarchar(max);
                DECLARE reminder_cursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT procedureName
                    FROM (VALUES (N'dbo.usp_Reminders_ReceivableList'),
                                 (N'dbo.usp_Reminders_PayableList')) procedures(procedureName);
                OPEN reminder_cursor;
                FETCH NEXT FROM reminder_cursor INTO @ProcedureName;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @Definition = OBJECT_DEFINITION(OBJECT_ID(@ProcedureName));
                    IF @Definition IS NOT NULL
                    BEGIN
                        SET @Definition = STUFF(@Definition, 1,
                            CHARINDEX(N'PROCEDURE', UPPER(@Definition)) - 1, N'ALTER ');
                        SET @Definition = REPLACE(@Definition,
                            N' AND (transType.TenantId IS NULL OR transType.TenantId = r.TenantId)', N'');
                        EXEC sys.sp_executesql @Definition;
                    END;
                    FETCH NEXT FROM reminder_cursor INTO @ProcedureName;
                END;
                CLOSE reminder_cursor;
                DEALLOCATE reminder_cursor;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountsTransTypes_Code",
                table: "AccountsTransTypes");

            migrationBuilder.DropIndex(
                name: "IX_AccountsTransModes_Code",
                table: "AccountsTransModes");

            migrationBuilder.DropIndex(
                name: "IX_AccountsRoznamchaTypes_Code",
                table: "AccountsRoznamchaTypes");

            migrationBuilder.DropIndex(
                name: "IX_AccountsDateLabels_Code",
                table: "AccountsDateLabels");

            migrationBuilder.DropIndex(
                name: "IX_AccountsCurrencies_Code",
                table: "AccountsCurrencies");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AccountsTransTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AccountsTransModes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AccountsRoznamchaTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AccountsDateLabels",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AccountsCurrencies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsTransTypes_TenantId_Code",
                table: "AccountsTransTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsTransModes_TenantId_Code",
                table: "AccountsTransModes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsRoznamchaTypes_TenantId_Code",
                table: "AccountsRoznamchaTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsDateLabels_TenantId_Code",
                table: "AccountsDateLabels",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsCurrencies_TenantId_Code",
                table: "AccountsCurrencies",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsCurrencies_Tenants_TenantId",
                table: "AccountsCurrencies",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsDateLabels_Tenants_TenantId",
                table: "AccountsDateLabels",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsRoznamchaTypes_Tenants_TenantId",
                table: "AccountsRoznamchaTypes",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsTransModes_Tenants_TenantId",
                table: "AccountsTransModes",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsTransTypes_Tenants_TenantId",
                table: "AccountsTransTypes",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        private static string ExecutableBatch(string sql) =>
            $"EXEC(N'{sql.Replace("'", "''", StringComparison.Ordinal)}');";
    }
}
