using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddBenefitParameterFigureAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Percentage",
                table: "PayrollBenefitParameters",
                type: "decimal(9,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE [parameter]
                SET AmountType = CASE
                        WHEN benefitRule.BenefitsType = N'EOBI' THEN N''
                        WHEN [parameter].AmountType IN (N'Fixed', N'Figure') THEN N'Figure'
                        ELSE N'Pay Ref'
                    END,
                    PayType = CASE
                        WHEN benefitRule.BenefitsType = N'EOBI' OR [parameter].AmountType IN (N'Fixed', N'Figure') THEN N''
                        WHEN [parameter].PayType = N'CurrentPay' THEN N'Current'
                        ELSE [parameter].PayType
                    END
                FROM PayrollBenefitParameters [parameter]
                INNER JOIN PayrollBenefitRules benefitRule ON benefitRule.Id = [parameter].BenefitRuleId;

                UPDATE [parameter]
                SET Amount = 5000
                FROM PayrollBenefitParameters [parameter]
                INNER JOIN PayrollBenefitRules benefitRule ON benefitRule.Id = [parameter].BenefitRuleId
                WHERE [parameter].TenantId = 2007
                  AND benefitRule.Name = N'Eid-Feb-26'
                  AND [parameter].Name = N'Eid Bonus 26';

                UPDATE [parameter]
                SET PayType = N'Current'
                FROM PayrollBenefitParameters [parameter]
                INNER JOIN PayrollBenefitRules benefitRule ON benefitRule.Id = [parameter].BenefitRuleId
                WHERE [parameter].TenantId = 2007
                  AND benefitRule.Name = N'Eid_Bonus 2025';

                DECLARE @AmountTypeId int = (
                    SELECT TOP (1) LookupTypeId FROM AppLookupTypes WHERE LookupTypeCode = N'BENEFIT_AMOUNT_TYPE'
                );
                IF @AmountTypeId IS NOT NULL
                BEGIN
                    MERGE AppLookupValues AS target
                    USING (VALUES
                        (N'FIGURE', N'Figure', 10),
                        (N'PAY_REF', N'Pay Ref', 20)
                    ) AS source(ValueCode, DisplayText, SortOrder)
                    ON target.LookupTypeId = @AmountTypeId AND target.ValueCode = source.ValueCode
                    WHEN MATCHED THEN UPDATE SET DisplayText = source.DisplayText, SortOrder = source.SortOrder, IsActive = 1
                    WHEN NOT MATCHED THEN INSERT
                        (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, CreatedOn)
                    VALUES (@AmountTypeId, source.ValueCode, source.DisplayText, source.SortOrder, 0, 1, SYSUTCDATETIME());

                    UPDATE AppLookupValues SET IsActive = 0
                    WHERE LookupTypeId = @AmountTypeId AND ValueCode IN (N'PH', N'FIXED', N'PERCENTAGE');
                END;

                DECLARE @PayTypeId int = (
                    SELECT TOP (1) LookupTypeId FROM AppLookupTypes WHERE LookupTypeCode = N'BENEFIT_PAY_TYPE'
                );
                IF @PayTypeId IS NOT NULL
                BEGIN
                    MERGE AppLookupValues AS target
                    USING (VALUES
                        (N'CURRENT', N'Current', 20),
                        (N'NET', N'Net', 30),
                        (N'GROSS', N'Gross', 40)
                    ) AS source(ValueCode, DisplayText, SortOrder)
                    ON target.LookupTypeId = @PayTypeId AND target.ValueCode = source.ValueCode
                    WHEN MATCHED THEN UPDATE SET DisplayText = source.DisplayText, SortOrder = source.SortOrder, IsActive = 1
                    WHEN NOT MATCHED THEN INSERT
                        (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, CreatedOn)
                    VALUES (@PayTypeId, source.ValueCode, source.DisplayText, source.SortOrder, 0, 1, SYSUTCDATETIME());

                    UPDATE AppLookupValues SET IsActive = 0
                    WHERE LookupTypeId = @PayTypeId AND ValueCode = N'CURRENT_PAY';
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Percentage",
                table: "PayrollBenefitParameters");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "PayrollBenefitParameters");
        }
    }
}
