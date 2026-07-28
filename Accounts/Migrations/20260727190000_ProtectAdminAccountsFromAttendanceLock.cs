using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727190000_ProtectAdminAccountsFromAttendanceLock")]
public sealed class ProtectAdminAccountsFromAttendanceLock : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Keep Identity flags aligned with role assignments before login/access checks run.
            UPDATE identityUser
               SET IsSuperAdmin = 1
            FROM dbo.AspNetUsers identityUser
            WHERE EXISTS (
                SELECT 1
                FROM dbo.AspNetUserRoles userRole
                JOIN dbo.AspNetRoles role ON role.Id = userRole.RoleId
                WHERE userRole.UserId = identityUser.Id
                  AND role.Name = N'SuperAdmin'
            );

            UPDATE identityUser
               SET IsTenantAdmin = 1
            FROM dbo.AspNetUsers identityUser
            WHERE EXISTS (
                SELECT 1
                FROM dbo.AspNetUserRoles userRole
                JOIN dbo.AspNetRoles role ON role.Id = userRole.RoleId
                WHERE userRole.UserId = identityUser.Id
                  AND role.Name = N'TenantAdmin'
            );

            -- Attendance auto-lock is a staff attendance control only.
            -- SuperAdmin/Admin/TenantAdmin accounts must remain available for management.
            UPDATE identityUser
               SET LockoutEnd = NULL,
                   AccessFailedCount = 0
            FROM dbo.AspNetUsers identityUser
            WHERE ISNULL(identityUser.IsSuperAdmin, 0) = 1
               OR ISNULL(identityUser.IsTenantAdmin, 0) = 1
               OR EXISTS (
                    SELECT 1
                    FROM dbo.AspNetUserRoles userRole
                    JOIN dbo.AspNetRoles role ON role.Id = userRole.RoleId
                    WHERE userRole.UserId = identityUser.Id
                      AND role.Name IN (N'SuperAdmin', N'Admin', N'TenantAdmin')
               );

            UPDATE person
               SET IsActive = 1
            FROM dbo.Persons person
            JOIN dbo.AspNetUsers identityUser
              ON identityUser.Id = person.IdentityUserId
            WHERE ISNULL(identityUser.IsSuperAdmin, 0) = 1
               OR ISNULL(identityUser.IsTenantAdmin, 0) = 1
               OR EXISTS (
                    SELECT 1
                    FROM dbo.AspNetUserRoles userRole
                    JOIN dbo.AspNetRoles role ON role.Id = userRole.RoleId
                    WHERE userRole.UserId = identityUser.Id
                      AND role.Name IN (N'SuperAdmin', N'Admin', N'TenantAdmin')
               );

            DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));
            IF @Definition IS NOT NULL
               AND @Definition NOT LIKE N'%role.Name IN (N''SuperAdmin'',N''Admin'',N''TenantAdmin'')%'
               AND @Definition LIKE N'%AND ISNULL(identityUser.IsTenantAdmin,0)=0%AND ISNULL(identityUser.IsSuperAdmin,0)=0%'
            BEGIN
                SET @Definition = REPLACE(@Definition, N'CREATE PROCEDURE', N'CREATE OR ALTER PROCEDURE');
                SET @Definition = REPLACE(@Definition, N'CREATE   PROCEDURE', N'CREATE OR ALTER PROCEDURE');
                SET @Definition = REPLACE(@Definition, N'CREATE OR ALTER   PROCEDURE', N'CREATE OR ALTER PROCEDURE');
                SET @Definition = REPLACE(
                    @Definition,
                    N'AND ISNULL(identityUser.IsTenantAdmin,0)=0
                      AND ISNULL(identityUser.IsSuperAdmin,0)=0',
                    N'AND ISNULL(identityUser.IsTenantAdmin,0)=0
                      AND ISNULL(identityUser.IsSuperAdmin,0)=0
                      AND NOT EXISTS (
                          SELECT 1
                          FROM dbo.AspNetUserRoles userRole
                          JOIN dbo.AspNetRoles role ON role.Id=userRole.RoleId
                          WHERE userRole.UserId=identityUser.Id
                            AND role.Name IN (N''SuperAdmin'',N''Admin'',N''TenantAdmin'')
                      )'
                );
                EXEC(@Definition);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
