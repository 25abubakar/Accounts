-- Fix Person HR: DOJ, ScaleDate, Scale (PKLT→RLT) from owner sheet.
-- ScaleDate = date current scale was applied (upgrade/extension); if blank but scale exists → DOJ.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AsOf date = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Pakistan Standard Time' AS date);
DECLARE @TenantId int = 2007;

DECLARE @Sheet TABLE (
    FullName nvarchar(200) NOT NULL,
    Doj date NULL,
    ScaleDate date NULL,
    ScaleNo int NULL -- NULL = leave scale empty
);

INSERT INTO @Sheet (FullName, Doj, ScaleDate, ScaleNo) VALUES
(N'Abdul Wahab',        '2018-09-24', '2025-06-01', 5),
(N'Aftab Hussain',      '2016-09-01', '2024-08-01', 7),
(N'Asif Khan',          '2019-07-27', '2025-06-01', 6),
(N'Asif Latif',         '2022-06-02', NULL,         4),
(N'Ayaz Qureshi',       '2024-07-08', NULL,         5),
(N'Danish Ahmed',       '2016-10-01', '2025-06-01', 6),
(N'Haroon Shahzad',     '2026-05-03', '2026-05-01', 4),
(N'Humayun Hashmat',    '2016-01-20', '2024-08-01', 4),
(N'Kishwar Hanif',      '2022-04-15', NULL,         5),
(N'M Arsalan',          '2022-09-21', '2025-06-01', 5),
(N'M Zahid',            '2016-08-01', '2025-06-01', 6),
(N'Muhammad Abubakar',  '2026-03-01', NULL,         NULL), -- sheet empty scale
(N'Muhammad Bashir Baz',NULL,         NULL,         NULL),
(N'Muhammad Farooq',    '2023-03-01', NULL,         10),
(N'Muhammad Kamran',    '2025-07-01', NULL,         3),
(N'Naseem Riaz',        '2024-07-22', '2025-06-01', 5),
(N'Noman Arif',         '2023-11-21', NULL,         3),
(N'Obaid Ullah',        '2026-03-01', NULL,         NULL), -- sheet empty scale
(N'Sajjad Aslam',       '2022-11-11', NULL,         4),
(N'Shehzad Gull',       '2023-03-01', NULL,         3),
(N'Taimoor Arshad',     '2020-11-01', '2024-08-01', 10),
(N'Umair khan',         '2023-09-01', NULL,         3),
(N'Zain Ul Abdin',      '2022-07-01', NULL,         4),
(N'Zeeshan Farooq',     '2017-12-04', '2024-10-01', 6);

-- One active RLT-* scale per name for tenant (lowest Id).
;WITH ScalePick AS (
    SELECT
        s.ScaleName,
        s.BasicSalary,
        s.YearlyIncrement,
        s.MaximumSalary,
        ROW_NUMBER() OVER (PARTITION BY s.ScaleName ORDER BY s.Id) AS rn
    FROM dbo.SalaryScales s
    WHERE s.TenantId = @TenantId
      AND s.IsActive = 1
      AND s.ScaleName LIKE N'RLT-%'
)
SELECT * INTO #Scales FROM ScalePick WHERE rn = 1;

BEGIN TRAN;

-- Ensure HR row exists for matched staff.
INSERT INTO dbo.PersonHrProfiles (PersonId, TenantId, CreatedDate, ModifiedDate)
SELECT p.PersonId, p.TenantId, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM dbo.Persons p
INNER JOIN @Sheet sh ON sh.FullName = p.FullName
WHERE p.TenantId = @TenantId
  AND NOT EXISTS (SELECT 1 FROM dbo.PersonHrProfiles hr WHERE hr.PersonId = p.PersonId);

