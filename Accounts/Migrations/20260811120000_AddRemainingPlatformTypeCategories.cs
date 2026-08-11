using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811120000_AddRemainingPlatformTypeCategories")]
public sealed class AddRemainingPlatformTypeCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO dbo.PlatformTypeCategories (Id,Code,Name,Icon,DisplayOrder,IsActive)
            SELECT seed.Id,seed.Code,seed.Name,seed.Icon,seed.DisplayOrder,1
            FROM (VALUES
              (6,N'LEAVE_TYPE',N'Leave Type',N'CalendarDays',6),
              (7,N'ANNOUNCEMENT_TYPE',N'Announcement Type',N'Megaphone',7),
              (8,N'ASSESSMENT_TYPE',N'Assessment Type',N'ClipboardCheck',8),
              (9,N'ATTENDANCE_TYPE',N'Attendance Type',N'Clock3',9),
              (10,N'BENEFITS_TYPE',N'Benefits Type',N'BadgePlus',10),
              (11,N'DESIGNATION',N'Designation',N'BriefcaseBusiness',11)
            ) seed(Id,Code,Name,Icon,DisplayOrder)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.PlatformTypeCategories category WHERE category.Code=seed.Code);

            INSERT INTO dbo.PlatformTypeValues (TenantId,CategoryId,Name,Code,DisplayOrder,IsActive,CreatedOnUtc)
            SELECT tenant.Id,category.Id,seed.Name,seed.Code,seed.DisplayOrder,1,SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            CROSS JOIN (VALUES
              (N'LEAVE_TYPE',N'Casual',N'CASUAL',1),(N'LEAVE_TYPE',N'Annual',N'ANNUAL',2),
              (N'LEAVE_TYPE',N'Sick',N'SICK',3),(N'LEAVE_TYPE',N'Education',N'EDUCATION',4),
              (N'LEAVE_TYPE',N'Short',N'SHORT',5),(N'LEAVE_TYPE',N'Maternity',N'MATERNITY',6),
              (N'LEAVE_TYPE',N'Special',N'SPECIAL',7),(N'LEAVE_TYPE',N'Day Off',N'DAY_OFF',8),
              (N'LEAVE_TYPE',N'Hours',N'HOURS',9),(N'LEAVE_TYPE',N'Over Time',N'OVER_TIME',10),
              (N'LEAVE_TYPE',N'Breaks',N'BREAKS',11),

              (N'ANNOUNCEMENT_TYPE',N'Info',N'INFO',1),(N'ANNOUNCEMENT_TYPE',N'Instructions',N'INSTRUCTIONS',2),
              (N'ANNOUNCEMENT_TYPE',N'Announcements',N'ANNOUNCEMENTS',3),(N'ANNOUNCEMENT_TYPE',N'Reminder',N'REMINDER',4),

              (N'ASSESSMENT_TYPE',N'Monthly Proficiency',N'MONTHLY_PROFICIENCY',1),
              (N'ASSESSMENT_TYPE',N'Annual Proficiency',N'ANNUAL_PROFICIENCY',2),
              (N'ASSESSMENT_TYPE',N'Team Assessment',N'TEAM_ASSESSMENT',3),
              (N'ASSESSMENT_TYPE',N'Customer Assessment',N'CUSTOMER_ASSESSMENT',4),
              (N'ASSESSMENT_TYPE',N'Attendance',N'ATTENDANCE',5),

              (N'ATTENDANCE_TYPE',N'Login',N'LOGIN',1),(N'ATTENDANCE_TYPE',N'Check In/Out',N'CHECK_IN_OUT',2),
              (N'ATTENDANCE_TYPE',N'Machine',N'MACHINE',3),(N'ATTENDANCE_TYPE',N'Camera',N'CAMERA',5),
              (N'ATTENDANCE_TYPE',N'Staff (Guard)',N'STAFF_GUARD',6),(N'ATTENDANCE_TYPE',N'Remote',N'REMOTE',7),
              (N'ATTENDANCE_TYPE',N'System (IP)',N'SYSTEM_IP',8),(N'ATTENDANCE_TYPE',N'Not Required',N'NOT_REQUIRED',9),
              (N'ATTENDANCE_TYPE',N'By Supervisor',N'BY_SUPERVISOR',11),(N'ATTENDANCE_TYPE',N'Test',N'TEST',12),

              (N'BENEFITS_TYPE',N'EOBI',N'EOBI',1),(N'BENEFITS_TYPE',N'Bonus',N'BONUS',2),
              (N'BENEFITS_TYPE',N'Provident Fund',N'PROVIDENT_FUND',3),(N'BENEFITS_TYPE',N'Gratuity',N'GRATUITY',4),
              (N'BENEFITS_TYPE',N'Incentive',N'INCENTIVE',5),(N'BENEFITS_TYPE',N'Entertainment',N'ENTERTAINMENT',6),
              (N'BENEFITS_TYPE',N'Tpt Funding',N'TPT_FUNDING',7),(N'BENEFITS_TYPE',N'Security',N'SECURITY',8),
              (N'BENEFITS_TYPE',N'Loan',N'LOAN',9),(N'BENEFITS_TYPE',N'Tax',N'TAX',10),
              (N'BENEFITS_TYPE',N'Proficiency',N'PROFICIENCY',11),

              (N'DESIGNATION',N'Administrator',N'ADMINISTRATOR',1),(N'DESIGNATION',N'Super Admin',N'SUPER_ADMIN',2),
              (N'DESIGNATION',N'Chairman',N'CHAIRMAN',3),(N'DESIGNATION',N'President',N'PRESIDENT',4),
              (N'DESIGNATION',N'Vice President',N'VICE_PRESIDENT',5),(N'DESIGNATION',N'Director',N'DIRECTOR',6),
              (N'DESIGNATION',N'CEO',N'CEO',7),(N'DESIGNATION',N'Manager',N'MANAGER',8),
              (N'DESIGNATION',N'Finance Officer',N'FINANCE_OFFICER',9),(N'DESIGNATION',N'Security Officer',N'SECURITY_OFFICER',10),
              (N'DESIGNATION',N'Supervisor',N'SUPERVISOR',11),(N'DESIGNATION',N'Assistant Supervisor',N'ASSISTANT_SUPERVISOR',12),
              (N'DESIGNATION',N'Agent',N'AGENT',14),(N'DESIGNATION',N'Member',N'MEMBER',18),
              (N'DESIGNATION',N'Assistant Manager',N'ASSISTANT_MANAGER',19),(N'DESIGNATION',N'Bell Boy',N'BELL_BOY',20),
              (N'DESIGNATION',N'Duty CEO',N'DUTY_CEO',26),(N'DESIGNATION',N'Deputy Manager',N'DEPUTY_MANAGER',55)
            ) seed(CategoryCode,Name,Code,DisplayOrder)
            JOIN dbo.PlatformTypeCategories category ON category.Code=seed.CategoryCode
            WHERE NOT EXISTS (
              SELECT 1 FROM dbo.PlatformTypeValues existing
              WHERE existing.TenantId=tenant.Id AND existing.CategoryId=category.Id AND existing.Code=seed.Code
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE value FROM dbo.PlatformTypeValues value
            JOIN dbo.PlatformTypeCategories category ON category.Id=value.CategoryId
            WHERE category.Code IN (N'LEAVE_TYPE',N'ANNOUNCEMENT_TYPE',N'ASSESSMENT_TYPE',N'ATTENDANCE_TYPE',N'BENEFITS_TYPE',N'DESIGNATION');

            DELETE FROM dbo.PlatformTypeCategories
            WHERE Code IN (N'LEAVE_TYPE',N'ANNOUNCEMENT_TYPE',N'ASSESSMENT_TYPE',N'ATTENDANCE_TYPE',N'BENEFITS_TYPE',N'DESIGNATION');
            """);
    }
}
