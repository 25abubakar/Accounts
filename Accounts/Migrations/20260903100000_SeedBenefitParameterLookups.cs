using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260903100000_SeedBenefitParameterLookups")]
public sealed class SeedBenefitParameterLookups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @Lookups TABLE
            (
                LookupTypeCode nvarchar(100) NOT NULL,
                LookupTypeName nvarchar(150) NOT NULL,
                ValueCode nvarchar(100) NOT NULL,
                DisplayText nvarchar(150) NOT NULL,
                SortOrder int NOT NULL
            );

            INSERT @Lookups (LookupTypeCode, LookupTypeName, ValueCode, DisplayText, SortOrder) VALUES
                (N'BENEFIT_SERVICE_STATUS', N'Benefit Service Status', N'ACTIVE', N'Active', 10),
                (N'BENEFIT_SERVICE_STATUS', N'Benefit Service Status', N'PROBATION', N'Probation', 20),
                (N'BENEFIT_SERVICE_STATUS', N'Benefit Service Status', N'INACTIVE', N'Inactive', 30),
                (N'BENEFIT_AMOUNT_TYPE', N'Benefit Amount Type', N'PH', N'PH', 10),
                (N'BENEFIT_AMOUNT_TYPE', N'Benefit Amount Type', N'FIXED', N'Fixed', 20),
                (N'BENEFIT_AMOUNT_TYPE', N'Benefit Amount Type', N'PERCENTAGE', N'Percentage', 30),
                (N'BENEFIT_PAY_TYPE', N'Benefit Pay Type', N'BASIC', N'Basic', 10),
                (N'BENEFIT_PAY_TYPE', N'Benefit Pay Type', N'GROSS', N'Gross', 20),
                (N'BENEFIT_PAY_TYPE', N'Benefit Pay Type', N'CURRENT_PAY', N'CurrentPay', 30),
                (N'BENEFIT_SHARE_TYPE', N'Benefit Share Type', N'COMPANY', N'Company', 10),
                (N'BENEFIT_SHARE_TYPE', N'Benefit Share Type', N'STAFF', N'Staff', 20),
                (N'BENEFIT_SHARE_TYPE', N'Benefit Share Type', N'SHARED', N'Shared', 30);

            DECLARE lookup_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT DISTINCT LookupTypeCode, LookupTypeName FROM @Lookups;

            DECLARE @LookupTypeCode nvarchar(100);
            DECLARE @LookupTypeName nvarchar(150);
            DECLARE @LookupTypeId int;

            OPEN lookup_cursor;
            FETCH NEXT FROM lookup_cursor INTO @LookupTypeCode, @LookupTypeName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SELECT TOP (1) @LookupTypeId = LookupTypeId
                FROM dbo.AppLookupTypes
                WHERE LookupTypeCode = @LookupTypeCode;

                IF @LookupTypeId IS NULL
                BEGIN
                    INSERT dbo.AppLookupTypes (LookupTypeCode, LookupTypeName, IsActive, CreatedOn)
                    VALUES (@LookupTypeCode, @LookupTypeName, 1, SYSUTCDATETIME());
                    SET @LookupTypeId = SCOPE_IDENTITY();
                END
                ELSE
                BEGIN
                    UPDATE dbo.AppLookupTypes
                    SET LookupTypeName = @LookupTypeName, IsActive = 1
                    WHERE LookupTypeId = @LookupTypeId;
                END;

                MERGE dbo.AppLookupValues AS target
                USING (
                    SELECT ValueCode, DisplayText, SortOrder
                    FROM @Lookups
                    WHERE LookupTypeCode = @LookupTypeCode
                ) AS source
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

                SET @LookupTypeId = NULL;
                FETCH NEXT FROM lookup_cursor INTO @LookupTypeCode, @LookupTypeName;
            END
            CLOSE lookup_cursor;
            DEALLOCATE lookup_cursor;

            -- Ensure every tenant has an active Bonus benefit type for Parameter Bonus Distribution.
            INSERT INTO PlatformTypes.BenefitTypes
                (TenantId, Name, Code, DisplayOrder, IsActive, CreatedOnUtc)
            SELECT t.Id, N'Bonus', N'BONUS', 100, 1, SYSUTCDATETIME()
            FROM dbo.Tenants t
            WHERE NOT EXISTS (
                SELECT 1
                FROM PlatformTypes.BenefitTypes bt
                WHERE bt.TenantId = t.Id
                  AND (
                      bt.Code = N'BONUS'
                      OR bt.Name = N'Bonus'
                  )
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE dbo.AppLookupTypes
            SET IsActive = 0
            WHERE LookupTypeCode IN (
                N'BENEFIT_SERVICE_STATUS',
                N'BENEFIT_AMOUNT_TYPE',
                N'BENEFIT_PAY_TYPE',
                N'BENEFIT_SHARE_TYPE'
            );
            """);
    }
}
