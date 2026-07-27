using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727110000_SeedMonthlyAttendanceChartLegend")]
public sealed class SeedMonthlyAttendanceChartLegend : Migration
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
            (N'P',   N'Present',      N'Monthly Present',      N'#16A34A', N'#FFFFFF', N'9px',  10, 1),
            (N'A',   N'Absent',       N'Monthly Absent',       N'#E11D48', N'#FFFFFF', N'9px',  15, 0),
            (N'DO',  N'Day Off',      N'Monthly Day Off',      N'#0EA5E9', N'#FFFFFF', N'8px',  20, 1),
            (N'HO',  N'Holiday',      N'Monthly Holiday',      N'#C084FC', N'#FFFFFF', N'8px',  30, 1),
            (N'T-P', N'T-Present',    N'Monthly T-Present',    N'#22C55E', N'#FFFFFF', N'7px',  40, 1),
            (N'1-L', N'1 Hr Late',    N'Monthly 1 Hr Late',    N'#E11D48', N'#FFFFFF', N'7px',  50, 0),
            (N'2-L', N'2 Hr Late',    N'Monthly 2 Hr Late',    N'#BE123C', N'#FFFFFF', N'7px',  60, 0),
            (N'1-E', N'1 Hr Early',   N'Monthly 1 Hr Early',   N'#E11D48', N'#FFFFFF', N'7px',  70, 0),
            (N'2-E', N'2 Hr Early',   N'Monthly 2 Hr Early',   N'#BE123C', N'#FFFFFF', N'7px',  80, 0),
            (N'S-L', N'Short Leave',  N'Monthly Short Leave',  N'#EAB308', N'#0F172A', N'7px',  90, 0),
            (N'L',   N'Leave',        N'Monthly Leave',        N'#A855F7', N'#FFFFFF', N'9px', 100, 1);

        INSERT dbo.Statuses(StatusName)
        SELECT d.StatusName
        FROM @Defaults d
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Statuses s WHERE s.StatusName=d.StatusName);

        INSERT dbo.ColorStyles(ColorName,ColorCode,FontColor,FontSize)
        SELECT d.ColorName,d.ColorCode,d.FontColor,d.FontSize
        FROM @Defaults d
        WHERE NOT EXISTS
        (
            SELECT 1 FROM dbo.ColorStyles c
            WHERE c.ColorName=d.ColorName AND c.ColorCode=d.ColorCode
              AND c.FontColor=d.FontColor AND c.FontSize=d.FontSize
        );

        INSERT dbo.ProcessStatusStyles
            (ProcessId,StatusId,ColorStyleId,TenantId,IsSystem,Code,Description,DisplayOrder,IsPaid,IsActive,CreatedDate)
        SELECT @ProcessId,statusRow.Id,colorRow.Id,NULL,1,d.Code,N'Attendance display legend status.',d.DisplayOrder,d.IsPaid,1,SYSUTCDATETIME()
        FROM @Defaults d
        JOIN dbo.Statuses statusRow ON statusRow.StatusName=d.StatusName
        JOIN dbo.ColorStyles colorRow
          ON colorRow.ColorName=d.ColorName AND colorRow.ColorCode=d.ColorCode
         AND colorRow.FontColor=d.FontColor AND colorRow.FontSize=d.FontSize
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.ProcessStatusStyles existing
            WHERE existing.ProcessId=@ProcessId AND existing.Code=d.Code AND existing.TenantId IS NULL
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Monthly attendance chart legend history is intentionally preserved.");
}
