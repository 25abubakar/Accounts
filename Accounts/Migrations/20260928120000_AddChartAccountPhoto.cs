using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>Adds the separate account photo path and keeps the chart-account list procedure in sync.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928120000_AddChartAccountPhoto")]
public sealed class AddChartAccountPhoto : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Photo",
            table: "AccountsChartAccounts",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        CreateListProcedure(migrationBuilder, includePhoto: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        CreateListProcedure(migrationBuilder, includePhoto: false);
        migrationBuilder.DropColumn(name: "Photo", table: "AccountsChartAccounts");
    }

    private static void CreateListProcedure(MigrationBuilder migrationBuilder, bool includePhoto)
    {
        var photoColumn = includePhoto ? "                    a.Photo,\n" : string.Empty;
        migrationBuilder.Sql(
            $$"""
            CREATE OR ALTER PROCEDURE dbo.usp_Accounts_ChartAccountList
                @TenantId INT,
                @CategoryId INT = NULL,
                @ParentId INT = NULL,
                @ActiveOnly BIT = 0,
                @MainOnly BIT = 0
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    a.Id,
                    a.CategoryId,
                    cat.Name AS CategoryName,
                    a.ParentId,
                    a.AccountName,
                    a.AccountNumber AS AccountNo,
                    a.AccountCode,
                    a.AccountReference,
                    a.BankAccountNumber AS BankAccountNo,
                    a.CnicNtn,
                    a.Address,
                    a.FullName,
                    a.Email,
                    a.Phone,
                    a.DesignationId,
                    a.PersonId,
                    a.Description,
            {{photoColumn}}        a.Attachment,
                    a.BudgetAmount,
                    a.UsedAmount,
                    a.BalanceAmount,
                    a.AccountLimit,
                    a.Credit,
                    a.Debit,
                    a.StatusId,
                    a.IsStatement,
                    a.IsInventory,
                    a.IsStaff,
                    a.IsActive
                FROM dbo.AccountsChartAccounts a
                LEFT JOIN dbo.AccountsCategories cat
                    ON cat.Id = a.CategoryId
                   AND cat.TenantId = a.TenantId
                WHERE a.TenantId = @TenantId
                  AND (@CategoryId IS NULL OR a.CategoryId = @CategoryId)
                  AND (@MainOnly = 0 OR a.ParentId IS NULL)
                  AND (@ParentId IS NULL OR a.ParentId = @ParentId)
                  AND (@ActiveOnly = 0 OR a.IsActive = 1)
                ORDER BY a.AccountName;
            END
            """);
    }
}
