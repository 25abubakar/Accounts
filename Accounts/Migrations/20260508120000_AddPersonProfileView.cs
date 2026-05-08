using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonProfileView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW dbo.vw_PersonProfiles AS
SELECT
    -- Person core
    p.PersonId,
    p.LoginId,
    p.FullName,
    p.Gender,
    p.DateOfBirth,
    p.MaritalStatus,
    p.Phone,
    p.Email,
    p.ProfilePhotoUrl,
    p.CreatedDate,
    p.BranchId,

    -- Org placement (Branch → Company → Country)
    branch.Name        AS BranchName,
    company.Name       AS CompanyName,
    country.Name       AS CountryName,
    country.FlagUrl    AS CountryFlag,

    -- Staff / Position info (NULL if not hired)
    s.StaffId,
    s.JoiningDate,
    v.VacancyId,
    v.VacancyCode,
    v.JobTitle,
    v.Department,
    CAST(CASE WHEN s.StaffId IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsHired,

    -- Current address
    ca.AddressLine  AS CurrentAddressLine,
    ca.Country      AS CurrentCountry,
    ca.Province     AS CurrentProvince,
    ca.District     AS CurrentDistrict,
    ca.City         AS CurrentCity,
    ca.PostalCode   AS CurrentPostalCode,

    -- Permanent address
    pa.AddressLine  AS PermanentAddressLine,
    pa.Country      AS PermanentCountry,
    pa.Province     AS PermanentProvince,
    pa.District     AS PermanentDistrict,
    pa.City         AS PermanentCity,
    pa.PostalCode   AS PermanentPostalCode

FROM dbo.Persons p

-- Org chain
LEFT JOIN dbo.OrganizationTree branch  ON branch.Id  = p.BranchId
LEFT JOIN dbo.OrganizationTree company ON company.Id = branch.ParentId
LEFT JOIN dbo.OrganizationTree country ON country.Id = company.ParentId

-- Staff & Vacancy (person may not be hired yet)
LEFT JOIN dbo.Staff   s ON s.PersonId  = p.PersonId
LEFT JOIN dbo.Vacancies v ON v.VacancyId = s.VacancyId

-- Addresses
LEFT JOIN dbo.PersonAddresses ca ON ca.PersonId = p.PersonId AND ca.AddressType = 'Current'
LEFT JOIN dbo.PersonAddresses pa ON pa.PersonId = p.PersonId AND pa.AddressType = 'Permanent';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_PersonProfiles;");
        }
    }
}
