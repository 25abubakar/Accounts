-- Align Person HR ScaleDate / Scale / Induction with owner sheet.
-- ScaleDate ONLY where sheet has a date (not auto-filled from DOJ).
-- Probation: Muhammad Abubakar, Obaid Ullah. Others Regular when hired with DOJ.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AsOf date = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Pakistan Standard Time' AS date);
DECLARE @TenantId int = 2007;

DECLARE @Sheet TABLE (
    FullName nvarchar(200) NOT NULL,
    Doj date NULL,
    ScaleDate date NULL,      -- NULL = leave ScaleDate empty
    ScaleNo int NULL,         -- NULL = clear scale
    Induction nvarchar(40) NOT NULL -- Regular | Probation
);

INSERT INTO @Sheet (FullName, Doj, ScaleDate, ScaleNo, Induction) VALUES
(N'Abdul Wahab',        '2018-09-24', '2025-06-01', 5,  N'Regular'),
(N'Aftab Hussain',      '2016-09-01', '2024-08-01', 7,  N'Regular'),
(N'Asif Khan',          '2019-07-27', '2025-06-01', 6,  N'Regular'),
(N'Asif Latif',         '2022-06-02', NULL,         4,  N'Regular'),
(N'Ayaz Qureshi',       '2024-07-08', NULL,         5,  N'Regular'),
(N'Danish Ahmed',       '2016-10-01', '2025-06-01', 6,  N'Regular'),
(N'Haroon Shahzad',     '2026-05-03', '2026-05-01', 4,  N'Regular'),
(N'Humayun Hashmat',    '2016-01-20', '2024-08-01', 4,  N'Regular'),
(N'Kishwar Hanif',      '2022-04-15', NULL,         5,  N'Regular'),
(N'M Arsalan',          '2022-09-21', '2025-06-01', 5,  N'Regular'),
(N'M Zahid',            '2016-08-01', '2025-06-01', 6,  N'Regular'),
(N'Muhammad Abubakar',  '2026-03-01', NULL,         NULL, N'Probation'),
(N'Muhammad Bashir Baz',NULL,         NULL,         NULL, N'Regular'),
(N'Muhammad Farooq',    '2023-03-01', NULL,         10, N'Regular'),
(N'Muhammad Kamran',    '2025-07-01', NULL,         3,  N'Regular'),
(N'Naseem Riaz',        '2024-07-22', '2025-06-01', 5,  N'Regular'),
(N'Noman Arif',         '2023-11-21', NULL,         3,  N'Regular'),
(N'Obaid Ullah',        '2026-03-01', NULL,         NULL, N'Probation'),
(N'Sajjad Aslam',       '2022-11-11', NULL,         4,  N'Regular'),
(N'Shehzad Gull',       '2023-03-01', NULL,         3,  N'Regular'),
(N'Taimoor Arshad',     '2020-11-01', '2024-08-01', 10, N'Regular'),
(N'Umair khan',         '2023-09-01', NULL,         3,  N'Regular'),
(N'Zain Ul Abdin',      '2022-07-01', NULL,         4,  N'Regular'),
(N'Zeeshan Farooq',     '2017-12-04', '2024-10-01', 6,  N'Regular');

;WITH ScalePick AS (
    SELECT ScaleName, BasicSalary, YearlyIncrement, MaximumSalary,
           ROW_NUMBER() OVER (PARTITION BY ScaleName ORDER BY Id) rn
    FROM dbo.SalaryScales
    WHERE TenantId = @TenantId AND IsActive = 1 AND ScaleName LIKE N'RLT-%'
)
SELECT * INTO #Scales FROM ScalePick WHERE rn = 1;

BEGIN TRAN;

INSERT INTO dbo.PersonHrProfiles (PersonId, TenantId, CreatedDate, ModifiedDate)
SELECT p.PersonId, p.TenantId, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM dbo.Persons p
INNER JOIN @Sheet sh ON sh.FullName = p.FullName
WHERE p.TenantId = @TenantId
  AND NOT EXISTS (SELECT 1 FROM dbo.PersonHrProfiles hr WHERE hr.PersonId = p.PersonId);

