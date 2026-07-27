using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727113000_EnsureAttendanceDisplayStatusLegend")]
public sealed class EnsureAttendanceDisplayStatusLegend : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF NOT EXISTS (SELECT 1 FROM dbo.Processes WHERE ProcessName=N'Attendance Display Status')
            INSERT dbo.Processes(ProcessName) VALUES(N'Attendance Display Status');

        DECLARE @ProcessId int=(SELECT Id FROM dbo.Processes WHERE ProcessName=N'Attendance Display Status');
        DECLARE @Defaults table
        (
            Code nvarchar(10) NOT NULL,
            StatusName nvarchar(100) NOT NULL,
            ColorName nvarchar(100) NOT NULL,
            ColorCode nvarchar(20) NOT NULL,
            FontColor nvarchar(20) NOT NULL,
            FontSize nvarchar(20) NOT NULL,
            DisplayOrder int NOT NULL,
            IsPaid bit NOT NULL
        );

        INSERT @Defaults(Code,StatusName,ColorName,ColorCode,FontColor,FontSize,DisplayOrder,IsPaid) VALUES
            (N'P',   N'Present',      N'Chart Present',      N'#16A34A', N'#FFFFFF', N'9px',  10, 1),
            (N'A',   N'Absent',       N'Chart Absent',       N'#EF4444', N'#FFFFFF', N'9px',  15, 0),
            (N'DO',  N'Day Off',      N'Chart Day Off',      N'#0EA5E9', N'#FFFFFF', N'8px',  20, 1),
            (N'HO',  N'Holiday',      N'Chart Holiday',      N'#C084FC', N'#FFFFFF', N'8px',  30, 1),
            (N'T-P', N'T-Present',    N'Chart T-Present',    N'#22C55E', N'#FFFFFF', N'7px',  40, 1),
            (N'1-L', N'1 Hr Late',    N'Chart 1 Hr Late',    N'#E11D48', N'#FFFFFF', N'7px',  50, 0),
            (N'2-L', N'2 Hr Late',    N'Chart 2 Hr Late',    N'#BE123C', N'#FFFFFF', N'7px',  60, 0),
            (N'1-E', N'1 Hr Early',   N'Chart 1 Hr Early',   N'#E11D48', N'#FFFFFF', N'7px',  70, 0),
            (N'2-E', N'2 Hr Early',   N'Chart 2 Hr Early',   N'#BE123C', N'#FFFFFF', N'7px',  80, 0),
            (N'S-L', N'Short Leave',  N'Chart Short Leave',  N'#F59E0B', N'#0F172A', N'7px',  90, 0),
            (N'L',   N'Leave',        N'Chart Leave',        N'#A855F7', N'#FFFFFF', N'9px', 100, 1);

        INSERT dbo.Statuses(StatusName)
        SELECT source.StatusName
        FROM @Defaults source
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Statuses existing WHERE existing.StatusName=source.StatusName);

        INSERT dbo.ColorStyles(ColorName,ColorCode,FontColor,FontSize)
        SELECT source.ColorName,source.ColorCode,source.FontColor,source.FontSize
        FROM @Defaults source
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.ColorStyles existing
            WHERE existing.ColorName=source.ColorName
              AND existing.ColorCode=source.ColorCode
              AND existing.FontColor=source.FontColor
              AND existing.FontSize=source.FontSize
        );

        MERGE dbo.ProcessStatusStyles AS target
        USING
        (
            SELECT
                @ProcessId AS ProcessId,
                statusRow.Id AS StatusId,
                colorRow.Id AS ColorStyleId,
                source.Code,
                source.DisplayOrder,
                source.IsPaid
            FROM @Defaults source
            JOIN dbo.Statuses statusRow ON statusRow.StatusName=source.StatusName
            JOIN dbo.ColorStyles colorRow
              ON colorRow.ColorName=source.ColorName
             AND colorRow.ColorCode=source.ColorCode
             AND colorRow.FontColor=source.FontColor
             AND colorRow.FontSize=source.FontSize
        ) AS source
        ON target.ProcessId=source.ProcessId AND target.Code=source.Code AND target.TenantId IS NULL
        WHEN MATCHED THEN
            UPDATE SET
                target.StatusId=source.StatusId,
                target.ColorStyleId=source.ColorStyleId,
                target.DisplayOrder=source.DisplayOrder,
                target.IsPaid=source.IsPaid,
                target.IsActive=1,
                target.ModifiedDate=SYSUTCDATETIME()
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (ProcessId,StatusId,ColorStyleId,TenantId,IsSystem,Code,Description,DisplayOrder,IsPaid,IsActive,CreatedDate)
            VALUES (source.ProcessId,source.StatusId,source.ColorStyleId,NULL,1,source.Code,N'Attendance display legend status.',source.DisplayOrder,source.IsPaid,1,SYSUTCDATETIME());
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Attendance display status legend history is intentionally preserved.");
}
