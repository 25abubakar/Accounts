using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <summary>
    /// Keeps the organization employee read model aligned with normalized job titles.
    /// Vacancies.JobTitle is a nullable legacy column; current titles live in JobTitles.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260714180000_FixOrganizationEmployeeQueryJobTitles")]
    public sealed class FixOrganizationEmployeeQueryJobTitles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW [dbo].[vw_OrganizationVacancyPersons] AS
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
FROM dbo.Vacancies v
INNER JOIN dbo.OrganizationTree org ON org.Id = v.OrganizationId
LEFT JOIN dbo.JobTitles jt ON jt.Id = v.JobTitleId
LEFT JOIN dbo.StaffVacancy sv ON sv.VacancyId = v.VacancyId
LEFT JOIN dbo.Persons p ON p.PersonId = sv.PersonId;");

            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE [dbo].[usp_GetPersonsByOrgNode_Clean]
    @OrgNodeId INT
AS
BEGIN
    SET NOCOUNT ON;
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
    INNER JOIN dbo.Vacancies v ON v.OrganizationId = org.Id
    LEFT JOIN dbo.JobTitles jt ON jt.Id = v.JobTitleId
    LEFT JOIN dbo.StaffVacancy sv ON sv.VacancyId = v.VacancyId
    LEFT JOIN dbo.Persons p ON p.PersonId = sv.PersonId
    ORDER BY org.OrgLevel, org.Name, v.VacancyCode;
END;");

            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE [dbo].[usp_GetEmployeesByOrgAndRole]
    @OrgNodeId INT,
    @JobTitle NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
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
    INNER JOIN dbo.Vacancies v ON v.OrganizationId = org.Id
    LEFT JOIN dbo.JobTitles jt ON jt.Id = v.JobTitleId
    INNER JOIN dbo.StaffVacancy sv ON sv.VacancyId = v.VacancyId
    INNER JOIN dbo.Persons p ON p.PersonId = sv.PersonId
    WHERE @JobTitle IS NULL
       OR COALESCE(jt.TitleName, v.JobTitle, N'') = @JobTitle
    ORDER BY org.OrgLevel, org.Name, p.FullName;
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous definitions read the nullable legacy JobTitle column and
            // caused runtime materialization failures. Reverting would restore the bug.
        }
    }
}
