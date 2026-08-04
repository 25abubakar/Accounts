using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260803190000_HardenTenantIsolation")]
public sealed class HardenTenantIsolation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SecurityAuditLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: true),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                Method = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                Path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                StatusCode = table.Column<int>(type: "int", nullable: false),
                Succeeded = table.Column<bool>(type: "bit", nullable: false),
                RemoteIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedOnUtc = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SecurityAuditLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_SecurityAuditLogs_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditLogs_TenantId_CreatedOnUtc",
            table: "SecurityAuditLogs",
            columns: new[] { "TenantId", "CreatedOnUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditLogs_UserId_CreatedOnUtc",
            table: "SecurityAuditLogs",
            columns: new[] { "UserId", "CreatedOnUtc" });

        foreach (var column in new[] { "CanAdd", "CanEdit", "CanDelete" })
        {
            migrationBuilder.AlterColumn<bool>(
                name: column,
                table: "TenantMenuPermissions",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);
        }

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.OrganizationTreeIdSequence', N'SO') IS NULL
            BEGIN
                DECLARE @StartValue INT =
                    ISNULL((SELECT MAX(Id) + 1 FROM dbo.OrganizationTree), 1);
                DECLARE @CreateSequenceSql NVARCHAR(MAX) =
                    N'CREATE SEQUENCE dbo.OrganizationTreeIdSequence AS INT START WITH '
                    + CONVERT(NVARCHAR(20), @StartValue)
                    + N' INCREMENT BY 1 CACHE 50;';
                EXEC sys.sp_executesql @CreateSequenceSql;
            END;

            UPDATE dbo.TenantMenuPermissions
            SET CanAdd = 0, CanEdit = 0, CanDelete = 0
            WHERE CanView = 0 AND (CanAdd = 1 OR CanEdit = 1 OR CanDelete = 1);

            IF NOT EXISTS
            (
                SELECT 1 FROM sys.check_constraints
                WHERE name = N'CK_TenantMenuPermissions_ActionsRequireView'
            )
                ALTER TABLE dbo.TenantMenuPermissions WITH CHECK
                    ADD CONSTRAINT CK_TenantMenuPermissions_ActionsRequireView
                    CHECK (CanView = 1 OR (CanAdd = 0 AND CanEdit = 0 AND CanDelete = 0));
            """);

        migrationBuilder.Sql(
            """
            IF EXISTS
            (
                SELECT 1
                FROM dbo.AttendanceRecords record
                LEFT JOIN dbo.Tenants tenant ON tenant.Id = record.TenantId
                WHERE tenant.Id IS NULL
            )
                THROW 51010, 'AttendanceRecords contains orphan TenantId values. Repair data before applying tenant isolation.', 1;

            IF NOT EXISTS
            (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_AttendanceRecords_Tenants_TenantId'
            )
            BEGIN
                ALTER TABLE dbo.AttendanceRecords WITH CHECK
                    ADD CONSTRAINT FK_AttendanceRecords_Tenants_TenantId
                    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id);
                ALTER TABLE dbo.AttendanceRecords
                    CHECK CONSTRAINT FK_AttendanceRecords_Tenants_TenantId;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE [dbo].[usp_GetPersonsByOrgNode_Clean]
                @TenantId INT,
                @OrgNodeId INT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @TenantRootId INT =
                    (SELECT OrganizationTreeId FROM dbo.Tenants WHERE Id = @TenantId AND IsActive = 1);
                IF @TenantRootId IS NULL
                    THROW 51011, 'Tenant is missing or inactive.', 1;

                DECLARE @IsInScope BIT = 0;
                ;WITH Ancestors AS
                (
                    SELECT Id, ParentId FROM dbo.OrganizationTree WHERE Id = @OrgNodeId
                    UNION ALL
                    SELECT parent.Id, parent.ParentId
                    FROM dbo.OrganizationTree parent
                    INNER JOIN Ancestors child ON child.ParentId = parent.Id
                )
                SELECT @IsInScope = CASE WHEN EXISTS
                    (SELECT 1 FROM Ancestors WHERE Id = @TenantRootId)
                    THEN 1 ELSE 0 END;

                IF @IsInScope = 0
                    THROW 51012, 'Organization node is outside tenant scope.', 1;

                ;WITH OrgCTE AS
                (
                    SELECT o.Id, o.Name, o.Code, o.ParentId, 0 AS OrgLevel
                    FROM dbo.OrganizationTree o WHERE o.Id = @OrgNodeId
                    UNION ALL
                    SELECT child.Id, child.Name, child.Code, child.ParentId, parent.OrgLevel + 1
                    FROM dbo.OrganizationTree child
                    INNER JOIN OrgCTE parent ON child.ParentId = parent.Id
                )
                SELECT
                    org.Id AS OrganizationId,
                    org.Name AS OrganizationName,
                    org.Code AS OrganizationCode,
                    v.VacancyId,
                    v.VacancyCode,
                    COALESCE(jt.TitleName, v.JobTitle, N'') AS JobTitle,
                    v.Department,
                    v.IsFilled,
                    p.PersonId,
                    p.FullName,
                    p.Email,
                    p.Phone
                FROM OrgCTE org
                INNER JOIN dbo.Vacancies v
                    ON v.OrganizationId = org.Id AND v.TenantId = @TenantId
                LEFT JOIN dbo.JobTitles jt
                    ON jt.Id = v.JobTitleId AND jt.TenantId = @TenantId
                LEFT JOIN dbo.StaffVacancy sv
                    ON sv.VacancyId = v.VacancyId AND sv.TenantId = @TenantId
                LEFT JOIN dbo.Persons p
                    ON p.PersonId = sv.PersonId AND p.TenantId = @TenantId
                ORDER BY org.OrgLevel, org.Name, v.VacancyCode;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE [dbo].[usp_GetEmployeesByOrgAndRole]
                @TenantId INT,
                @OrgNodeId INT,
                @JobTitle NVARCHAR(100) = NULL
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @TenantRootId INT =
                    (SELECT OrganizationTreeId FROM dbo.Tenants WHERE Id = @TenantId AND IsActive = 1);
                IF @TenantRootId IS NULL
                    THROW 51011, 'Tenant is missing or inactive.', 1;

                DECLARE @IsInScope BIT = 0;
                ;WITH Ancestors AS
                (
                    SELECT Id, ParentId FROM dbo.OrganizationTree WHERE Id = @OrgNodeId
                    UNION ALL
                    SELECT parent.Id, parent.ParentId
                    FROM dbo.OrganizationTree parent
                    INNER JOIN Ancestors child ON child.ParentId = parent.Id
                )
                SELECT @IsInScope = CASE WHEN EXISTS
                    (SELECT 1 FROM Ancestors WHERE Id = @TenantRootId)
                    THEN 1 ELSE 0 END;

                IF @IsInScope = 0
                    THROW 51012, 'Organization node is outside tenant scope.', 1;

                ;WITH OrgCTE AS
                (
                    SELECT o.Id, o.Name, o.ParentId, 0 AS OrgLevel
                    FROM dbo.OrganizationTree o WHERE o.Id = @OrgNodeId
                    UNION ALL
                    SELECT child.Id, child.Name, child.ParentId, parent.OrgLevel + 1
                    FROM dbo.OrganizationTree child
                    INNER JOIN OrgCTE parent ON child.ParentId = parent.Id
                )
                SELECT
                    org.Id AS OrganizationId,
                    org.Name AS OrganizationName,
                    v.VacancyCode,
                    COALESCE(jt.TitleName, v.JobTitle, N'') AS JobTitle,
                    p.FullName,
                    p.Email
                FROM OrgCTE org
                INNER JOIN dbo.Vacancies v
                    ON v.OrganizationId = org.Id AND v.TenantId = @TenantId
                LEFT JOIN dbo.JobTitles jt
                    ON jt.Id = v.JobTitleId AND jt.TenantId = @TenantId
                INNER JOIN dbo.StaffVacancy sv
                    ON sv.VacancyId = v.VacancyId AND sv.TenantId = @TenantId
                INNER JOIN dbo.Persons p
                    ON p.PersonId = sv.PersonId AND p.TenantId = @TenantId
                WHERE @JobTitle IS NULL
                   OR COALESCE(jt.TitleName, v.JobTitle, N'') = @JobTitle
                ORDER BY org.OrgLevel, org.Name, p.FullName;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SecurityAuditLogs");
        foreach (var column in new[] { "CanAdd", "CanEdit", "CanDelete" })
        {
            migrationBuilder.AlterColumn<bool>(
                name: column,
                table: "TenantMenuPermissions",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);
        }
        migrationBuilder.Sql(
            """
            IF EXISTS
            (
                SELECT 1 FROM sys.check_constraints
                WHERE name = N'CK_TenantMenuPermissions_ActionsRequireView'
            )
                ALTER TABLE dbo.TenantMenuPermissions
                    DROP CONSTRAINT CK_TenantMenuPermissions_ActionsRequireView;
            """);

        migrationBuilder.Sql(
            """
            IF EXISTS
            (
                SELECT 1 FROM sys.foreign_keys
                WHERE name = N'FK_AttendanceRecords_Tenants_TenantId'
            )
                ALTER TABLE dbo.AttendanceRecords
                    DROP CONSTRAINT FK_AttendanceRecords_Tenants_TenantId;
            """);

        // Keep tenant-scoped procedure definitions during rollback. Reintroducing
        // unscoped operational procedures would be a security regression.
    }
}
