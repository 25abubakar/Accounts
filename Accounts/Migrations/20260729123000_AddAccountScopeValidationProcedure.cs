using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260729123000_AddAccountScopeValidationProcedure")]
public sealed class AddAccountScopeValidationProcedure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_AccountScope_ValidateAccess
                @UserId nvarchar(450)
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE
                    @IsSuperAdmin bit,
                    @IsTenantAdmin bit,
                    @TenantId int,
                    @LockoutEnabled bit,
                    @LockoutEnd datetimeoffset,
                    @TenantIsActive bit,
                    @TenantOrganizationTreeId int,
                    @PersonIsActive bit,
                    @EmployeeOrganizationId int,
                    @BlockedNodeName nvarchar(100),
                    @BlockedNodeLabel nvarchar(50);

                SELECT TOP (1)
                    @IsSuperAdmin = ISNULL([IsSuperAdmin], 0),
                    @IsTenantAdmin = ISNULL([IsTenantAdmin], 0),
                    @TenantId = [TenantId],
                    @LockoutEnabled = ISNULL([LockoutEnabled], 0),
                    @LockoutEnd = [LockoutEnd]
                FROM dbo.AspNetUsers
                WHERE [Id] = @UserId;

                IF @IsSuperAdmin IS NULL
                BEGIN
                    SELECT
                        CAST(0 AS bit) AS IsAllowed,
                        CAST(N'ACCOUNT_SCOPE_DISABLED' AS nvarchar(80)) AS Code,
                        CAST(N'This account no longer exists.' AS nvarchar(4000)) AS [Message];
                    RETURN;
                END;

                IF @IsSuperAdmin = 1
                BEGIN
                    SELECT
                        CAST(1 AS bit) AS IsAllowed,
                        CAST(N'OK' AS nvarchar(80)) AS Code,
                        CAST(N'' AS nvarchar(4000)) AS [Message];
                    RETURN;
                END;

                IF @IsTenantAdmin = 0
                   AND @LockoutEnabled = 1
                   AND @LockoutEnd IS NOT NULL
                   AND @LockoutEnd > SYSDATETIMEOFFSET()
                BEGIN
                    SELECT
                        CAST(0 AS bit) AS IsAllowed,
                        CAST(N'ACCOUNT_SCOPE_DISABLED' AS nvarchar(80)) AS Code,
                        CAST(N'This account is disabled or locked.' AS nvarchar(4000)) AS [Message];
                    RETURN;
                END;

                IF @TenantId IS NULL
                BEGIN
                    SELECT
                        CAST(0 AS bit) AS IsAllowed,
                        CAST(N'ACCOUNT_SCOPE_DISABLED' AS nvarchar(80)) AS Code,
                        CAST(N'This account is not assigned to an active tenant.' AS nvarchar(4000)) AS [Message];
                    RETURN;
                END;

                SELECT TOP (1)
                    @TenantIsActive = ISNULL([IsActive], 0),
                    @TenantOrganizationTreeId = [OrganizationTreeId]
                FROM dbo.Tenants
                WHERE [Id] = @TenantId;

                IF @TenantOrganizationTreeId IS NULL OR @TenantIsActive <> 1
                BEGIN
                    SELECT
                        CAST(0 AS bit) AS IsAllowed,
                        CAST(N'ACCOUNT_SCOPE_DISABLED' AS nvarchar(80)) AS Code,
                        CAST(N'Your tenant is currently disabled. Contact your administrator.' AS nvarchar(4000)) AS [Message];
                    RETURN;
                END;

                SELECT TOP (1)
                    @PersonIsActive = ISNULL(person.[IsActive], 0),
                    @EmployeeOrganizationId = vacancy.[OrganizationId]
                FROM dbo.Persons AS person
                LEFT JOIN dbo.StaffVacancy AS staff ON staff.[PersonId] = person.[PersonId]
                LEFT JOIN dbo.Vacancies AS vacancy ON vacancy.[VacancyId] = staff.[VacancyId]
                WHERE person.[IdentityUserId] = @UserId
                ORDER BY person.[CreatedDate] DESC;

                IF @IsTenantAdmin = 0 AND @PersonIsActive = 0
                BEGIN
                    SELECT
                        CAST(0 AS bit) AS IsAllowed,
                        CAST(N'ACCOUNT_SCOPE_DISABLED' AS nvarchar(80)) AS Code,
                        CAST(N'Your staff account is inactive. Contact your administrator.' AS nvarchar(4000)) AS [Message];
                    RETURN;
                END;

                ;WITH SeedNodes AS
                (
                    SELECT @TenantOrganizationTreeId AS [Id]
                    UNION
                    SELECT @EmployeeOrganizationId
                    WHERE @EmployeeOrganizationId IS NOT NULL
                ),
                Ancestors AS
                (
                    SELECT node.[Id], node.[ParentId], node.[IsActive], node.[Name], node.[Label]
                    FROM dbo.OrganizationTree AS node
                    INNER JOIN SeedNodes AS seed ON seed.[Id] = node.[Id]

                    UNION ALL

                    SELECT parent.[Id], parent.[ParentId], parent.[IsActive], parent.[Name], parent.[Label]
                    FROM dbo.OrganizationTree AS parent
                    INNER JOIN Ancestors AS child ON child.[ParentId] = parent.[Id]
                )
                SELECT TOP (1)
                    @BlockedNodeName = blocked.[Name],
                    @BlockedNodeLabel = blocked.[Label]
                FROM
                (
                    SELECT DISTINCT [Id], [Name], [Label]
                    FROM Ancestors
                    WHERE ISNULL([IsActive], 0) = 0
                ) AS blocked
                ORDER BY blocked.[Id]
                OPTION (MAXRECURSION 100);

                IF @BlockedNodeName IS NOT NULL
                BEGIN
                    SELECT
                        CAST(0 AS bit) AS IsAllowed,
                        CAST(N'ACCOUNT_SCOPE_DISABLED' AS nvarchar(80)) AS Code,
                        CAST(CONCAT(N'Access is disabled for ', @BlockedNodeName, N' (', @BlockedNodeLabel, N'). Contact your administrator.') AS nvarchar(4000)) AS [Message];
                    RETURN;
                END;

                SELECT
                    CAST(1 AS bit) AS IsAllowed,
                    CAST(N'OK' AS nvarchar(80)) AS Code,
                    CAST(N'' AS nvarchar(4000)) AS [Message];
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_AccountScope_ValidateAccess;");
    }
}
