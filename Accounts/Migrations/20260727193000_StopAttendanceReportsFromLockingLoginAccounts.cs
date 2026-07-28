using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727193000_StopAttendanceReportsFromLockingLoginAccounts")]
public sealed class StopAttendanceReportsFromLockingLoginAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Emergency repair: attendance report evaluation accidentally used the same
            -- Identity lock columns used by real account-disable workflows. Restore
            -- login for users that belong to active tenants; disabled tenants stay blocked.
            UPDATE identityUser
               SET LockoutEnd = NULL,
                   AccessFailedCount = 0
            FROM dbo.AspNetUsers identityUser
            LEFT JOIN dbo.Tenants tenant
              ON tenant.Id = identityUser.TenantId
            WHERE identityUser.LockoutEnd IS NOT NULL
              AND (identityUser.TenantId IS NULL OR tenant.IsActive = 1);

            UPDATE person
               SET IsActive = 1
            FROM dbo.Persons person
            JOIN dbo.AspNetUsers identityUser
              ON identityUser.Id = person.IdentityUserId
            LEFT JOIN dbo.Tenants tenant
              ON tenant.Id = identityUser.TenantId
            WHERE person.IsActive = 0
              AND (identityUser.TenantId IS NULL OR tenant.IsActive = 1);

            -- Keep admin flags aligned with roles so admin bypass checks remain correct.
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

            -- Patch already-installed evaluator procedure. It may exist from older
            -- migrations, so edit it in-place without hardcoding any attendance labels.
            DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));
            IF @Definition IS NOT NULL
            BEGIN
                SET @Definition = REPLACE(@Definition, N'CREATE PROCEDURE', N'CREATE OR ALTER PROCEDURE');
                SET @Definition = REPLACE(@Definition, N'CREATE   PROCEDURE', N'CREATE OR ALTER PROCEDURE');
                SET @Definition = REPLACE(@Definition, N'CREATE OR ALTER   PROCEDURE', N'CREATE OR ALTER PROCEDURE');

                IF @Definition NOT LIKE N'%Attendance reports must not directly lock application login accounts.%'
                BEGIN
                    SET @Definition = REPLACE(
                        @Definition,
                        N'AND setting.AccountLockAbsentDays>0',
                        N'AND 1=0 -- Attendance reports must not directly lock application login accounts.
                      AND setting.AccountLockAbsentDays>0'
                    );

                    EXEC(@Definition);
                END
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
