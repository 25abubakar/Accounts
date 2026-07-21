using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720230000_ConfigureHolidayTypesAndMapColors")]
public sealed class ConfigureHolidayTypesAndMapColors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @HolidayTypeId int = (
                SELECT TOP (1) [LookupTypeId]
                FROM [AppLookupTypes]
                WHERE [LookupTypeCode] = N'TIMING_HOLIDAY_TYPE'
            );

            IF @HolidayTypeId IS NULL
            BEGIN
                INSERT INTO [AppLookupTypes] ([LookupTypeCode], [LookupTypeName], [IsActive], [CreatedOn])
                VALUES (N'TIMING_HOLIDAY_TYPE', N'Holiday Type', 1, SYSUTCDATETIME());
                SET @HolidayTypeId = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE [AppLookupTypes]
                SET [LookupTypeName] = N'Holiday Type', [IsActive] = 1
                WHERE [LookupTypeId] = @HolidayTypeId;

            DECLARE @HolidayTypes table
            (
                [ValueCode] nvarchar(100) NOT NULL,
                [DisplayText] nvarchar(150) NOT NULL,
                [SortOrder] int NOT NULL,
                [IsDefault] bit NOT NULL,
                [MetadataJson] nvarchar(2000) NOT NULL
            );
            INSERT INTO @HolidayTypes VALUES
                (N'HOLIDAY', N'Holiday', 10, 0, N'{"defaultIsOn":false}'),
                (N'WORKING_DAY', N'Working Day', 20, 1, N'{"defaultIsOn":true}'),
                (N'ANNUAL_HOLIDAY', N'Annual Holiday', 30, 0, N'{"defaultIsOn":false}'),
                (N'DAY_OFF', N'Day Off', 40, 0, N'{"defaultIsOn":false}');

            MERGE [AppLookupValues] AS target
            USING @HolidayTypes AS source
               ON target.[LookupTypeId] = @HolidayTypeId
              AND target.[ValueCode] = source.[ValueCode]
            WHEN MATCHED THEN UPDATE SET
                [DisplayText] = source.[DisplayText],
                [SortOrder] = source.[SortOrder],
                [IsDefault] = source.[IsDefault],
                [MetadataJson] = source.[MetadataJson],
                [IsActive] = 1
            WHEN NOT MATCHED THEN INSERT
                ([LookupTypeId], [ValueCode], [DisplayText], [SortOrder], [IsDefault], [IsActive], [MetadataJson], [CreatedOn])
            VALUES
                (@HolidayTypeId, source.[ValueCode], source.[DisplayText], source.[SortOrder], source.[IsDefault], 1, source.[MetadataJson], SYSUTCDATETIME());

            UPDATE value
            SET [IsActive] = 0, [IsDefault] = 0
            FROM [AppLookupValues] value
            WHERE value.[LookupTypeId] = @HolidayTypeId
              AND NOT EXISTS (
                  SELECT 1 FROM @HolidayTypes configured
                  WHERE configured.[ValueCode] = value.[ValueCode]);

            DECLARE @ColorTypeId int = (
                SELECT TOP (1) [LookupTypeId]
                FROM [AppLookupTypes]
                WHERE [LookupTypeCode] = N'ATTENDANCE_MAP_COLOR'
            );

            IF @ColorTypeId IS NULL
            BEGIN
                INSERT INTO [AppLookupTypes] ([LookupTypeCode], [LookupTypeName], [IsActive], [CreatedOn])
                VALUES (N'ATTENDANCE_MAP_COLOR', N'Attendance Map Color', 1, SYSUTCDATETIME());
                SET @ColorTypeId = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE [AppLookupTypes]
                SET [LookupTypeName] = N'Attendance Map Color', [IsActive] = 1
                WHERE [LookupTypeId] = @ColorTypeId;

            DECLARE @Colors table
            (
                [ValueCode] nvarchar(100) NOT NULL,
                [DisplayText] nvarchar(150) NOT NULL,
                [SortOrder] int NOT NULL,
                [IsDefault] bit NOT NULL
            );
            INSERT INTO @Colors VALUES
                (N'#64748B', N'Slate', 10, 0),
                (N'#6B7280', N'Gray', 20, 0),
                (N'#71717A', N'Zinc', 30, 0),
                (N'#EF4444', N'Red', 40, 0),
                (N'#F43F5E', N'Rose', 50, 0),
                (N'#EC4899', N'Pink', 60, 0),
                (N'#D946EF', N'Fuchsia', 70, 0),
                (N'#A855F7', N'Purple', 80, 0),
                (N'#8B5CF6', N'Violet', 90, 0),
                (N'#6366F1', N'Indigo', 100, 0),
                (N'#3B82F6', N'Blue', 110, 0),
                (N'#0EA5E9', N'Sky Blue', 120, 1),
                (N'#06B6D4', N'Cyan', 130, 0),
                (N'#14B8A6', N'Teal', 140, 0),
                (N'#10B981', N'Emerald', 150, 0),
                (N'#22C55E', N'Green', 160, 0),
                (N'#84CC16', N'Lime', 170, 0),
                (N'#EAB308', N'Yellow', 180, 0),
                (N'#F59E0B', N'Amber', 190, 0),
                (N'#F97316', N'Orange', 200, 0),
                (N'#92400E', N'Brown', 210, 0),
                (N'#1E3A8A', N'Navy', 220, 0),
                (N'#2563EB', N'Royal Blue', 230, 0),
                (N'#22D3EE', N'Aqua', 240, 0),
                (N'#34D399', N'Mint', 250, 0),
                (N'#15803D', N'Forest', 260, 0),
                (N'#4D7C0F', N'Olive', 270, 0),
                (N'#CA8A04', N'Gold', 280, 0),
                (N'#FB7185', N'Coral', 290, 0),
                (N'#9F1239', N'Maroon', 300, 0);

            MERGE [AppLookupValues] AS target
            USING @Colors AS source
               ON target.[LookupTypeId] = @ColorTypeId
              AND target.[ValueCode] = source.[ValueCode]
            WHEN MATCHED THEN UPDATE SET
                [DisplayText] = source.[DisplayText],
                [SortOrder] = source.[SortOrder],
                [IsDefault] = source.[IsDefault],
                [IsActive] = 1
            WHEN NOT MATCHED THEN INSERT
                ([LookupTypeId], [ValueCode], [DisplayText], [SortOrder], [IsDefault], [IsActive], [CreatedOn])
            VALUES
                (@ColorTypeId, source.[ValueCode], source.[DisplayText], source.[SortOrder], source.[IsDefault], 1, SYSUTCDATETIME());

            UPDATE value
            SET [IsActive] = 0, [IsDefault] = 0
            FROM [AppLookupValues] value
            WHERE value.[LookupTypeId] = @ColorTypeId
              AND NOT EXISTS (
                  SELECT 1 FROM @Colors configured
                  WHERE configured.[ValueCode] = value.[ValueCode]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @HolidayTypeId int = (
                SELECT TOP (1) [LookupTypeId] FROM [AppLookupTypes]
                WHERE [LookupTypeCode] = N'TIMING_HOLIDAY_TYPE'
            );
            IF @HolidayTypeId IS NOT NULL
            BEGIN
                UPDATE [AppLookupValues] SET [IsActive] = 0, [IsDefault] = 0
                WHERE [LookupTypeId] = @HolidayTypeId;

                UPDATE [AppLookupValues] SET [DisplayText] = N'Working Day', [SortOrder] = 10, [IsDefault] = 1, [IsActive] = 1
                WHERE [LookupTypeId] = @HolidayTypeId AND [ValueCode] = N'WORKING_DAY';
                UPDATE [AppLookupValues] SET [DisplayText] = N'Special Duty', [SortOrder] = 20, [IsDefault] = 0, [IsActive] = 1
                WHERE [LookupTypeId] = @HolidayTypeId AND [ValueCode] = N'SPECIAL_DUTY';
                UPDATE [AppLookupValues] SET [DisplayText] = N'Day Off', [SortOrder] = 30, [IsDefault] = 0, [IsActive] = 1
                WHERE [LookupTypeId] = @HolidayTypeId AND [ValueCode] = N'DAY_OFF';
                UPDATE [AppLookupValues] SET [DisplayText] = N'Public Holiday', [SortOrder] = 40, [IsDefault] = 0, [IsActive] = 1
                WHERE [LookupTypeId] = @HolidayTypeId AND [ValueCode] = N'PUBLIC_HOLIDAY';
                UPDATE [AppLookupValues] SET [DisplayText] = N'Company Holiday', [SortOrder] = 50, [IsDefault] = 0, [IsActive] = 1
                WHERE [LookupTypeId] = @HolidayTypeId AND [ValueCode] = N'COMPANY_HOLIDAY';
            END;

            UPDATE [AppLookupTypes] SET [IsActive] = 0
            WHERE [LookupTypeCode] = N'ATTENDANCE_MAP_COLOR';
            """);
    }
}
