using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260909114000_UpdateBenefitServiceStatuses")]
public sealed class UpdateBenefitServiceStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @LookupTypeId int = (
                SELECT TOP (1) LookupTypeId
                FROM dbo.AppLookupTypes
                WHERE LookupTypeCode = N'BENEFIT_SERVICE_STATUS'
            );

            IF @LookupTypeId IS NOT NULL
            BEGIN
                MERGE dbo.AppLookupValues AS target
                USING (VALUES
                    (N'ACTIVE', N'Active', 10),
                    (N'INACTIVE', N'Inactive', 20),
                    (N'RETIRED', N'Retired', 30)
                ) AS source(ValueCode, DisplayText, SortOrder)
                ON target.LookupTypeId = @LookupTypeId
                   AND target.ValueCode = source.ValueCode
                WHEN MATCHED THEN UPDATE SET
                    DisplayText = source.DisplayText,
                    SortOrder = source.SortOrder,
                    IsActive = 1
                WHEN NOT MATCHED THEN INSERT
                    (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, CreatedOn)
                VALUES
                    (@LookupTypeId, source.ValueCode, source.DisplayText, source.SortOrder, 0, 1, SYSUTCDATETIME());

                UPDATE dbo.AppLookupValues
                SET IsActive = 0
                WHERE LookupTypeId = @LookupTypeId
                  AND ValueCode = N'PROBATION';
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @LookupTypeId int = (
                SELECT TOP (1) LookupTypeId
                FROM dbo.AppLookupTypes
                WHERE LookupTypeCode = N'BENEFIT_SERVICE_STATUS'
            );

            IF @LookupTypeId IS NOT NULL
            BEGIN
                UPDATE dbo.AppLookupValues
                SET IsActive = CASE WHEN ValueCode = N'PROBATION' THEN 1 ELSE 0 END
                WHERE LookupTypeId = @LookupTypeId
                  AND ValueCode IN (N'PROBATION', N'RETIRED');
            END;
            """);
    }
}
