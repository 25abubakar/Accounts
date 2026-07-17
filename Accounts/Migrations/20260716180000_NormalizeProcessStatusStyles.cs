using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716180000_NormalizeProcessStatusStyles")]
public sealed class NormalizeProcessStatusStyles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE dbo.Processes(
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Processes PRIMARY KEY,
                ProcessName nvarchar(100) NOT NULL CONSTRAINT UQ_Processes_ProcessName UNIQUE);
            CREATE TABLE dbo.Statuses(
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Statuses PRIMARY KEY,
                StatusName nvarchar(100) NOT NULL CONSTRAINT UQ_Statuses_StatusName UNIQUE);
            CREATE TABLE dbo.ColorStyles(
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ColorStyles PRIMARY KEY,
                ColorName nvarchar(100) NOT NULL,
                ColorCode nvarchar(20) NOT NULL,
                FontColor nvarchar(20) NOT NULL,
                FontSize nvarchar(20) NOT NULL,
                CONSTRAINT UQ_ColorStyles_Style UNIQUE(ColorName, ColorCode, FontColor, FontSize));
            CREATE TABLE dbo.ProcessStatusStyles(
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessStatusStyles PRIMARY KEY,
                ProcessId int NOT NULL,
                StatusId int NOT NULL,
                ColorStyleId int NOT NULL,
                Code nvarchar(10) NOT NULL,
                Description nvarchar(500) NULL,
                DisplayOrder int NOT NULL,
                IsPaid bit NOT NULL,
                IsActive bit NOT NULL CONSTRAINT DF_ProcessStatusStyles_IsActive DEFAULT(1),
                CreatedDate datetime2 NOT NULL CONSTRAINT DF_ProcessStatusStyles_Created DEFAULT(SYSUTCDATETIME()),
                ModifiedDate datetime2 NULL,
                CONSTRAINT FK_ProcessStatusStyles_Processes FOREIGN KEY(ProcessId) REFERENCES dbo.Processes(Id),
                CONSTRAINT FK_ProcessStatusStyles_Statuses FOREIGN KEY(StatusId) REFERENCES dbo.Statuses(Id),
                CONSTRAINT FK_ProcessStatusStyles_ColorStyles FOREIGN KEY(ColorStyleId) REFERENCES dbo.ColorStyles(Id),
                CONSTRAINT UQ_ProcessStatusStyles_Code UNIQUE(ProcessId, Code),
                CONSTRAINT UQ_ProcessStatusStyles_Assignment UNIQUE(ProcessId, StatusId, ColorStyleId));

            INSERT dbo.Processes(ProcessName) VALUES(N'Attendance');
            DECLARE @AttendanceProcessId int = SCOPE_IDENTITY();

            SET IDENTITY_INSERT dbo.Statuses ON;
            INSERT dbo.Statuses(Id, StatusName) SELECT Id, StatusName FROM dbo.StatusMaster;
            SET IDENTITY_INSERT dbo.Statuses OFF;

            SET IDENTITY_INSERT dbo.ColorStyles ON;
            INSERT dbo.ColorStyles(Id, ColorName, ColorCode, FontColor, FontSize)
            SELECT Id, CONCAT(StatusName, N' style'), COALESCE(ColorCode, N'#64748B'), N'#FFFFFF', N'12px'
            FROM dbo.StatusMaster;
            SET IDENTITY_INSERT dbo.ColorStyles OFF;

            SET IDENTITY_INSERT dbo.ProcessStatusStyles ON;
            INSERT dbo.ProcessStatusStyles(Id, ProcessId, StatusId, ColorStyleId, Code, Description, DisplayOrder, IsPaid, IsActive, CreatedDate, ModifiedDate)
            SELECT Id, @AttendanceProcessId, Id, Id, Code, Description, DisplayOrder, IsPaid, IsActive, CreatedDate, ModifiedDate
            FROM dbo.StatusMaster;
            SET IDENTITY_INSERT dbo.ProcessStatusStyles OFF;

            DECLARE @AttendanceStatusFk sysname = (
                SELECT TOP (1) fk.name
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
                WHERE fk.parent_object_id = OBJECT_ID(N'dbo.AttendanceRecords')
                  AND c.name = N'AttendanceStatusId');
            IF @AttendanceStatusFk IS NOT NULL
            BEGIN
                DECLARE @DropAttendanceStatusFkSql nvarchar(max) =
                    N'ALTER TABLE dbo.AttendanceRecords DROP CONSTRAINT ' + QUOTENAME(@AttendanceStatusFk) + N';';
                EXEC sys.sp_executesql @DropAttendanceStatusFkSql;
            END;
            ALTER TABLE dbo.AttendanceRecords ADD CONSTRAINT FK_AttendanceRecords_ProcessStatusStyles_AttendanceStatusId
                FOREIGN KEY(AttendanceStatusId) REFERENCES dbo.ProcessStatusStyles(Id);
            DROP TABLE dbo.StatusMaster;
            """);

        // SQL Server requires CREATE OR ALTER PROCEDURE to be the first statement in its batch.
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
            AS
            BEGIN
                SET NOCOUNT ON;
                ;WITH Dates AS (
                    SELECT @DateFrom AttendanceDate UNION ALL SELECT DATEADD(day,1,AttendanceDate) FROM Dates WHERE AttendanceDate < @DateTo
                ), VisiblePeople AS (
                    SELECT TRY_CONVERT(uniqueidentifier,[value]) PersonId FROM OPENJSON(@VisiblePersonIds)
                    WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
                )
                SELECT ar.Id,p.PersonId,COALESCE(sv.LoginId,v.VacancyCode) EmployeeNumber,p.FullName EmployeeName,
                    COALESCE(v.Department,org.Name) Department,COALESCE(jt.TitleName,v.JobTitle,N'') Designation,d.AttendanceDate,
                    ar.AttendanceStatusId,st.StatusName,ps.Code StatusCode,cs.ColorCode StatusColorCode,
                    ar.CheckInUtc,ar.CheckOutUtc,ar.TotalBreakMinutes,p.ShiftStartTime,p.ShiftEndTime,p.TimeZoneId,p.ReportsToPersonId
                FROM VisiblePeople vp JOIN Persons p ON p.PersonId=vp.PersonId AND p.IsActive=1 AND p.TenantId=@TenantId
                JOIN StaffVacancy sv ON sv.PersonId=p.PersonId JOIN Vacancies v ON v.VacancyId=sv.VacancyId
                LEFT JOIN JobTitles jt ON jt.Id=v.JobTitleId LEFT JOIN OrganizationTree org ON org.Id=v.OrganizationId CROSS JOIN Dates d
                LEFT JOIN AttendanceRecords ar ON ar.PersonId=p.PersonId AND ar.AttendanceDate=d.AttendanceDate AND ar.TenantId=@TenantId
                LEFT JOIN ProcessStatusStyles ps ON ps.Id=ar.AttendanceStatusId LEFT JOIN Statuses st ON st.Id=ps.StatusId
                LEFT JOIN ColorStyles cs ON cs.Id=ps.ColorStyleId
                WHERE DATENAME(weekday,d.AttendanceDate) NOT IN(N'Saturday',N'Sunday') OR ar.Id IS NOT NULL
                ORDER BY d.AttendanceDate DESC,p.FullName OPTION(MAXRECURSION 367);
            END
            """);

        migrationBuilder.Sql(
            """
            DECLARE @JobTitleMenuId int=(
                SELECT TOP(1) Id FROM Menus
                WHERE Title IN(N'Platform Settings',N'Settings') AND ParentId IS NULL
                ORDER BY CASE WHEN Title=N'Platform Settings' THEN 0 ELSE 1 END, Id);
            IF NOT EXISTS(SELECT 1 FROM Menus WHERE Route=N'/settings/statuses')
                INSERT Menus(Title,Icon,Route,ParentId,SortOrder,IsActive)
                VALUES(N'Status',N'Palette',N'/settings/statuses',@JobTitleMenuId,1,1);

            DECLARE @StatusMenuId int=(SELECT TOP(1) Id FROM Menus WHERE Route=N'/settings/statuses');
            DECLARE @StatusFeaturePrefix nvarchar(50)=CONCAT(N'MENU_',@StatusMenuId);
            IF NOT EXISTS(SELECT 1 FROM Features WHERE FeatureKey=@StatusFeaturePrefix)
                INSERT Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
                VALUES(@StatusFeaturePrefix,N'Status',N'Menu',N'Open the shared status configuration screen.',SYSUTCDATETIME());
            IF NOT EXISTS(SELECT 1 FROM Features WHERE FeatureKey=CONCAT(@StatusFeaturePrefix,N'_VIEW'))
                INSERT Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
                VALUES(CONCAT(@StatusFeaturePrefix,N'_VIEW'),N'Status - View',N'Menu',NULL,SYSUTCDATETIME());
            IF NOT EXISTS(SELECT 1 FROM Features WHERE FeatureKey=CONCAT(@StatusFeaturePrefix,N'_ADD'))
                INSERT Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
                VALUES(CONCAT(@StatusFeaturePrefix,N'_ADD'),N'Status - Add',N'Menu',NULL,SYSUTCDATETIME());
            IF NOT EXISTS(SELECT 1 FROM Features WHERE FeatureKey=CONCAT(@StatusFeaturePrefix,N'_EDIT'))
                INSERT Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
                VALUES(CONCAT(@StatusFeaturePrefix,N'_EDIT'),N'Status - Edit',N'Menu',NULL,SYSUTCDATETIME());
            IF NOT EXISTS(SELECT 1 FROM Features WHERE FeatureKey=CONCAT(@StatusFeaturePrefix,N'_DELETE'))
                INSERT Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
                VALUES(CONCAT(@StatusFeaturePrefix,N'_DELETE'),N'Status - Delete',N'Menu',NULL,SYSUTCDATETIME());
            DECLARE @StatusBasePermissionId int=(SELECT PermissionId FROM Features WHERE FeatureKey=@StatusFeaturePrefix);
            IF NOT EXISTS(SELECT 1 FROM MenuPermissions WHERE MenuId=@StatusMenuId AND PermissionId=@StatusBasePermissionId)
                INSERT MenuPermissions(MenuId,PermissionId) VALUES(@StatusMenuId,@StatusBasePermissionId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("This data-normalization migration is intentionally forward-only to protect status history.");
    }
}
