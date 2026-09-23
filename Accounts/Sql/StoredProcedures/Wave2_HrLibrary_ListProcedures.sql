SET NOCOUNT ON;
GO

/* Wave-2 HR / Library list SPs — kill Include-graph / correlated-count list loads */

CREATE OR ALTER PROCEDURE dbo.usp_Hr_Designations_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- EF Designation entity maps to dbo.JobTitles (TitleName).
    SELECT
        d.Id,
        d.TitleName AS Name,
        d.AttendanceVisibilityScope,
        COUNT(v.VacancyId) AS [Count]
    FROM dbo.JobTitles d
    LEFT JOIN dbo.Vacancies v
        ON v.JobTitleId = d.Id
       AND v.TenantId = @TenantId
    WHERE d.TenantId = @TenantId
    GROUP BY d.Id, d.TitleName, d.AttendanceVisibilityScope
    ORDER BY d.TitleName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Hr_Staff_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH OrgClimb AS
    (
        SELECT
            o.Id AS StartId,
            o.Id,
            o.ParentId,
            o.Name,
            o.Label,
            0 AS Depth
        FROM dbo.OrganizationTree o
        UNION ALL
        SELECT
            c.StartId,
            p.Id,
            p.ParentId,
            p.Name,
            p.Label,
            c.Depth + 1
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
            MAX(CASE WHEN Label = N'Company' THEN Name END) AS CompanyName,
            MAX(CASE WHEN Label = N'Country' THEN Name END) AS CountryName,
            MAX(CASE WHEN Label = N'Group' THEN Name END) AS GroupName
        FROM OrgClimb
        GROUP BY StartId
    )
    SELECT
        sv.StaffId,
        sv.PersonId,
        per.FullName,
        per.Email,
        per.Phone,
        per.ProfilePhotoUrl AS PhotoUrl,
        CAST(per.IsActive AS BIT) AS IsActive,
        sv.LoginId,
        sv.VacancyId,
        vac.VacancyCode,
        COALESCE(des.TitleName, vac.JobTitle, N'') AS Designation,
        COALESCE(vac.Department, ol.DepartmentName, org.Name, N'') AS Department,
        ol.BranchName,
        ol.CompanyName,
        ol.CountryName,
        ol.GroupName,
        per.ShiftStartTime,
        per.ShiftEndTime,
        CAST(SYSUTCDATETIME() AS DATETIME2) AS JoiningDate
    FROM dbo.StaffVacancy sv
    INNER JOIN dbo.Persons per ON per.PersonId = sv.PersonId AND per.TenantId = sv.TenantId
    LEFT JOIN dbo.Vacancies vac ON vac.VacancyId = sv.VacancyId AND vac.TenantId = sv.TenantId
    LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
    LEFT JOIN dbo.OrganizationTree org ON org.Id = vac.OrganizationId
    LEFT JOIN OrgLabels ol ON ol.StartId = vac.OrganizationId
    WHERE sv.TenantId = @TenantId
    ORDER BY per.FullName;
END
GO

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
GO

