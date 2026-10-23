namespace Accounts.Migrations;

public partial class PreserveImportedStaffIdentifiers
{
    private const string StaffListProcedure = """
        CREATE OR ALTER PROCEDURE dbo.usp_Hr_Staff_List @TenantId INT
        AS
        BEGIN
            SET NOCOUNT ON;
            ;WITH OrgClimb AS
            (
                SELECT o.Id AS StartId,o.Id,o.ParentId,o.Name,o.Label,0 AS Depth FROM dbo.OrganizationTree o
                UNION ALL
                SELECT c.StartId,p.Id,p.ParentId,p.Name,p.Label,c.Depth+1
                FROM OrgClimb c JOIN dbo.OrganizationTree p ON p.Id=c.ParentId WHERE c.Depth<20
            ), OrgLabels AS
            (
                SELECT StartId,
                    MAX(CASE WHEN Label IN(N'Department',N'Sub Department',N'SubDepartment',N'Unit',N'Team',N'Section') THEN Name END) AS DepartmentName,
                    MAX(CASE WHEN Label IN(N'Branch',N'Sub Branch',N'SubBranch',N'Office') THEN Name END) AS BranchName,
                    MAX(CASE WHEN Label=N'Company' THEN Name END) AS CompanyName,
                    MAX(CASE WHEN Label=N'Country' THEN Name END) AS CountryName,
                    MAX(CASE WHEN Label=N'Group' THEN Name END) AS GroupName
                FROM OrgClimb GROUP BY StartId
            )
            SELECT sv.StaffId,sv.LegacyStaffId,sv.LegacyVacancyId,sv.LegacyServiceStatusId,
                sv.PersonId,per.FullName,per.Email,per.Phone,per.ProfilePhotoUrl AS PhotoUrl,
                CAST(per.IsActive AS BIT) AS IsActive,sv.LoginId,sv.VacancyId,
                COALESCE(vac.VacancyCode,per.LastVacancyCode) AS VacancyCode,
                COALESCE(des.TitleName,vac.JobTitle,per.LastJobTitle,N'') AS Designation,
                COALESCE(vac.Department,per.LastDepartment,ol.DepartmentName,org.Name,N'') AS Department,
                COALESCE(ol.BranchName,per.LastBranchName) AS BranchName,
                COALESCE(ol.CompanyName,per.LastCompanyName) AS CompanyName,
                COALESCE(ol.CountryName,per.LastCountryName) AS CountryName,ol.GroupName,
                per.ShiftStartTime,per.ShiftEndTime,
                CASE WHEN sv.LegacyStaffId IS NOT NULL THEN hr.JoiningDate
                    ELSE COALESCE(hr.JoiningDate,per.LastJoiningDate,per.CreatedDate) END AS JoiningDate
            FROM dbo.StaffVacancy sv
            JOIN dbo.Persons per ON per.PersonId=sv.PersonId AND per.TenantId=sv.TenantId
            LEFT JOIN dbo.Vacancies vac ON vac.VacancyId=sv.VacancyId AND vac.TenantId=sv.TenantId
            LEFT JOIN dbo.JobTitles des ON des.Id=vac.JobTitleId
            LEFT JOIN dbo.OrganizationTree org ON org.Id=COALESCE(vac.OrganizationId,per.LastOrganizationId)
            LEFT JOIN OrgLabels ol ON ol.StartId=org.Id
            LEFT JOIN dbo.PersonHrProfiles hr ON hr.PersonId=per.PersonId AND hr.TenantId=sv.TenantId
            WHERE sv.TenantId=@TenantId ORDER BY per.FullName;
        END
        """;
}
