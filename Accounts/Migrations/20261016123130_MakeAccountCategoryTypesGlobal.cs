using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class MakeAccountCategoryTypesGlobal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Category types are now shared application-wide. If an older database has
            // the same type in more than one tenant, preserve the lowest/original ID,
            // repoint tenant categories to it, then remove only the duplicate rows.
            migrationBuilder.Sql(
                """
                SELECT
                    Id AS OldId,
                    MIN(Id) OVER (PARTITION BY Name) AS KeepId
                INTO #AccountsCategoryTypeMap
                FROM dbo.AccountsCategoryTypes;

                UPDATE category
                SET category.CategoryTypeId = map.KeepId
                FROM dbo.AccountsCategories AS category
                INNER JOIN #AccountsCategoryTypeMap AS map
                    ON map.OldId = category.CategoryTypeId
                WHERE map.OldId <> map.KeepId;

                DELETE categoryType
                FROM dbo.AccountsCategoryTypes AS categoryType
                INNER JOIN #AccountsCategoryTypeMap AS map
                    ON map.OldId = categoryType.Id
                WHERE map.OldId <> map.KeepId;

                DROP TABLE #AccountsCategoryTypeMap;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_AccountsCategoryTypes_Tenants_TenantId",
                table: "AccountsCategoryTypes");

            migrationBuilder.DropIndex(
                name: "IX_AccountsCategoryTypes_TenantId_Name",
                table: "AccountsCategoryTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AccountsCategoryTypes");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsCategoryTypes_Name",
                table: "AccountsCategoryTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountsCategoryTypes_Name",
                table: "AccountsCategoryTypes");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AccountsCategoryTypes",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE categoryType
                SET categoryType.TenantId = tenant.Id
                FROM dbo.AccountsCategoryTypes AS categoryType
                CROSS JOIN (SELECT MIN(Id) AS Id FROM dbo.Tenants) AS tenant;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "AccountsCategoryTypes",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsCategoryTypes_TenantId_Name",
                table: "AccountsCategoryTypes",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsCategoryTypes_Tenants_TenantId",
                table: "AccountsCategoryTypes",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