UPDATE hr
SET
    hr.JoiningDate = sh.Doj,
    hr.ScaleDate = CASE WHEN sh.ScaleDate IS NULL THEN NULL ELSE CAST(sh.ScaleDate AS datetime2) END,
    hr.Scale = CASE WHEN sh.ScaleNo IS NULL THEN NULL ELSE CONCAT(N'RLT-', sh.ScaleNo) END,
    hr.BasicSalary = sc.BasicSalary,
    hr.IncrementSalary = sc.YearlyIncrement,
    hr.MaxSalary = sc.MaximumSalary,
    hr.CurrentPay = CASE
        WHEN sc.ScaleName IS NULL THEN NULL
        WHEN anchor.AnchorDate IS NULL OR sc.YearlyIncrement <= 0 OR anchor.AnchorDate > @AsOf
            THEN CASE WHEN sc.MaximumSalary > 0 AND sc.BasicSalary > sc.MaximumSalary THEN sc.MaximumSalary ELSE sc.BasicSalary END
        ELSE (
            SELECT CASE WHEN pay.RawPay > sc.MaximumSalary AND sc.MaximumSalary > 0 THEN sc.MaximumSalary ELSE pay.RawPay END
            FROM (
                SELECT sc.BasicSalary + (CASE WHEN completed.Y < 0 THEN 0 ELSE completed.Y + 1 END) * sc.YearlyIncrement AS RawPay
                FROM (
                    SELECT DATEDIFF(year, anchor.AnchorDate, @AsOf)
                         - CASE WHEN DATEADD(year, DATEDIFF(year, anchor.AnchorDate, @AsOf), anchor.AnchorDate) > @AsOf THEN 1 ELSE 0 END AS Y
                ) completed
            ) pay
        )
    END,
    hr.InductionType = sh.Induction,
    hr.ProbationFrom = CASE WHEN sh.Induction = N'Probation' AND sh.Doj IS NOT NULL THEN CAST(sh.Doj AS datetime2) ELSE NULL END,
    hr.ProbationTo = CASE WHEN sh.Induction = N'Probation' AND sh.Doj IS NOT NULL THEN DATEADD(month, 3, CAST(sh.Doj AS datetime2)) ELSE NULL END,
    hr.ContractFrom = CASE WHEN sh.Induction = N'Regular' AND sh.Doj IS NOT NULL THEN CAST(sh.Doj AS datetime2) ELSE NULL END,
    hr.ContractTo = NULL,
    hr.TrainingFrom = NULL,
    hr.TrainingTo = NULL,
    hr.ModifiedDate = SYSUTCDATETIME()
FROM dbo.PersonHrProfiles hr
INNER JOIN dbo.Persons p ON p.PersonId = hr.PersonId
INNER JOIN @Sheet sh ON sh.FullName = p.FullName
LEFT JOIN #Scales sc ON sc.ScaleName = CONCAT(N'RLT-', sh.ScaleNo)
OUTER APPLY (
    -- Pay increments from ScaleDate when set, else DOJ (scale applied at join without separate ScaleDate).
    SELECT CAST(COALESCE(sh.ScaleDate, sh.Doj) AS date) AS AnchorDate
) anchor
WHERE p.TenantId = @TenantId;

IF COL_LENGTH(N'dbo.Persons', N'LastJoiningDate') IS NOT NULL
BEGIN
    UPDATE p SET p.LastJoiningDate = sh.Doj
    FROM dbo.Persons p
    INNER JOIN @Sheet sh ON sh.FullName = p.FullName
    WHERE p.TenantId = @TenantId AND sh.Doj IS NOT NULL;
END

COMMIT TRAN;

PRINT N'=== WITH ScaleDate ===';
SELECT p.FullName, CONVERT(varchar(10), hr.JoiningDate, 103) DOJ,
       CONVERT(varchar(10), hr.ScaleDate, 103) ScaleDate, hr.Scale, hr.InductionType, hr.CurrentPay
FROM Persons p JOIN PersonHrProfiles hr ON hr.PersonId=p.PersonId
JOIN @Sheet sh ON sh.FullName=p.FullName
WHERE hr.ScaleDate IS NOT NULL
ORDER BY p.FullName;

PRINT N'=== WITHOUT ScaleDate ===';
SELECT p.FullName, CONVERT(varchar(10), hr.JoiningDate, 103) DOJ,
       hr.Scale, hr.InductionType,
       CONVERT(varchar(10), hr.ProbationFrom, 103) ProbFrom,
       CONVERT(varchar(10), hr.ProbationTo, 103) ProbTo,
       hr.CurrentPay
FROM Persons p JOIN PersonHrProfiles hr ON hr.PersonId=p.PersonId
JOIN @Sheet sh ON sh.FullName=p.FullName
WHERE hr.ScaleDate IS NULL
ORDER BY p.FullName;

PRINT N'=== PROBATION ===';
SELECT p.FullName, hr.InductionType,
       CONVERT(varchar(10), hr.ProbationFrom, 103) ProbFrom,
       CONVERT(varchar(10), hr.ProbationTo, 103) ProbTo, hr.Scale
FROM Persons p JOIN PersonHrProfiles hr ON hr.PersonId=p.PersonId
WHERE hr.InductionType = N'Probation'
ORDER BY p.FullName;

DROP TABLE #Scales;
GO