UPDATE hr
SET
    hr.JoiningDate = sh.Doj,
    hr.ScaleDate = CASE
        WHEN sh.ScaleNo IS NULL THEN NULL
        WHEN sh.ScaleDate IS NOT NULL THEN CAST(sh.ScaleDate AS datetime2)
        WHEN sh.Doj IS NOT NULL THEN CAST(sh.Doj AS datetime2) -- initial scale at join
        ELSE NULL
    END,
    hr.Scale = CASE WHEN sh.ScaleNo IS NULL THEN NULL ELSE CONCAT(N'RLT-', sh.ScaleNo) END,
    hr.BasicSalary = sc.BasicSalary,
    hr.IncrementSalary = sc.YearlyIncrement,
    hr.MaxSalary = sc.MaximumSalary,
    hr.CurrentPay = CASE
        WHEN sc.ScaleName IS NULL THEN NULL
        ELSE
            CASE
                WHEN sc.YearlyIncrement <= 0 OR anchor.AnchorDate IS NULL OR anchor.AnchorDate > @AsOf
                    THEN CASE WHEN sc.MaximumSalary > 0 AND sc.BasicSalary > sc.MaximumSalary THEN sc.MaximumSalary ELSE sc.BasicSalary END
                ELSE
                (
                    SELECT CASE
                        WHEN pay.RawPay > sc.MaximumSalary AND sc.MaximumSalary > 0 THEN sc.MaximumSalary
                        ELSE pay.RawPay
                    END
                    FROM (
                        SELECT
                            sc.BasicSalary + (
                                -- completed anniversary years + 1 (first service year earns 1× INC)
                                CASE
                                    WHEN completed.Y < 0 THEN 0
                                    ELSE completed.Y + 1
                                END
                            ) * sc.YearlyIncrement AS RawPay
                        FROM (
                            SELECT
                                DATEDIFF(year, anchor.AnchorDate, @AsOf)
                                - CASE
                                    WHEN DATEADD(year, DATEDIFF(year, anchor.AnchorDate, @AsOf), anchor.AnchorDate) > @AsOf THEN 1
                                    ELSE 0
                                  END AS Y
                        ) completed
                    ) pay
                )
            END
    END,
    hr.ModifiedDate = SYSUTCDATETIME()
FROM dbo.PersonHrProfiles hr
INNER JOIN dbo.Persons p ON p.PersonId = hr.PersonId
INNER JOIN @Sheet sh ON sh.FullName = p.FullName
LEFT JOIN #Scales sc ON sc.ScaleName = CONCAT(N'RLT-', sh.ScaleNo)
OUTER APPLY (
    SELECT CAST(COALESCE(sh.ScaleDate, sh.Doj) AS date) AS AnchorDate
) anchor
WHERE p.TenantId = @TenantId;

-- Keep Persons.LastJoiningDate in sync when column exists.
IF COL_LENGTH(N'dbo.Persons', N'LastJoiningDate') IS NOT NULL
BEGIN
    UPDATE p
    SET p.LastJoiningDate = sh.Doj
    FROM dbo.Persons p
    INNER JOIN @Sheet sh ON sh.FullName = p.FullName
    WHERE p.TenantId = @TenantId
      AND sh.Doj IS NOT NULL;
END

COMMIT TRAN;

SELECT
    p.FullName,
    COALESCE(CASE WHEN org.Label = N'Department' THEN org.Name END, vac.Department, org.Name) AS Department,
    CONVERT(varchar(10), hr.JoiningDate, 103) AS DOJ,
    CONVERT(varchar(10), hr.ScaleDate, 103) AS ScaleDate,
    hr.Scale,
    hr.BasicSalary,
    hr.IncrementSalary,
    hr.MaxSalary,
    hr.CurrentPay
FROM dbo.Persons p
INNER JOIN dbo.PersonHrProfiles hr ON hr.PersonId = p.PersonId
LEFT JOIN dbo.StaffVacancy sv ON sv.PersonId = p.PersonId AND sv.TenantId = p.TenantId
LEFT JOIN dbo.Vacancies vac ON vac.VacancyId = sv.VacancyId
LEFT JOIN dbo.OrganizationTree org ON org.Id = vac.OrganizationId
INNER JOIN @Sheet sh ON sh.FullName = p.FullName
ORDER BY p.FullName;

-- Sheet names not found in DB
SELECT sh.FullName AS MissingInDb
FROM @Sheet sh
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Persons p
    WHERE p.TenantId = @TenantId AND p.FullName = sh.FullName
);

DROP TABLE #Scales;
GO
