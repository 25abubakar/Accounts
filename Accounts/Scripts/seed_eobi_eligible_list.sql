-- Seed EOBI Eligible List + PersonHrProfiles.JoiningDate from legacy screenshot.
-- Match by FullName (local LT numbers may differ from legacy StaffIds).
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#EobiSeed') IS NOT NULL DROP TABLE #EobiSeed;
CREATE TABLE #EobiSeed
(
    MatchName   nvarchar(200) NOT NULL PRIMARY KEY,
    Doj         date NOT NULL,
    IsOn        bit NOT NULL
);

INSERT INTO #EobiSeed (MatchName, Doj, IsOn) VALUES
(N'Taimoor Arshad',   '2020-11-01', 1),
(N'Danish Ahmed',     '2015-10-01', 1),
(N'M Arsalan',        '2022-09-21', 1),
(N'M Zahid',          '2015-08-01', 1),
(N'Asif Khan',        '2019-07-27', 1),
(N'Zeeshan Farooq',   '2017-12-04', 1),
(N'Humayun Hashmat',  '2015-01-20', 1),
(N'Sajjad Aslam',     '2022-11-11', 1),
(N'Kishwar Hanif',    '2022-04-13', 1),
(N'Aftab Hussain',    '2015-09-01', 1),
(N'Shehzad Gull',     '2023-03-01', 1),
(N'Umair khan',       '2023-09-01', 0),
(N'Asif Latif',       '2022-05-02', 1),
(N'Abdul Wahab',      '2018-09-24', 1),
(N'Zain Ul Abdin',    '2022-01-01', 1),
(N'Muhammad Farooq',  '2023-03-01', 1),
(N'Noman Arif',       '2023-11-21', 1),
(N'Ayaz Qureshi',     '2024-07-05', 1),
(N'Naseem Riaz',      '2024-07-22', 1),
(N'Muhammad Kamran',  '2023-07-01', 1);

DECLARE @Matched int = 0, @HrUpdated int = 0, @EligUpserted int = 0;
DECLARE @PersonId uniqueidentifier, @TenantId int, @Doj date, @IsOn bit;

DECLARE c CURSOR LOCAL FAST_FORWARD FOR
SELECT d.PersonId, d.TenantId, s.Doj, s.IsOn
FROM #EobiSeed s
INNER JOIN dbo.vw_StaffDirectory d
    ON LOWER(LTRIM(RTRIM(d.FullName))) = LOWER(LTRIM(RTRIM(s.MatchName)));

OPEN c;
FETCH NEXT FROM c INTO @PersonId, @TenantId, @Doj, @IsOn;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Matched += 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.PersonHrProfiles WHERE PersonId = @PersonId)
    BEGIN
        INSERT INTO dbo.PersonHrProfiles (PersonId, TenantId, JoiningDate, CreatedDate)
        VALUES (@PersonId, @TenantId, CAST(@Doj AS datetime2), SYSUTCDATETIME());
        SET @HrUpdated += 1;
    END
    ELSE
    BEGIN
        UPDATE dbo.PersonHrProfiles
        SET JoiningDate = CAST(@Doj AS datetime2),
            ModifiedDate = SYSUTCDATETIME()
        WHERE PersonId = @PersonId;
        SET @HrUpdated += @@ROWCOUNT;
    END

    IF EXISTS (SELECT 1 FROM dbo.EobiEligibilities WHERE PersonId = @PersonId AND TenantId = @TenantId)
    BEGIN
        UPDATE dbo.EobiEligibilities
        SET EobiNumber = NULL,
            EffectiveFrom = @Doj,
            EffectiveTo = NULL,
            IsEligible = @IsOn,
            UpdatedOnUtc = SYSUTCDATETIME()
        WHERE PersonId = @PersonId AND TenantId = @TenantId;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.EobiEligibilities
            (TenantId, PersonId, EobiNumber, EffectiveFrom, EffectiveTo, IsEligible, Remarks, CreatedOnUtc, UpdatedOnUtc)
        VALUES
            (@TenantId, @PersonId, NULL, @Doj, NULL, @IsOn, NULL, SYSUTCDATETIME(), NULL);
    END
    SET @EligUpserted += 1;

    FETCH NEXT FROM c INTO @PersonId, @TenantId, @Doj, @IsOn;
END
CLOSE c; DEALLOCATE c;

SELECT @Matched AS MatchedStaff, @HrUpdated AS HrRowsTouched, @EligUpserted AS EligibilityUpserts;

SELECT e.Id AS id, d.EmployeeId AS StaffId, d.FullName, e.EobiNumber AS EOBI_No, d.Department,
       CONVERT(varchar(10), CAST(hr.JoiningDate AS date), 103) AS DOJ, e.IsEligible AS IsOn
FROM dbo.EobiEligibilities e
INNER JOIN dbo.vw_StaffDirectory d ON d.PersonId = e.PersonId AND d.TenantId = e.TenantId
LEFT JOIN dbo.PersonHrProfiles hr ON hr.PersonId = e.PersonId
ORDER BY d.FullName;

SELECT s.MatchName AS UnmatchedSeedName
FROM #EobiSeed s
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.vw_StaffDirectory d
    WHERE LOWER(LTRIM(RTRIM(d.FullName))) = LOWER(LTRIM(RTRIM(s.MatchName)))
);
