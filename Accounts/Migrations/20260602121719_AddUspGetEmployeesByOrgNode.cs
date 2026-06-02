using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddUspGetEmployeesByOrgNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE [dbo].[usp_GetEmployeesByOrgNode]
    @OrgNodeId INT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH OrgCTE AS
    (
        SELECT
            o.Id,
            o.Name,
            o.Label,
            o.ParentId,
            o.Code,
            o.FlagUrl,
            0 AS OrgLevel,
            CAST(o.Name AS NVARCHAR(MAX)) AS OrgPath
        FROM dbo.OrganizationTree o
        WHERE o.Id = @OrgNodeId

        UNION ALL

        SELECT
            child.Id,
            child.Name,
            child.Label,
            child.ParentId,
            child.Code,
            child.FlagUrl,
            parent.OrgLevel + 1,
            CAST(parent.OrgPath + N' > ' + child.Name AS NVARCHAR(MAX))
        FROM dbo.OrganizationTree child
        INNER JOIN OrgCTE parent ON child.ParentId = parent.Id
    )
    SELECT
        org.Id          AS OrgNodeId,
        org.Name        AS OrgNodeName,
        org.Label       AS OrgLabel,
        org.OrgLevel,
        org.OrgPath,
        org.ParentId,
        parent.Name     AS ParentName,

        sv.StaffId,
        sv.LoginId,

        v.VacancyId,
        v.VacancyCode,
        v.JobTitle,
        v.Department,
        v.IsFilled,
        v.CreatedDate   AS VacancyCreatedDate,

        p.PersonId,
        p.FullName,
        p.Email         AS CompanyEmail,
        p.PersonalEmail,
        p.Phone,
        p.Gender,
        p.DateOfBirth,
        p.ProfilePhotoUrl,
        p.CreatedDate   AS PersonCreatedDate,

        branch.Id       AS BranchId,
        branch.Name     AS BranchName,
        company.Id      AS CompanyId,
        company.Name    AS CompanyName,
        country.Id      AS CountryId,
        country.Name    AS CountryName,
        country.FlagUrl AS CountryFlag
    FROM OrgCTE org
    INNER JOIN dbo.Vacancies v
        ON v.OrganizationId = org.Id
    INNER JOIN dbo.StaffVacancy sv
        ON sv.VacancyId = v.VacancyId
    LEFT JOIN dbo.Persons p
        ON p.PersonId = sv.PersonId
    LEFT JOIN dbo.OrganizationTree parent
        ON parent.Id = org.ParentId
    LEFT JOIN dbo.OrganizationTree branch
        ON branch.Id = v.OrganizationId
    LEFT JOIN dbo.OrganizationTree company
        ON company.Id = branch.ParentId
    LEFT JOIN dbo.OrganizationTree country
        ON country.Id = company.ParentId
    ORDER BY
        org.OrgLevel,
        org.Name,
        p.FullName,
        sv.StaffId;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[usp_GetEmployeesByOrgNode]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[usp_GetEmployeesByOrgNode];
");
        }
    }
}
