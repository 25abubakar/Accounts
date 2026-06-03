using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgVacancyPersonViewAndProcs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW [dbo].[vw_OrganizationVacancyPersons] AS
SELECT
    org.Id              AS OrganizationId,
    org.Name            AS OrganizationName,
    org.Code            AS OrganizationCode,
    v.VacancyId,
    v.VacancyCode,
    v.JobTitle,
    v.Department,
    v.IsFilled,
    p.PersonId,
    p.FullName,
    p.Email,
    p.Phone
FROM dbo.Vacancies v
INNER JOIN dbo.OrganizationTree org ON org.Id = v.OrganizationId
LEFT JOIN dbo.StaffVacancy sv ON sv.VacancyId = v.VacancyId
LEFT JOIN dbo.Persons p ON p.PersonId = sv.PersonId;
");

            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE [dbo].[usp_GetPersonsByOrgNode_Clean]
    @OrgNodeId INT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH OrgCTE AS
    (
        SELECT
            o.Id,
            o.Name,
            o.Code,
            o.ParentId,
            0 AS OrgLevel
        FROM dbo.OrganizationTree o
        WHERE o.Id = @OrgNodeId

        UNION ALL

        SELECT
            child.Id,
            child.Name,
            child.Code,
            child.ParentId,
            parent.OrgLevel + 1
        FROM dbo.OrganizationTree child
        INNER JOIN OrgCTE parent ON child.ParentId = parent.Id
    )
    SELECT
        org.Id              AS OrganizationId,
        org.Name            AS OrganizationName,
        org.Code            AS OrganizationCode,
        v.VacancyId,
        v.VacancyCode,
        v.JobTitle,
        v.Department,
        v.IsFilled,
        p.PersonId,
        p.FullName,
        p.Email,
        p.Phone
    FROM OrgCTE org
    INNER JOIN dbo.Vacancies v
        ON v.OrganizationId = org.Id
    LEFT JOIN dbo.StaffVacancy sv
        ON sv.VacancyId = v.VacancyId
    LEFT JOIN dbo.Persons p
        ON p.PersonId = sv.PersonId
    ORDER BY
        org.OrgLevel,
        org.Name,
        v.VacancyCode;
END
");

            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE [dbo].[usp_GetEmployeesByOrgAndRole]
    @OrgNodeId INT,
    @JobTitle NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH OrgCTE AS
    (
        SELECT
            o.Id,
            o.Name,
            o.ParentId,
            0 AS OrgLevel
        FROM dbo.OrganizationTree o
        WHERE o.Id = @OrgNodeId

        UNION ALL

        SELECT
            child.Id,
            child.Name,
            child.ParentId,
            parent.OrgLevel + 1
        FROM dbo.OrganizationTree child
        INNER JOIN OrgCTE parent ON child.ParentId = parent.Id
    )
    SELECT
        org.Id              AS OrganizationId,
        org.Name            AS OrganizationName,
        v.VacancyCode,
        v.JobTitle,
        p.FullName,
        p.Email
    FROM OrgCTE org
    INNER JOIN dbo.Vacancies v
        ON v.OrganizationId = org.Id
    INNER JOIN dbo.StaffVacancy sv
        ON sv.VacancyId = v.VacancyId
    INNER JOIN dbo.Persons p
        ON p.PersonId = sv.PersonId
    WHERE (@JobTitle IS NULL OR v.JobTitle = @JobTitle)
    ORDER BY
        org.OrgLevel,
        org.Name,
        p.FullName;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[usp_GetEmployeesByOrgAndRole]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_GetEmployeesByOrgAndRole];
");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[usp_GetPersonsByOrgNode_Clean]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_GetPersonsByOrgNode_Clean];
");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[vw_OrganizationVacancyPersons]', N'V') IS NOT NULL
    DROP VIEW [dbo].[vw_OrganizationVacancyPersons];
");
        }
    }
}
