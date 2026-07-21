using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717160000_SeedExtendedAttendanceLegendStatuses")]
public sealed class SeedExtendedAttendanceLegendStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DECLARE @ProcessId int=(SELECT Id FROM dbo.Processes WHERE ProcessName=N'Attendance');
        DECLARE @Defaults table(Code nvarchar(10),StatusName nvarchar(100),ColorName nvarchar(100),ColorCode nvarchar(20),FontColor nvarchar(20),FontSize nvarchar(20),DisplayOrder int,IsPaid bit);
        INSERT @Defaults VALUES
            (N'DO',N'Day Off',N'Day Off',N'#0EA5E9',N'#FFFFFF',N'12px',20,1),
            (N'HO',N'Holiday',N'Holiday',N'#D946EF',N'#FFFFFF',N'12px',21,1),
            (N'1-L',N'1 Hr Late',N'1 Hr Late',N'#F43F5E',N'#FFFFFF',N'12px',22,1),
            (N'2-L',N'2 Hr Late',N'2 Hr Late',N'#E11D48',N'#FFFFFF',N'12px',23,1),
            (N'1-E',N'1 Hr Early',N'1 Hr Early',N'#F43F5E',N'#FFFFFF',N'12px',24,1),
            (N'2-E',N'2 Hr Early',N'2 Hr Early',N'#E11D48',N'#FFFFFF',N'12px',25,1);

        INSERT dbo.Statuses(StatusName)
        SELECT d.StatusName FROM @Defaults d WHERE NOT EXISTS(SELECT 1 FROM dbo.Statuses s WHERE s.StatusName=d.StatusName);
        INSERT dbo.ColorStyles(ColorName,ColorCode,FontColor,FontSize)
        SELECT d.ColorName,d.ColorCode,d.FontColor,d.FontSize FROM @Defaults d
        WHERE NOT EXISTS(SELECT 1 FROM dbo.ColorStyles c WHERE c.ColorName=d.ColorName AND c.ColorCode=d.ColorCode AND c.FontColor=d.FontColor AND c.FontSize=d.FontSize);
        INSERT dbo.ProcessStatusStyles(ProcessId,StatusId,ColorStyleId,TenantId,IsSystem,Code,Description,DisplayOrder,IsPaid,IsActive,CreatedDate)
        SELECT @ProcessId,s.Id,c.Id,NULL,1,d.Code,N'Standard attendance legend status.',d.DisplayOrder,d.IsPaid,1,SYSUTCDATETIME()
        FROM @Defaults d JOIN dbo.Statuses s ON s.StatusName=d.StatusName
        JOIN dbo.ColorStyles c ON c.ColorName=d.ColorName AND c.ColorCode=d.ColorCode AND c.FontColor=d.FontColor AND c.FontSize=d.FontSize
        WHERE NOT EXISTS(SELECT 1 FROM dbo.ProcessStatusStyles ps WHERE ps.ProcessId=@ProcessId AND ps.Code=d.Code AND ps.TenantId IS NULL);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Attendance status history is intentionally preserved.");
}
