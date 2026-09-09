using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddDeductionAdjustmentMakerCheckerAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdjustmentApprovedByUserId",
                table: "AttendanceMonthlySettlements",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdjustmentApprovedDateUtc",
                table: "AttendanceMonthlySettlements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdjustmentSubmittedByUserId",
                table: "AttendanceMonthlySettlements",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdjustmentSubmittedDateUtc",
                table: "AttendanceMonthlySettlements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(@"
                DECLARE @ApprovalMenus TABLE (MenuId int PRIMARY KEY, Title nvarchar(100), ModuleName nvarchar(100));

                INSERT INTO @ApprovalMenus (MenuId, Title, ModuleName)
                SELECT Id, Title,
                    CASE WHEN Route = '/attendance/deduction' THEN 'Attendance' ELSE 'Pay & Allowances' END
                FROM dbo.Menus
                WHERE Route IN ('/attendance/deduction', '/pay-allowances/payroll');

                INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
                SELECT CONCAT('MENU_', menu.MenuId, '_APPROVE'),
                       CONCAT(menu.Title, ' Approve'),
                       menu.ModuleName,
                       'Allows higher-authority approval. This permission must be separate from edit access.',
                       SYSUTCDATETIME()
                FROM @ApprovalMenus menu
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.Features feature
                    WHERE feature.FeatureKey = CONCAT('MENU_', menu.MenuId, '_APPROVE'));

                INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
                SELECT menu.MenuId, feature.PermissionId
                FROM @ApprovalMenus menu
                INNER JOIN dbo.Features feature
                    ON feature.FeatureKey = CONCAT('MENU_', menu.MenuId, '_APPROVE')
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.MenuPermissions mapping
                    WHERE mapping.MenuId = menu.MenuId
                      AND mapping.PermissionId = feature.PermissionId);
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE mapping
                FROM dbo.MenuPermissions mapping
                INNER JOIN dbo.Menus menu ON menu.Id = mapping.MenuId
                INNER JOIN dbo.Features feature ON feature.PermissionId = mapping.PermissionId
                WHERE menu.Route IN ('/attendance/deduction', '/pay-allowances/payroll')
                  AND feature.FeatureKey = CONCAT('MENU_', menu.Id, '_APPROVE');

                ");

            migrationBuilder.DropColumn(
                name: "AdjustmentApprovedByUserId",
                table: "AttendanceMonthlySettlements");

            migrationBuilder.DropColumn(
                name: "AdjustmentApprovedDateUtc",
                table: "AttendanceMonthlySettlements");

            migrationBuilder.DropColumn(
                name: "AdjustmentSubmittedByUserId",
                table: "AttendanceMonthlySettlements");

            migrationBuilder.DropColumn(
                name: "AdjustmentSubmittedDateUtc",
                table: "AttendanceMonthlySettlements");
        }
    }
}
