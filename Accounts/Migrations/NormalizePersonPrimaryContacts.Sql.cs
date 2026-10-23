namespace Accounts.Migrations;

public partial class NormalizePersonPrimaryContacts
{
    private static readonly string[] NormalizedSql =
    [
        """
        CREATE OR ALTER VIEW dbo.vw_PersonPrimaryContacts AS
        WITH Ranked AS
        (
            SELECT PersonId,ContactType,ContactValue,
                ROW_NUMBER() OVER(PARTITION BY PersonId,ContactType ORDER BY IsPrimary DESC,Id) AS Position
            FROM dbo.PersonContacts
            WHERE ContactType IN(N'Email',N'Phone') AND NULLIF(LTRIM(RTRIM(ContactValue)),N'') IS NOT NULL
        )
        SELECT PersonId,
            MAX(CASE WHEN ContactType=N'Email' AND Position=1 THEN ContactValue END) AS Email,
            MAX(CASE WHEN ContactType=N'Phone' AND Position=1 THEN ContactValue END) AS Phone
        FROM Ranked WHERE Position=1 GROUP BY PersonId;
        """,
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
                contact.Email
            FROM OrgCTE org
            INNER JOIN dbo.Vacancies v
                ON v.OrganizationId = org.Id AND v.TenantId = @TenantId
            LEFT JOIN dbo.JobTitles jt
                ON jt.Id = v.JobTitleId AND jt.TenantId = @TenantId
            INNER JOIN dbo.StaffVacancy sv
                ON sv.VacancyId = v.VacancyId AND sv.TenantId = @TenantId
            INNER JOIN dbo.Persons p
                ON p.PersonId = sv.PersonId AND p.TenantId = @TenantId
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=p.PersonId
            WHERE @JobTitle IS NULL
               OR COALESCE(jt.TitleName, v.JobTitle, N'') = @JobTitle
            ORDER BY org.OrgLevel, org.Name, p.FullName;
        END;
        """,
        """
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
                contact.Email         AS CompanyEmail,
                p.PersonalEmail,
                contact.Phone,
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
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=p.PersonId
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
        """,
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
                contact.Email,
                contact.Phone
            FROM OrgCTE org
            INNER JOIN dbo.Vacancies v
                ON v.OrganizationId = org.Id AND v.TenantId = @TenantId
            LEFT JOIN dbo.JobTitles jt
                ON jt.Id = v.JobTitleId AND jt.TenantId = @TenantId
            LEFT JOIN dbo.StaffVacancy sv
                ON sv.VacancyId = v.VacancyId AND sv.TenantId = @TenantId
            LEFT JOIN dbo.Persons p
                ON p.PersonId = sv.PersonId AND p.TenantId = @TenantId
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=p.PersonId
            ORDER BY org.OrgLevel, org.Name, v.VacancyCode;
        END;
        """,
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Hr_Persons_List
            @TenantId INT,
            @Mode NVARCHAR(20) = N'ALL', -- ALL | UNASSIGNED | FORMER
            @PersonId UNIQUEIDENTIFIER = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
        
            ;WITH OrgClimb AS
            (
                SELECT o.Id AS StartId, o.Id, o.ParentId, o.Name, o.Label, 0 AS Depth
                FROM dbo.OrganizationTree o
                UNION ALL
                SELECT c.StartId, p.Id, p.ParentId, p.Name, p.Label, c.Depth + 1
                FROM OrgClimb c
                INNER JOIN dbo.OrganizationTree p ON p.Id = c.ParentId
                WHERE c.Depth < 20
            ),
            OrgLabels AS
            (
                SELECT
                    StartId,
                    MAX(CASE WHEN Label IN (N'Department', N'Sub Department', N'SubDepartment', N'Unit', N'Team', N'Section') THEN Name END) AS DepartmentName,
                    MAX(CASE WHEN Label IN (N'Branch', N'Sub Branch', N'SubBranch', N'Office') THEN Name END) AS BranchName,
                    MAX(CASE WHEN Label IN (N'Branch', N'Sub Branch', N'SubBranch', N'Office') THEN Id END) AS BranchId,
                    MAX(CASE WHEN Label = N'Company' THEN Name END) AS CompanyName,
                    MAX(CASE WHEN Label = N'Country' THEN Name END) AS CountryName
                FROM OrgClimb
                GROUP BY StartId
            )
            SELECT
                p.PersonId, p.LegacyPersonId, sv.StaffId, sv.VacancyId,
                COALESCE(sv.LoginId, N'-') AS LoginId,
                p.FullName,
                p.Gender,
                p.DateOfBirth,
                p.MaritalStatus,
                contact.Phone,
                contact.Email,
                p.PersonalEmail,
                p.ShiftStartTime,
                p.ShiftEndTime,
                p.TimeZoneId,
                p.ProfilePhotoUrl AS PhotoUrl,
                CAST(CASE WHEN sv.StaffId IS NULL THEN 0 ELSE 1 END AS BIT) AS IsHired,
                CAST(p.IsActive AS BIT) AS IsActive,
                p.EmploymentStatus,
                p.TerminationDateUtc,
                p.TerminationReason,
                CONVERT(NVARCHAR(40), p.CreatedDate, 127) AS RegisteredAt,
                p.LastJoiningDate AS JoiningDate,
                COALESCE(ol.BranchId, CAST(NULL AS INT)) AS BranchId,
                COALESCE(ol.BranchName, p.LastBranchName) AS BranchName,
                COALESCE(ol.CompanyName, p.LastCompanyName) AS CompanyName,
                COALESCE(ol.CountryName, p.LastCountryName) AS CountryName,
                COALESCE(vac.VacancyCode, p.LastVacancyCode) AS VacancyCode,
                COALESCE(des.TitleName, vac.JobTitle, p.LastJobTitle) AS JobTitle,
                COALESCE(vac.Department, ol.DepartmentName, p.LastDepartment) AS Department,
                cur.AddressLine AS CurrentAddressLine,
                cur.Country AS CurrentCountry,
                cur.Province AS CurrentProvince,cur.District AS CurrentDistrict,cur.PostalCode AS CurrentPostalCode,
                cur.City AS CurrentCity,
                perm.AddressId AS PermanentAddressId,perm.AddressLine AS PermanentAddressLine,
                perm.Country AS PermanentCountry,
                perm.Province AS PermanentProvince,perm.District AS PermanentDistrict,perm.PostalCode AS PermanentPostalCode,
                perm.City AS PermanentCity
            FROM dbo.Persons p
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=p.PersonId
            LEFT JOIN dbo.StaffVacancy sv ON sv.PersonId = p.PersonId AND sv.TenantId = p.TenantId
            LEFT JOIN dbo.Vacancies vac ON vac.VacancyId = sv.VacancyId
            LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
            LEFT JOIN OrgLabels ol ON ol.StartId = vac.OrganizationId
            LEFT JOIN dbo.PersonAddresses cur ON cur.PersonId = p.PersonId AND cur.AddressType = N'Current'
            LEFT JOIN dbo.PersonAddresses perm ON perm.PersonId = p.PersonId AND perm.AddressType = N'Permanent'
            WHERE p.TenantId = @TenantId AND (@PersonId IS NULL OR p.PersonId=@PersonId)
              AND (
                    (@Mode = N'ALL')
                    OR (@Mode = N'UNASSIGNED' AND sv.StaffId IS NULL)
                    OR (@Mode = N'FORMER' AND p.EmploymentStatus IN (N'Fired', N'Retired'))
                  )
            ORDER BY
                CASE WHEN @Mode = N'FORMER' THEN p.TerminationDateUtc END DESC,
                p.CreatedDate DESC;
        END
        """,
        """
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
                sv.PersonId,per.FullName,contact.Email,contact.Phone,per.ProfilePhotoUrl AS PhotoUrl,
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
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=per.PersonId
            LEFT JOIN dbo.Vacancies vac ON vac.VacancyId=sv.VacancyId AND vac.TenantId=sv.TenantId
            LEFT JOIN dbo.JobTitles des ON des.Id=vac.JobTitleId
            LEFT JOIN dbo.OrganizationTree org ON org.Id=COALESCE(vac.OrganizationId,per.LastOrganizationId)
            LEFT JOIN OrgLabels ol ON ol.StartId=org.Id
            LEFT JOIN dbo.PersonHrProfiles hr ON hr.PersonId=per.PersonId AND hr.TenantId=sv.TenantId
            WHERE sv.TenantId=@TenantId ORDER BY per.FullName;
        END
        """,
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Hr_Vacancies_List
            @TenantId INT,
            @FilledFilter NVARCHAR(10) = NULL -- NULL=all, '1'=filled, '0'=open
        AS
        BEGIN
            SET NOCOUNT ON;
        
            ;WITH OrgClimb AS
            (
                SELECT o.Id AS StartId, o.Id, o.ParentId, o.Name, o.Label, 0 AS Depth
                FROM dbo.OrganizationTree o
                UNION ALL
                SELECT c.StartId, p.Id, p.ParentId, p.Name, p.Label, c.Depth + 1
                FROM OrgClimb c
                INNER JOIN dbo.OrganizationTree p ON p.Id = c.ParentId
                WHERE c.Depth < 20
            ),
            OrgLabels AS
            (
                SELECT
                    StartId,
                    MAX(CASE WHEN Label IN (N'Department', N'Sub Department', N'SubDepartment', N'Unit', N'Team', N'Section') THEN Name END) AS DepartmentName,
                    MAX(CASE WHEN Depth = 0 THEN Name END) AS NodeName,
                    MAX(CASE WHEN Depth = 0 THEN Label END) AS NodeLabel,
                    MAX(CASE WHEN Label = N'Company' THEN Name END) AS CompanyName,
                    MAX(CASE WHEN Label = N'Country' THEN Name END) AS CountryName,
                    MAX(CASE WHEN Label IN (N'Branch', N'Sub Branch', N'SubBranch', N'Office') THEN Name END) AS BranchName
                FROM OrgClimb
                GROUP BY StartId
            )
            SELECT
                vac.VacancyId,
                vac.OrganizationId,
                COALESCE(ol.BranchName, ol.NodeName, N'-') AS BranchName,
                COALESCE(ol.CompanyName, N'-') AS CompanyName,
                COALESCE(ol.CountryName, N'-') AS CountryName,
                COALESCE(ol.NodeLabel, N'-') AS NodeLabel,
                vac.VacancyCode,
                vac.JobTitleId AS DesignationId,
                COALESCE(des.TitleName, vac.JobTitle, N'') AS Designation,
                COALESCE(vac.Department, ol.DepartmentName, ol.NodeName) AS Department,
                vac.IsFilled,
                vac.CreatedDate,
                sv.StaffId AS EmployeeStaffId,
                per.FullName AS EmployeeFullName,
                contact.Email AS EmployeeEmail,
                contact.Phone AS EmployeePhone,
                per.ProfilePhotoUrl AS EmployeePhotoUrl
            FROM dbo.Vacancies vac
            LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
            LEFT JOIN OrgLabels ol ON ol.StartId = vac.OrganizationId
            LEFT JOIN dbo.StaffVacancy sv ON sv.VacancyId = vac.VacancyId AND sv.TenantId = vac.TenantId
            LEFT JOIN dbo.Persons per ON per.PersonId = sv.PersonId
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=per.PersonId
            WHERE vac.TenantId = @TenantId
              AND (
                    @FilledFilter IS NULL
                    OR (@FilledFilter = N'1' AND vac.IsFilled = 1)
                    OR (@FilledFilter = N'0' AND vac.IsFilled = 0)
                  )
            ORDER BY vac.VacancyCode;
        END
        """,
        """
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
            contact.Email,
            contact.Phone
        FROM dbo.Vacancies v
        INNER JOIN dbo.OrganizationTree org ON org.Id = v.OrganizationId
        LEFT JOIN dbo.JobTitles jt ON jt.Id = v.JobTitleId
        LEFT JOIN dbo.StaffVacancy sv ON sv.VacancyId = v.VacancyId
        LEFT JOIN dbo.Persons p ON p.PersonId = sv.PersonId
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=p.PersonId;
        """,
        """
        CREATE OR ALTER VIEW dbo.vw_PersonHrProfiles AS
        SELECT
            p.PersonId, p.TenantId, p.FullName, s.LoginId, s.StaffId, v.VacancyCode,
            COALESCE(j.TitleName, v.JobTitle) AS JobTitle, v.Department, contact.Phone, contact.Email,
            COALESCE(p.PersonalEmail, pcPersonal.ContactValue) AS PersonalEmail,
            p.Gender, p.DateOfBirth, p.MaritalStatus, p.ShiftStartTime, p.ShiftEndTime, p.TimeZoneId,
            h.CnicOrLicense, h.Nationality, h.Race, h.Language, h.BloodGroup, h.Disability, h.PoliceStation,
            COALESCE(h.EmergencyContactNo, pcEmergency.ContactValue) AS EmergencyContactNo,
            h.MedicalFrom, h.MedicalTo, h.Treatment, h.DiagnosisDisease, h.Doctor, h.DoctorContactNo,
            h.BankName, h.BankBranchName, h.BankBranchCode, h.SwiftCode, h.AccountTitle, h.AccountNo, h.IbanNo,
            h.BankBranchContactNo, h.TaxNumber, h.PaymentMode, h.InductionType, h.JoiningDate,
            h.TrainingFrom, h.TrainingTo, h.ProbationFrom, h.ProbationTo, h.ContractFrom, h.ContractTo,
            h.WorkingDays, h.WorkingHours, h.TimingFrom, h.TimingTo, h.PostingPerHour, h.PostingPerDay,
            h.PromotionFrom, h.PromotionTo, h.SalaryPackageId, h.Scale, h.ScaleDate, h.BasicSalary,
            h.IncrementSalary, h.MaxSalary, h.CurrentPay, h.SalaryAdjustment, h.AccountsPerDay, h.AccountsPerHour,
            h.LeaveFrom, h.LeaveTo, h.LeaveEntitled, h.LeaveAvailed
        FROM dbo.Persons p
            LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=p.PersonId
        LEFT JOIN dbo.PersonHrProfiles h ON h.PersonId = p.PersonId
        LEFT JOIN dbo.StaffVacancy s ON s.PersonId = p.PersonId
        LEFT JOIN dbo.Vacancies v ON v.VacancyId = s.VacancyId
        LEFT JOIN dbo.JobTitles j ON j.Id = v.JobTitleId
        OUTER APPLY (
            SELECT TOP (1) c.ContactValue FROM dbo.PersonContacts c
            WHERE c.PersonId = p.PersonId AND c.ContactType = 'PersonalEmail'
            ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
        ) pcPersonal
        OUTER APPLY (
            SELECT TOP (1) c.ContactValue FROM dbo.PersonContacts c
            WHERE c.PersonId = p.PersonId AND c.ContactType = 'Emergency'
            ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
        ) pcEmergency;
        """,
        """
        CREATE OR ALTER VIEW dbo.vw_PersonProfiles AS
        WITH OrgClimb AS
        (
            SELECT Id AS StartId,Id,ParentId,Name,Label,FlagUrl,0 AS Depth FROM dbo.OrganizationTree
            UNION ALL
            SELECT c.StartId,p.Id,p.ParentId,p.Name,p.Label,p.FlagUrl,c.Depth+1
            FROM OrgClimb c JOIN dbo.OrganizationTree p ON p.Id=c.ParentId WHERE c.Depth<20
        ), Labels AS
        (
            SELECT StartId,
                MAX(CASE WHEN Label IN(N'Branch',N'Sub Branch',N'SubBranch',N'Office') THEN Id END) AS BranchId,
                MAX(CASE WHEN Label IN(N'Branch',N'Sub Branch',N'SubBranch',N'Office') THEN Name END) AS BranchName,
                MAX(CASE WHEN Label=N'Company' THEN Name END) AS CompanyName,
                MAX(CASE WHEN Label=N'Country' THEN Name END) AS CountryName,
                MAX(CASE WHEN Label=N'Country' THEN FlagUrl END) AS CountryFlag
            FROM OrgClimb GROUP BY StartId
        )
        SELECT p.PersonId,COALESCE(s.LoginId,p.LastLoginId) AS LoginId,p.FullName,p.Gender,p.DateOfBirth,p.MaritalStatus,
            contact.Phone,contact.Email,p.ProfilePhotoUrl,p.CreatedDate,labels.BranchId,
            COALESCE(labels.BranchName,p.LastBranchName) AS BranchName,
            COALESCE(labels.CompanyName,p.LastCompanyName) AS CompanyName,
            COALESCE(labels.CountryName,p.LastCountryName) AS CountryName,labels.CountryFlag,
            s.StaffId,COALESCE(h.JoiningDate,p.LastJoiningDate) AS JoiningDate,
            v.VacancyId,COALESCE(v.VacancyCode,p.LastVacancyCode) AS VacancyCode,
            COALESCE(j.TitleName,v.JobTitle,p.LastJobTitle) AS JobTitle,COALESCE(v.Department,p.LastDepartment) AS Department,
            CAST(CASE WHEN s.StaffId IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsHired,
            ca.AddressLine AS CurrentAddressLine,ca.Country AS CurrentCountry,ca.Province AS CurrentProvince,
            ca.District AS CurrentDistrict,ca.City AS CurrentCity,ca.PostalCode AS CurrentPostalCode,
            pa.AddressLine AS PermanentAddressLine,pa.Country AS PermanentCountry,pa.Province AS PermanentProvince,
            pa.District AS PermanentDistrict,pa.City AS PermanentCity,pa.PostalCode AS PermanentPostalCode
        FROM dbo.Persons p
        LEFT JOIN dbo.vw_PersonPrimaryContacts contact ON contact.PersonId=p.PersonId
        LEFT JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
        LEFT JOIN dbo.Vacancies v ON v.VacancyId=s.VacancyId AND v.TenantId=p.TenantId
        LEFT JOIN dbo.JobTitles j ON j.Id=v.JobTitleId AND j.TenantId=p.TenantId
        LEFT JOIN dbo.PersonHrProfiles h ON h.PersonId=p.PersonId AND h.TenantId=p.TenantId
        LEFT JOIN Labels labels ON labels.StartId=COALESCE(v.OrganizationId,p.LastOrganizationId)
        LEFT JOIN dbo.PersonAddresses ca ON ca.PersonId=p.PersonId AND ca.AddressType=N'Current'
        LEFT JOIN dbo.PersonAddresses pa ON pa.PersonId=p.PersonId AND pa.AddressType=N'Permanent';
        """,
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Hr_PersonHrProfile_Get
            @TenantId INT,@PersonId UNIQUEIDENTIFIER
        AS
        BEGIN
            SET NOCOUNT ON;
            SELECT * FROM dbo.vw_PersonHrProfiles WHERE TenantId=@TenantId AND PersonId=@PersonId;
        END;
        """
    ];
    private static readonly string[] LegacySql =
    [
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
        """,
        """
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
        """,
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
        """,
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Hr_Persons_List
            @TenantId INT,
            @Mode NVARCHAR(20) = N'ALL' -- ALL | UNASSIGNED | FORMER
        AS
        BEGIN
            SET NOCOUNT ON;
        
            ;WITH OrgClimb AS
            (
                SELECT o.Id AS StartId, o.Id, o.ParentId, o.Name, o.Label, 0 AS Depth
                FROM dbo.OrganizationTree o
                UNION ALL
                SELECT c.StartId, p.Id, p.ParentId, p.Name, p.Label, c.Depth + 1
                FROM OrgClimb c
                INNER JOIN dbo.OrganizationTree p ON p.Id = c.ParentId
                WHERE c.Depth < 20
            ),
            OrgLabels AS
            (
                SELECT
                    StartId,
                    MAX(CASE WHEN Label IN (N'Department', N'Sub Department', N'SubDepartment', N'Unit', N'Team', N'Section') THEN Name END) AS DepartmentName,
                    MAX(CASE WHEN Label IN (N'Branch', N'Sub Branch', N'SubBranch', N'Office') THEN Name END) AS BranchName,
                    MAX(CASE WHEN Label IN (N'Branch', N'Sub Branch', N'SubBranch', N'Office') THEN Id END) AS BranchId,
                    MAX(CASE WHEN Label = N'Company' THEN Name END) AS CompanyName,
                    MAX(CASE WHEN Label = N'Country' THEN Name END) AS CountryName
                FROM OrgClimb
                GROUP BY StartId
            )
            SELECT
                p.PersonId,
                COALESCE(sv.LoginId, N'-') AS LoginId,
                p.FullName,
                p.Gender,
                p.DateOfBirth,
                p.MaritalStatus,
                p.Phone,
                p.Email,
                p.PersonalEmail,
                p.ShiftStartTime,
                p.ShiftEndTime,
                p.TimeZoneId,
                p.ProfilePhotoUrl AS PhotoUrl,
                CAST(CASE WHEN sv.StaffId IS NULL THEN 0 ELSE 1 END AS BIT) AS IsHired,
                CAST(p.IsActive AS BIT) AS IsActive,
                p.EmploymentStatus,
                p.TerminationDateUtc,
                p.TerminationReason,
                CONVERT(NVARCHAR(40), p.CreatedDate, 127) AS RegisteredAt,
                p.LastJoiningDate AS JoiningDate,
                COALESCE(ol.BranchId, CAST(NULL AS INT)) AS BranchId,
                COALESCE(ol.BranchName, p.LastBranchName) AS BranchName,
                COALESCE(ol.CompanyName, p.LastCompanyName) AS CompanyName,
                COALESCE(ol.CountryName, p.LastCountryName) AS CountryName,
                COALESCE(vac.VacancyCode, p.LastVacancyCode) AS VacancyCode,
                COALESCE(des.TitleName, vac.JobTitle, p.LastJobTitle) AS JobTitle,
                COALESCE(vac.Department, ol.DepartmentName, p.LastDepartment) AS Department,
                cur.AddressLine AS CurrentAddressLine,
                cur.Country AS CurrentCountry,
                cur.City AS CurrentCity,
                perm.AddressLine AS PermanentAddressLine,
                perm.Country AS PermanentCountry,
                perm.City AS PermanentCity
            FROM dbo.Persons p
            LEFT JOIN dbo.StaffVacancy sv ON sv.PersonId = p.PersonId AND sv.TenantId = p.TenantId
            LEFT JOIN dbo.Vacancies vac ON vac.VacancyId = sv.VacancyId
            LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
            LEFT JOIN OrgLabels ol ON ol.StartId = vac.OrganizationId
            LEFT JOIN dbo.PersonAddresses cur ON cur.PersonId = p.PersonId AND cur.AddressType = N'Current'
            LEFT JOIN dbo.PersonAddresses perm ON perm.PersonId = p.PersonId AND perm.AddressType = N'Permanent'
            WHERE p.TenantId = @TenantId
              AND (
                    (@Mode = N'ALL')
                    OR (@Mode = N'UNASSIGNED' AND sv.StaffId IS NULL)
                    OR (@Mode = N'FORMER' AND p.EmploymentStatus IN (N'Fired', N'Retired'))
                  )
            ORDER BY
                CASE WHEN @Mode = N'FORMER' THEN p.TerminationDateUtc END DESC,
                p.CreatedDate DESC;
        END
        """,
        """
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
        """,
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Hr_Vacancies_List
            @TenantId INT,
            @FilledFilter NVARCHAR(10) = NULL -- NULL=all, '1'=filled, '0'=open
        AS
        BEGIN
            SET NOCOUNT ON;
        
            ;WITH OrgClimb AS
            (
                SELECT o.Id AS StartId, o.Id, o.ParentId, o.Name, o.Label, 0 AS Depth
                FROM dbo.OrganizationTree o
                UNION ALL
                SELECT c.StartId, p.Id, p.ParentId, p.Name, p.Label, c.Depth + 1
                FROM OrgClimb c
                INNER JOIN dbo.OrganizationTree p ON p.Id = c.ParentId
                WHERE c.Depth < 20
            ),
            OrgLabels AS
            (
                SELECT
                    StartId,
                    MAX(CASE WHEN Label IN (N'Department', N'Sub Department', N'SubDepartment', N'Unit', N'Team', N'Section') THEN Name END) AS DepartmentName,
                    MAX(CASE WHEN Depth = 0 THEN Name END) AS NodeName,
                    MAX(CASE WHEN Depth = 0 THEN Label END) AS NodeLabel,
                    MAX(CASE WHEN Label = N'Company' THEN Name END) AS CompanyName,
                    MAX(CASE WHEN Label = N'Country' THEN Name END) AS CountryName,
                    MAX(CASE WHEN Label IN (N'Branch', N'Sub Branch', N'SubBranch', N'Office') THEN Name END) AS BranchName
                FROM OrgClimb
                GROUP BY StartId
            )
            SELECT
                vac.VacancyId,
                vac.OrganizationId,
                COALESCE(ol.BranchName, ol.NodeName, N'-') AS BranchName,
                COALESCE(ol.CompanyName, N'-') AS CompanyName,
                COALESCE(ol.CountryName, N'-') AS CountryName,
                COALESCE(ol.NodeLabel, N'-') AS NodeLabel,
                vac.VacancyCode,
                vac.JobTitleId AS DesignationId,
                COALESCE(des.TitleName, vac.JobTitle, N'') AS Designation,
                COALESCE(vac.Department, ol.DepartmentName, ol.NodeName) AS Department,
                vac.IsFilled,
                vac.CreatedDate,
                sv.StaffId AS EmployeeStaffId,
                per.FullName AS EmployeeFullName,
                per.Email AS EmployeeEmail,
                per.Phone AS EmployeePhone,
                per.ProfilePhotoUrl AS EmployeePhotoUrl
            FROM dbo.Vacancies vac
            LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
            LEFT JOIN OrgLabels ol ON ol.StartId = vac.OrganizationId
            LEFT JOIN dbo.StaffVacancy sv ON sv.VacancyId = vac.VacancyId AND sv.TenantId = vac.TenantId
            LEFT JOIN dbo.Persons per ON per.PersonId = sv.PersonId
            WHERE vac.TenantId = @TenantId
              AND (
                    @FilledFilter IS NULL
                    OR (@FilledFilter = N'1' AND vac.IsFilled = 1)
                    OR (@FilledFilter = N'0' AND vac.IsFilled = 0)
                  )
            ORDER BY vac.VacancyCode;
        END
        """,
        """
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
        LEFT JOIN dbo.Persons p ON p.PersonId = sv.PersonId;
        """,
        """
        CREATE OR ALTER VIEW dbo.vw_PersonHrProfiles AS
        SELECT
            p.PersonId, p.TenantId, p.FullName, s.LoginId, s.StaffId, v.VacancyCode,
            COALESCE(j.TitleName, v.JobTitle) AS JobTitle, v.Department, p.Phone, p.Email,
            COALESCE(p.PersonalEmail, pcPersonal.ContactValue) AS PersonalEmail,
            p.Gender, p.DateOfBirth, p.MaritalStatus, p.ShiftStartTime, p.ShiftEndTime, p.TimeZoneId,
            h.CnicOrLicense, h.Nationality, h.Race, h.Language, h.BloodGroup, h.Disability, h.PoliceStation,
            COALESCE(h.EmergencyContactNo, pcEmergency.ContactValue) AS EmergencyContactNo,
            h.MedicalFrom, h.MedicalTo, h.Treatment, h.DiagnosisDisease, h.Doctor, h.DoctorContactNo,
            h.BankName, h.BankBranchName, h.BankBranchCode, h.SwiftCode, h.AccountTitle, h.AccountNo, h.IbanNo,
            h.BankBranchContactNo, h.TaxNumber, h.PaymentMode, h.InductionType, h.JoiningDate,
            h.TrainingFrom, h.TrainingTo, h.ProbationFrom, h.ProbationTo, h.ContractFrom, h.ContractTo,
            h.WorkingDays, h.WorkingHours, h.TimingFrom, h.TimingTo, h.PostingPerHour, h.PostingPerDay,
            h.PromotionFrom, h.PromotionTo, h.SalaryPackageId, h.Scale, h.ScaleDate, h.BasicSalary,
            h.IncrementSalary, h.MaxSalary, h.CurrentPay, h.SalaryAdjustment, h.AccountsPerDay, h.AccountsPerHour,
            h.LeaveFrom, h.LeaveTo, h.LeaveEntitled, h.LeaveAvailed
        FROM dbo.Persons p
        LEFT JOIN dbo.PersonHrProfiles h ON h.PersonId = p.PersonId
        LEFT JOIN dbo.StaffVacancy s ON s.PersonId = p.PersonId
        LEFT JOIN dbo.Vacancies v ON v.VacancyId = s.VacancyId
        LEFT JOIN dbo.JobTitles j ON j.Id = v.JobTitleId
        OUTER APPLY (
            SELECT TOP (1) c.ContactValue FROM dbo.PersonContacts c
            WHERE c.PersonId = p.PersonId AND c.ContactType = 'PersonalEmail'
            ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
        ) pcPersonal
        OUTER APPLY (
            SELECT TOP (1) c.ContactValue FROM dbo.PersonContacts c
            WHERE c.PersonId = p.PersonId AND c.ContactType = 'Emergency'
            ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
        ) pcEmergency;
        """,
        """
        CREATE OR ALTER VIEW dbo.vw_PersonProfiles AS
        WITH OrgClimb AS
        (
            SELECT Id AS StartId,Id,ParentId,Name,Label,FlagUrl,0 AS Depth FROM dbo.OrganizationTree
            UNION ALL
            SELECT c.StartId,p.Id,p.ParentId,p.Name,p.Label,p.FlagUrl,c.Depth+1
            FROM OrgClimb c JOIN dbo.OrganizationTree p ON p.Id=c.ParentId WHERE c.Depth<20
        ), Labels AS
        (
            SELECT StartId,
                MAX(CASE WHEN Label IN(N'Branch',N'Sub Branch',N'SubBranch',N'Office') THEN Id END) AS BranchId,
                MAX(CASE WHEN Label IN(N'Branch',N'Sub Branch',N'SubBranch',N'Office') THEN Name END) AS BranchName,
                MAX(CASE WHEN Label=N'Company' THEN Name END) AS CompanyName,
                MAX(CASE WHEN Label=N'Country' THEN Name END) AS CountryName,
                MAX(CASE WHEN Label=N'Country' THEN FlagUrl END) AS CountryFlag
            FROM OrgClimb GROUP BY StartId
        )
        SELECT p.PersonId,COALESCE(s.LoginId,p.LastLoginId) AS LoginId,p.FullName,p.Gender,p.DateOfBirth,p.MaritalStatus,
            p.Phone,p.Email,p.ProfilePhotoUrl,p.CreatedDate,labels.BranchId,
            COALESCE(labels.BranchName,p.LastBranchName) AS BranchName,
            COALESCE(labels.CompanyName,p.LastCompanyName) AS CompanyName,
            COALESCE(labels.CountryName,p.LastCountryName) AS CountryName,labels.CountryFlag,
            s.StaffId,COALESCE(h.JoiningDate,p.LastJoiningDate) AS JoiningDate,
            v.VacancyId,COALESCE(v.VacancyCode,p.LastVacancyCode) AS VacancyCode,
            COALESCE(j.TitleName,v.JobTitle,p.LastJobTitle) AS JobTitle,COALESCE(v.Department,p.LastDepartment) AS Department,
            CAST(CASE WHEN s.StaffId IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsHired,
            ca.AddressLine AS CurrentAddressLine,ca.Country AS CurrentCountry,ca.Province AS CurrentProvince,
            ca.District AS CurrentDistrict,ca.City AS CurrentCity,ca.PostalCode AS CurrentPostalCode,
            pa.AddressLine AS PermanentAddressLine,pa.Country AS PermanentCountry,pa.Province AS PermanentProvince,
            pa.District AS PermanentDistrict,pa.City AS PermanentCity,pa.PostalCode AS PermanentPostalCode
        FROM dbo.Persons p
        LEFT JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
        LEFT JOIN dbo.Vacancies v ON v.VacancyId=s.VacancyId AND v.TenantId=p.TenantId
        LEFT JOIN dbo.JobTitles j ON j.Id=v.JobTitleId AND j.TenantId=p.TenantId
        LEFT JOIN dbo.PersonHrProfiles h ON h.PersonId=p.PersonId AND h.TenantId=p.TenantId
        LEFT JOIN Labels labels ON labels.StartId=COALESCE(v.OrganizationId,p.LastOrganizationId)
        LEFT JOIN dbo.PersonAddresses ca ON ca.PersonId=p.PersonId AND ca.AddressType=N'Current'
        LEFT JOIN dbo.PersonAddresses pa ON pa.PersonId=p.PersonId AND pa.AddressType=N'Permanent';
        """
    ];
}

