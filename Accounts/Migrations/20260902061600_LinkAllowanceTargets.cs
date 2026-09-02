using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class LinkAllowanceTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DesignationId",
                table: "PayScaleAllowances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftLookupValueId",
                table: "PayScaleAllowances",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [PlatformTypes].[AllowanceTypes]
                   SET [AllowanceCategory] = N'SHIFT'
                 WHERE [AllowanceCategory] = N'NIGHT';

                UPDATE [dbo].[PayScaleAllowances]
                   SET [AllowanceCategory] = N'SHIFT'
                 WHERE [AllowanceCategory] = N'NIGHT';

                UPDATE allowance
                   SET allowance.[DesignationId] = designation.[Id]
                  FROM [dbo].[PayScaleAllowances] allowance
                  JOIN [dbo].[JobTitles] designation
                    ON designation.[TenantId] = allowance.[TenantId]
                   AND designation.[TitleName] = allowance.[Name]
                 WHERE allowance.[AllowanceCategory] = N'APPT'
                   AND allowance.[DesignationId] IS NULL;

                UPDATE allowance
                   SET allowance.[ShiftLookupValueId] = lookupValue.[LookupValueId]
                  FROM [dbo].[PayScaleAllowances] allowance
                  JOIN [dbo].[AppLookupValues] lookupValue
                    ON lookupValue.[DisplayText] = allowance.[Name]
                    OR lookupValue.[ValueCode] = allowance.[Name]
                  JOIN [dbo].[AppLookupTypes] lookupType
                    ON lookupType.[LookupTypeId] = lookupValue.[LookupTypeId]
                   AND lookupType.[LookupTypeCode] = N'ATTENDANCE_SHIFT'
                 WHERE allowance.[AllowanceCategory] = N'SHIFT'
                   AND allowance.[ShiftLookupValueId] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_DesignationId",
                table: "PayScaleAllowances",
                column: "DesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_ShiftLookupValueId",
                table: "PayScaleAllowances",
                column: "ShiftLookupValueId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_TenantId_DesignationId",
                table: "PayScaleAllowances",
                columns: new[] { "TenantId", "DesignationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_TenantId_ShiftLookupValueId",
                table: "PayScaleAllowances",
                columns: new[] { "TenantId", "ShiftLookupValueId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PayScaleAllowances_AppLookupValues_ShiftLookupValueId",
                table: "PayScaleAllowances",
                column: "ShiftLookupValueId",
                principalTable: "AppLookupValues",
                principalColumn: "LookupValueId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayScaleAllowances_JobTitles_DesignationId",
                table: "PayScaleAllowances",
                column: "DesignationId",
                principalTable: "JobTitles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayScaleAllowances_AppLookupValues_ShiftLookupValueId",
                table: "PayScaleAllowances");

            migrationBuilder.DropForeignKey(
                name: "FK_PayScaleAllowances_JobTitles_DesignationId",
                table: "PayScaleAllowances");

            migrationBuilder.DropIndex(
                name: "IX_PayScaleAllowances_DesignationId",
                table: "PayScaleAllowances");

            migrationBuilder.DropIndex(
                name: "IX_PayScaleAllowances_ShiftLookupValueId",
                table: "PayScaleAllowances");

            migrationBuilder.DropIndex(
                name: "IX_PayScaleAllowances_TenantId_DesignationId",
                table: "PayScaleAllowances");

            migrationBuilder.DropIndex(
                name: "IX_PayScaleAllowances_TenantId_ShiftLookupValueId",
                table: "PayScaleAllowances");

            migrationBuilder.DropColumn(
                name: "DesignationId",
                table: "PayScaleAllowances");

            migrationBuilder.DropColumn(
                name: "ShiftLookupValueId",
                table: "PayScaleAllowances");

            migrationBuilder.Sql("""
                UPDATE [PlatformTypes].[AllowanceTypes]
                   SET [AllowanceCategory] = N'NIGHT'
                 WHERE [AllowanceCategory] = N'SHIFT';

                UPDATE [dbo].[PayScaleAllowances]
                   SET [AllowanceCategory] = N'NIGHT'
                 WHERE [AllowanceCategory] = N'SHIFT';
                """);
        }
    }
}
