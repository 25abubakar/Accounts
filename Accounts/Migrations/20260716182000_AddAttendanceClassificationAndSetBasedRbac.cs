using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716182000_AddAttendanceClassificationAndSetBasedRbac")]
public sealed class AddAttendanceClassificationAndSetBasedRbac : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE dbo.AttendanceEntryTypes(
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceEntryTypes PRIMARY KEY,
                Code nvarchar(30) NOT NULL,
                Name nvarchar(100) NOT NULL,
                IsActive bit NOT NULL CONSTRAINT DF_AttendanceEntryTypes_IsActive DEFAULT(1),
                CONSTRAINT UQ_AttendanceEntryTypes_Code UNIQUE(Code));
            CREATE TABLE dbo.AttendanceWorkModes(
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceWorkModes PRIMARY KEY,
                Code nvarchar(30) NOT NULL,
                Name nvarchar(100) NOT NULL,
                IsActive bit NOT NULL CONSTRAINT DF_AttendanceWorkModes_IsActive DEFAULT(1),
                CONSTRAINT UQ_AttendanceWorkModes_Code UNIQUE(Code));

            INSERT dbo.AttendanceEntryTypes(Code,Name) VALUES
                (N'NONE',N'No Attendance'),(N'CHECK',N'Check In / Out'),(N'MANUAL',N'Manual Entry'),(N'IMPORT',N'Imported');
            INSERT dbo.AttendanceWorkModes(Code,Name) VALUES
                (N'ONSITE',N'On-site'),(N'REMOTE',N'Remote'),(N'HYBRID',N'Hybrid');

            ALTER TABLE dbo.AttendanceRecords ADD AttendanceEntryTypeId int NULL, AttendanceWorkModeId int NULL;
            """);

        // SQL Server compiles a batch before ALTER TABLE has exposed its new columns.
        // Backfill and constraint creation therefore belong in the following batch.
        migrationBuilder.Sql(
            """
            DECLARE @CheckTypeId int=(SELECT Id FROM dbo.AttendanceEntryTypes WHERE Code=N'CHECK');
            DECLARE @OnsiteModeId int=(SELECT Id FROM dbo.AttendanceWorkModes WHERE Code=N'ONSITE');
            UPDATE dbo.AttendanceRecords SET AttendanceEntryTypeId=@CheckTypeId,AttendanceWorkModeId=@OnsiteModeId
            WHERE CheckInUtc IS NOT NULL OR CheckOutUtc IS NOT NULL;
            DECLARE @ManualTypeId int=(SELECT Id FROM dbo.AttendanceEntryTypes WHERE Code=N'MANUAL');
            UPDATE dbo.AttendanceRecords SET AttendanceEntryTypeId=@ManualTypeId
            WHERE AttendanceEntryTypeId IS NULL;
            DECLARE @RemoteModeId int=(SELECT Id FROM dbo.AttendanceWorkModes WHERE Code=N'REMOTE');
            UPDATE ar SET AttendanceWorkModeId=@RemoteModeId
            FROM dbo.AttendanceRecords ar JOIN dbo.ProcessStatusStyles ps ON ps.Id=ar.AttendanceStatusId
            WHERE ps.Code=N'WFH';
            ALTER TABLE dbo.AttendanceRecords ADD CONSTRAINT FK_AttendanceRecords_AttendanceEntryTypes
                FOREIGN KEY(AttendanceEntryTypeId) REFERENCES dbo.AttendanceEntryTypes(Id);
            ALTER TABLE dbo.AttendanceRecords ADD CONSTRAINT FK_AttendanceRecords_AttendanceWorkModes
                FOREIGN KEY(AttendanceWorkModeId) REFERENCES dbo.AttendanceWorkModes(Id);
            CREATE INDEX IX_AttendanceRecords_AttendanceEntryTypeId ON dbo.AttendanceRecords(AttendanceEntryTypeId);
            CREATE INDEX IX_AttendanceRecords_AttendanceWorkModeId ON dbo.AttendanceRecords(AttendanceWorkModeId);
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
            AS
            BEGIN
                SET NOCOUNT ON;
                ;WITH Dates AS (
                    SELECT @DateFrom AttendanceDate UNION ALL
                    SELECT DATEADD(day,1,AttendanceDate) FROM Dates WHERE AttendanceDate < @DateTo
                ), VisiblePeople AS (
                    SELECT TRY_CONVERT(uniqueidentifier,[value]) PersonId FROM OPENJSON(@VisiblePersonIds)
                    WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
                )
                SELECT ar.Id,p.PersonId,COALESCE(sv.LoginId,v.VacancyCode) EmployeeNumber,p.FullName EmployeeName,
                    COALESCE(v.Department,org.Name) Department,COALESCE(jt.TitleName,v.JobTitle,N'') Designation,d.AttendanceDate,
                    ar.AttendanceStatusId,st.StatusName,ps.Code StatusCode,cs.ColorCode StatusColorCode,
                    ar.AttendanceEntryTypeId,COALESCE(aet.Name,CASE WHEN ar.Id IS NULL THEN noEntry.Name END) AttendanceEntryType,
                    ar.AttendanceWorkModeId,awm.Name AttendanceWorkMode,
                    ar.CheckInUtc,ar.CheckOutUtc,ar.TotalBreakMinutes,p.ShiftStartTime,p.ShiftEndTime,p.TimeZoneId,p.ReportsToPersonId
                FROM VisiblePeople vp
                JOIN Persons p ON p.PersonId=vp.PersonId AND p.IsActive=1 AND p.TenantId=@TenantId
                JOIN StaffVacancy sv ON sv.PersonId=p.PersonId JOIN Vacancies v ON v.VacancyId=sv.VacancyId
                LEFT JOIN JobTitles jt ON jt.Id=v.JobTitleId LEFT JOIN OrganizationTree org ON org.Id=v.OrganizationId CROSS JOIN Dates d
                LEFT JOIN AttendanceRecords ar ON ar.PersonId=p.PersonId AND ar.AttendanceDate=d.AttendanceDate AND ar.TenantId=@TenantId
                LEFT JOIN ProcessStatusStyles ps ON ps.Id=ar.AttendanceStatusId LEFT JOIN Statuses st ON st.Id=ps.StatusId
                LEFT JOIN ColorStyles cs ON cs.Id=ps.ColorStyleId
                LEFT JOIN AttendanceEntryTypes aet ON aet.Id=ar.AttendanceEntryTypeId
                LEFT JOIN AttendanceEntryTypes noEntry ON noEntry.Code=N'NONE' AND noEntry.IsActive=1
                LEFT JOIN AttendanceWorkModes awm ON awm.Id=ar.AttendanceWorkModeId
                WHERE DATENAME(weekday,d.AttendanceDate) NOT IN(N'Saturday',N'Sunday') OR ar.Id IS NOT NULL
                ORDER BY d.AttendanceDate DESC,p.FullName OPTION(MAXRECURSION 367);
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Rbac_ReplaceStaffAccess
                @StaffIdsJson nvarchar(max), @PermissionsJson nvarchar(max), @GrantedBy nvarchar(450)=NULL
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                BEGIN TRY
                    CREATE TABLE #Staff(StaffId uniqueidentifier NOT NULL PRIMARY KEY);
                    INSERT #Staff SELECT DISTINCT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@StaffIdsJson)
                    WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL;
                    CREATE TABLE #Permission(MenuId int NOT NULL,PermissionId int NOT NULL,IsTopLevel bit NOT NULL,
                        PRIMARY KEY(MenuId,PermissionId));
                    INSERT #Permission(MenuId,PermissionId,IsTopLevel)
                    SELECT MenuId,PermissionId,IsTopLevel FROM OPENJSON(@PermissionsJson)
                    WITH(MenuId int '$.MenuId',PermissionId int '$.PermissionId',IsTopLevel bit '$.IsTopLevel');

                    DELETE sma FROM StaffMenuAccess sma JOIN #Staff s ON s.StaffId=sma.StaffId;
                    INSERT StaffMenuAccess(StaffId,MenuId,IsAllow,GrantedBy,GrantedDate)
                    SELECT s.StaffId,p.MenuId,1,@GrantedBy,SYSUTCDATETIME()
                    FROM #Staff s CROSS JOIN (SELECT DISTINCT MenuId FROM #Permission) p;
                    INSERT AccessFeatures(StaffMenuAccessId,PermissionId,IsAllow)
                    SELECT sma.Id,p.PermissionId,1 FROM StaffMenuAccess sma
                    JOIN #Staff s ON s.StaffId=sma.StaffId JOIN #Permission p ON p.MenuId=sma.MenuId
                    WHERE p.IsTopLevel=0;
                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
                    THROW;
                END CATCH
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Attendance classification history is intentionally preserved.");
}
