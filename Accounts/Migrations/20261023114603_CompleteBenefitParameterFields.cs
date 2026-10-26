using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class CompleteBenefitParameterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollBenefitParameters_TenantId_Reference",
                table: "PayrollBenefitParameters");

            migrationBuilder.AlterColumn<decimal>(
                name: "StaffShare",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MinimumService",
                table: "PayrollBenefitRules",
                type: "decimal(9,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MinimumPh",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MaximumPh",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MaximumExpense",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsIneligible",
                table: "PayrollBenefitRules",
                type: "bit",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<decimal>(
                name: "CompanyShare",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "StaffShare",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Percentage",
                table: "PayrollBenefitParameters",
                type: "decimal(9,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,4)");

            migrationBuilder.AlterColumn<string>(
                name: "PayType",
                table: "PayrollBenefitParameters",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<decimal>(
                name: "MinimumService",
                table: "PayrollBenefitParameters",
                type: "decimal(9,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CompanyShare",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<string>(
                name: "Entitled",
                table: "PayrollBenefitParameters",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                table: "PayrollBenefitParameters",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumPh",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumPh",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodFrom",
                table: "PayrollBenefitParameters",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodTo",
                table: "PayrollBenefitParameters",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitParameters_TenantId_Reference",
                table: "PayrollBenefitParameters",
                columns: new[] { "TenantId", "Reference" });

            migrationBuilder.Sql(ParameterListSql);
            migrationBuilder.Sql(NullableCalculationSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never silently replace imported blanks with zero or discard period data.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.PayrollBenefitRules WHERE MaximumExpense IS NULL OR MinimumService IS NULL OR MaximumPh IS NULL OR MinimumPh IS NULL OR IsIneligible IS NULL OR CompanyShare IS NULL OR StaffShare IS NULL)
                   OR EXISTS (SELECT 1 FROM dbo.PayrollBenefitParameters WHERE PayType IS NULL OR Amount IS NULL OR Percentage IS NULL OR MinimumService IS NULL OR CompanyShare IS NULL OR StaffShare IS NULL OR PeriodFrom IS NOT NULL OR PeriodTo IS NOT NULL OR Frequency IS NOT NULL OR Entitled IS NOT NULL OR MaximumPh IS NOT NULL OR MinimumPh IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM dbo.PayrollBenefitParameters GROUP BY TenantId, Reference HAVING COUNT(*) > 1)
                    THROW 51000, 'Resolve nullable benefit data and repeated references before reverting this migration.', 1;
                """);
            migrationBuilder.Sql(PreviousParameterListSql);
            migrationBuilder.DropIndex(
                name: "IX_PayrollBenefitParameters_TenantId_Reference",
                table: "PayrollBenefitParameters");

            migrationBuilder.DropColumn(
                name: "Entitled",
                table: "PayrollBenefitParameters");

            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "PayrollBenefitParameters");

            migrationBuilder.DropColumn(
                name: "MaximumPh",
                table: "PayrollBenefitParameters");

            migrationBuilder.DropColumn(
                name: "MinimumPh",
                table: "PayrollBenefitParameters");

            migrationBuilder.DropColumn(
                name: "PeriodFrom",
                table: "PayrollBenefitParameters");

            migrationBuilder.DropColumn(
                name: "PeriodTo",
                table: "PayrollBenefitParameters");

            migrationBuilder.AlterColumn<decimal>(
                name: "StaffShare",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MinimumService",
                table: "PayrollBenefitRules",
                type: "decimal(9,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MinimumPh",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MaximumPh",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MaximumExpense",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsIneligible",
                table: "PayrollBenefitRules",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CompanyShare",
                table: "PayrollBenefitRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "StaffShare",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Percentage",
                table: "PayrollBenefitParameters",
                type: "decimal(9,4)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PayType",
                table: "PayrollBenefitParameters",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MinimumService",
                table: "PayrollBenefitParameters",
                type: "decimal(9,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CompanyShare",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "PayrollBenefitParameters",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitParameters_TenantId_Reference",
                table: "PayrollBenefitParameters",
                columns: new[] { "TenantId", "Reference" },
                unique: true);
        }
        private const string ParameterListSql = """
CREATE OR ALTER PROCEDURE dbo.usp_Pay_BenefitParameters_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        p.Id,
        r.Name AS RuleName,
        p.Name,
        p.Reference AS [Ref],
        p.Entitled,
        p.PeriodFrom AS PdFrom,
        p.PeriodTo AS PdTo,
        p.BenefitRuleId AS BenefitId,
        p.Frequency AS FreqId,
        p.MinimumService AS MinSer,
        p.AmountType AS AmtType,
        p.PayType AS PayTypeId,
        p.Amount,
        p.Percentage,
        p.MaximumPh AS MaxPh,
        p.MinimumPh AS MinPh,
        p.CompanyShare AS CoyShare,
        p.StaffShare,
        r.BenefitsType,
        d.Month AS BonusMonth,
        d.InstallmentStart,
        d.InstallmentEnd,
        COALESCE(d.BasicPercentage, 0) AS BasicPercentage,
        COALESCE(d.ServicePercentage, 0) AS ServicePercentage,
        COALESCE(d.ServiceYears, 0) AS ServiceYears,
        COALESCE(d.AssessmentPercentage, 0) AS AssessmentPercentage,
        COALESCE(d.AttendancePercentage, 0) AS AttendancePercentage,
        COALESCE(d.LeavePercentage, 0) AS LeavePercentage,
        COALESCE(d.DisciplinePercentage, 0) AS DisciplinePercentage,
        COALESCE(d.Installments, 1) AS Installments
    FROM dbo.PayrollBenefitParameters p
    INNER JOIN dbo.PayrollBenefitRules r ON r.Id = p.BenefitRuleId AND r.TenantId = p.TenantId
    LEFT JOIN dbo.PayrollBonusDistributions d ON d.BenefitParameterId = p.Id AND d.TenantId = p.TenantId
    WHERE p.TenantId = @TenantId
    ORDER BY p.Name;
END
""";
        private const string PreviousParameterListSql = """
CREATE OR ALTER PROCEDURE dbo.usp_Pay_BenefitParameters_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        p.Id,
        r.Name AS RuleName,
        p.Name,
        p.Reference AS [Ref],
        COALESCE(r.Entitled, r.CompanyName) AS Entitled,
        p.BenefitRuleId AS BenefitId,
        r.Frequency AS FreqId,
        p.MinimumService AS MinSer,
        p.AmountType AS AmtType,
        p.PayType AS PayTypeId,
        p.Amount,
        p.Percentage,
        r.MaximumPh AS MaxPh,
        r.MinimumPh AS MinPh,
        p.CompanyShare AS CoyShare,
        p.StaffShare,
        r.BenefitsType,
        d.Month AS BonusMonth,
        d.InstallmentStart,
        d.InstallmentEnd,
        COALESCE(d.BasicPercentage, 0) AS BasicPercentage,
        COALESCE(d.ServicePercentage, 0) AS ServicePercentage,
        COALESCE(d.ServiceYears, 0) AS ServiceYears,
        COALESCE(d.AssessmentPercentage, 0) AS AssessmentPercentage,
        COALESCE(d.AttendancePercentage, 0) AS AttendancePercentage,
        COALESCE(d.LeavePercentage, 0) AS LeavePercentage,
        COALESCE(d.DisciplinePercentage, 0) AS DisciplinePercentage,
        COALESCE(d.Installments, 1) AS Installments
    FROM dbo.PayrollBenefitParameters p
    INNER JOIN dbo.PayrollBenefitRules r ON r.Id = p.BenefitRuleId AND r.TenantId = p.TenantId
    LEFT JOIN dbo.PayrollBonusDistributions d ON d.BenefitParameterId = p.Id AND d.TenantId = p.TenantId
    WHERE p.TenantId = @TenantId
    ORDER BY p.Name;
END
""";
        private const string NullableCalculationSql = """
DECLARE @name nvarchar(256), @sql nvarchar(max);
DECLARE benefit_procs CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.procedures WHERE schema_id=SCHEMA_ID('dbo') AND name IN ('usp_Pay_BonusRules_List','usp_Payroll_GenerateMonthly','usp_Pay_BonusGenerate_Candidates');
OPEN benefit_procs; FETCH NEXT FROM benefit_procs INTO @name;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @sql=OBJECT_DEFINITION(OBJECT_ID('dbo.'+@name));
 SET @sql=REPLACE(@sql, 'CREATE OR ALTER PROCEDURE', 'ALTER PROCEDURE');
 SET @sql=REPLACE(@sql, 'CREATE   PROCEDURE', 'ALTER PROCEDURE');
 SET @sql=REPLACE(@sql, 'CREATE PROCEDURE', 'ALTER PROCEDURE');
 SET @sql=REPLACE(@sql, 'benRule.IsIneligible = 0', 'COALESCE(benRule.IsIneligible,0) = 0');
 SET @sql=REPLACE(@sql, 'r.IsIneligible = 0', 'COALESCE(r.IsIneligible,0) = 0');
 SET @sql=REPLACE(@sql, 'a.ServiceMonths >= p.MinimumService', 'a.ServiceMonths >= COALESCE(p.MinimumService,0)');
 SET @sql=REPLACE(@sql, 'sc.ServiceMonths >= dw.MinimumService', 'sc.ServiceMonths >= COALESCE(dw.MinimumService,0)');
 EXEC sys.sp_executesql @sql;
 FETCH NEXT FROM benefit_procs INTO @name;
END;
CLOSE benefit_procs; DEALLOCATE benefit_procs;
""";
    }
}