CREATE OR ALTER PROCEDURE dbo.usp_Hr_ReportTo_List
    @TenantId INT,
    @VisiblePersonIds NVARCHAR(MAX) = NULL -- JSON array of guids; NULL = all tenant staff
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Visible TABLE (PersonId UNIQUEIDENTIFIER PRIMARY KEY);
    IF @VisiblePersonIds IS NOT NULL AND LEN(LTRIM(RTRIM(@VisiblePersonIds))) > 2
    BEGIN
        INSERT INTO @Visible(PersonId)
        SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER, [value])
        FROM OPENJSON(@VisiblePersonIds)
        WHERE TRY_CONVERT(UNIQUEIDENTIFIER, [value]) IS NOT NULL;
    END

    SELECT
        p.PersonId,
        p.FullName,
        p.ProfilePhotoUrl,
        CAST(p.IsActive AS BIT) AS IsActive,
        sv.StaffId,
        sv.LoginId AS EmployeeId,
        COALESCE(
            CASE WHEN org.Label = N'Department' THEN org.Name END,
            vac.Department,
            org.Name
        ) AS Department,
        COALESCE(des.TitleName, vac.JobTitle) AS Designation,
        p.ReportsToPersonId,
        mgr.FullName AS ReportsToName,
        COALESCE(
            CASE WHEN morg.Label = N'Department' THEN morg.Name END,
            mvac.Department,
            morg.Name
        ) AS ReportsToDepartment,
        COALESCE(mdes.TitleName, mvac.JobTitle) AS ReportsToDesignation,
        p.AlternativeReportsToPersonId,
        alt.FullName AS AlternativeReportsToName,
        COALESCE(
            CASE WHEN aorg.Label = N'Department' THEN aorg.Name END,
            avoc.Department,
            aorg.Name
        ) AS AlternativeReportsToDepartment,
        COALESCE(ades.TitleName, avoc.JobTitle) AS AlternativeReportsToDesignation
    FROM dbo.Persons p
    INNER JOIN dbo.StaffVacancy sv ON sv.PersonId = p.PersonId AND sv.TenantId = p.TenantId
    LEFT JOIN dbo.Vacancies vac ON vac.VacancyId = sv.VacancyId
    LEFT JOIN dbo.OrganizationTree org ON org.Id = vac.OrganizationId
    LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
    LEFT JOIN dbo.Persons mgr ON mgr.PersonId = p.ReportsToPersonId
    LEFT JOIN dbo.StaffVacancy msv ON msv.PersonId = mgr.PersonId
    LEFT JOIN dbo.Vacancies mvac ON mvac.VacancyId = msv.VacancyId
    LEFT JOIN dbo.OrganizationTree morg ON morg.Id = mvac.OrganizationId
    LEFT JOIN dbo.JobTitles mdes ON mdes.Id = mvac.JobTitleId
    LEFT JOIN dbo.Persons alt ON alt.PersonId = p.AlternativeReportsToPersonId
    LEFT JOIN dbo.StaffVacancy asv ON asv.PersonId = alt.PersonId
    LEFT JOIN dbo.Vacancies avoc ON avoc.VacancyId = asv.VacancyId
    LEFT JOIN dbo.OrganizationTree aorg ON aorg.Id = avoc.OrganizationId
    LEFT JOIN dbo.JobTitles ades ON ades.Id = avoc.JobTitleId
    WHERE p.TenantId = @TenantId
      AND (
            NOT EXISTS (SELECT 1 FROM @Visible)
            OR EXISTS (SELECT 1 FROM @Visible v WHERE v.PersonId = p.PersonId)
          )
    ORDER BY p.FullName;
END
GO

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
GO

CREATE OR ALTER PROCEDURE dbo.usp_Library_Documents_List
    @TenantId INT,
    @AssetKind NVARCHAR(30),
    @TypeId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        d.Id,
        d.TenantId,
        d.LibraryTypeId,
        t.Name AS LibraryTypeName,
        d.AssetKind,
        d.Title,
        d.Description,
        d.OriginalFileName,
        d.StoredFileName,
        d.ContentType,
        d.FileExtension,
        d.FileSizeBytes,
        d.IsActive,
        d.UploadedByUserId,
        d.CreatedOnUtc,
        d.UpdatedOnUtc
    FROM dbo.LibraryDocuments d
    LEFT JOIN dbo.LibraryTypes t ON t.Id = d.LibraryTypeId
    WHERE d.TenantId = @TenantId
      AND d.AssetKind = @AssetKind
      AND (@TypeId IS NULL OR d.LibraryTypeId = @TypeId)
    ORDER BY d.CreatedOnUtc DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Library_Templates_List
    @TenantId INT,
    @TypeId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        x.Id,
        x.TenantId,
        x.LibraryTypeId,
        t.Name AS LibraryTypeName,
        x.Name,
        x.Description,
        x.Content,
        x.IsActive,
        x.CreatedByUserId,
        x.CreatedOnUtc,
        x.UpdatedOnUtc
    FROM dbo.LibraryTemplates x
    LEFT JOIN dbo.LibraryTypes t ON t.Id = x.LibraryTypeId
    WHERE x.TenantId = @TenantId
      AND (@TypeId IS NULL OR x.LibraryTypeId = @TypeId)
    ORDER BY x.CreatedOnUtc DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Library_Invoices_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        i.Id,
        i.TenantId,
        i.InvoiceNumber,
        i.CustomerName,
        i.CustomerEmail,
        i.CustomerAddress,
        i.IssueDate,
        i.DueDate,
        i.Currency,
        i.Subtotal,
        i.TaxRate,
        i.TaxAmount,
        i.DiscountAmount,
        i.TotalAmount,
        i.Status,
        i.Notes,
        i.CreatedByUserId,
        i.CreatedOnUtc,
        i.UpdatedOnUtc
    FROM dbo.GeneratedInvoices i
    WHERE i.TenantId = @TenantId
    ORDER BY i.CreatedOnUtc DESC;
END
GO
