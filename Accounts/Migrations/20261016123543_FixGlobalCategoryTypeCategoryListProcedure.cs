using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class FixGlobalCategoryTypeCategoryListProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CreateTenantScopedCategoryListProcedure(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The category type table remains global until the preceding migration is
            // rolled back, so retain the valid procedure shape at this boundary.
            CreateTenantScopedCategoryListProcedure(migrationBuilder);
        }

        private static void CreateTenantScopedCategoryListProcedure(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Accounts_CategoryList
                    @TenantId INT
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT
                        c.Id,
                        c.CategoryTypeId,
                        t.Name AS CategoryType,
                        c.ReferenceNumber AS ReferenceNo,
                        c.Name,
                        c.Number,
                        c.Code,
                        c.BudgetAmount,
                        c.UsedAmount,
                        c.BalanceAmount,
                        c.IsNotInReport,
                        c.IsActive
                    FROM dbo.AccountsCategories c
                    LEFT JOIN dbo.AccountsCategoryTypes t
                        ON t.Id = c.CategoryTypeId
                    WHERE c.TenantId = @TenantId
                    ORDER BY c.Name;
                END
                """);
        }
    }
}
