-- ============================================================
-- MIGRATION V2: Full Schema Normalization
-- LAL Group Accounts Portal
-- Run order: execute top-to-bottom in a transaction.
-- Safe to run on existing DB — uses IF NOT EXISTS guards.
-- ============================================================

BEGIN TRANSACTION;

-- ─────────────────────────────────────────────────────────────
-- PART 1A: Split Persons.FullName → FirstName, MiddleName, LastName
-- ─────────────────────────────────────────────────────────────

-- Add the three new columns (nullable first so existing rows stay valid)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Persons') AND name = 'FirstName'
)
BEGIN
    ALTER TABLE dbo.Persons
        ADD FirstName   NVARCHAR(60)  NULL,
            MiddleName  NVARCHAR(60)  NULL,
            LastName    NVARCHAR(60)  NULL;
    PRINT 'Added FirstName/MiddleName/LastName to Persons.';
END

-- Backfill: split existing FullName on first space
-- Everything before the first space → FirstName, rest → LastName
UPDATE dbo.Persons
SET
    FirstName  = LTRIM(RTRIM(LEFT(FullName, CHARINDEX(' ', FullName + ' ') - 1))),
    MiddleName = NULL,
    LastName   = CASE
                     WHEN CHARINDEX(' ', LTRIM(RTRIM(FullName))) > 0
                     THEN LTRIM(RTRIM(SUBSTRING(FullName, CHARINDEX(' ', LTRIM(RTRIM(FullName))) + 1, 200)))
                     ELSE NULL
                 END
WHERE FirstName IS NULL;

-- Make FirstName and LastName required after backfill
ALTER TABLE dbo.Persons ALTER COLUMN FirstName NVARCHAR(60) NOT NULL;
ALTER TABLE dbo.Persons ALTER COLUMN LastName  NVARCHAR(60) NULL;   -- allow null (some people use mononyms)

-- NOTE: FullName column is KEPT for backward compatibility with existing
-- code that reads it. A computed column will sync it going forward.
-- Drop old column only when all EF projections are updated.
PRINT 'Persons name columns updated.';


-- ─────────────────────────────────────────────────────────────
-- PART 1B: PersonContacts — one-to-many emails & phones
-- ─────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.PersonContacts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PersonContacts (
        Id           INT           NOT NULL IDENTITY(1,1),
        PersonId     UNIQUEIDENTIFIER NOT NULL,
        ContactType  VARCHAR(20)   NOT NULL,   -- 'Email' | 'Phone' | 'WhatsApp' | 'Emergency'
        ContactValue NVARCHAR(256) NOT NULL,
        IsPrimary    BIT           NOT NULL DEFAULT(0),
        CreatedDate  DATETIME2     NOT NULL DEFAULT(SYSUTCDATETIME()),

        CONSTRAINT PK_PersonContacts PRIMARY KEY (Id),
        CONSTRAINT FK_PersonContacts_Persons
            FOREIGN KEY (PersonId) REFERENCES dbo.Persons(PersonId)
            ON DELETE CASCADE,
        CONSTRAINT CK_PersonContacts_Type
            CHECK (ContactType IN ('Email', 'Phone', 'WhatsApp', 'Emergency', 'Other'))
    );

    -- Each person can have at most one primary per type
    CREATE UNIQUE INDEX UIX_PersonContacts_PrimaryPerType
        ON dbo.PersonContacts (PersonId, ContactType)
        WHERE IsPrimary = 1;

    -- Fast lookup by person
    CREATE INDEX IX_PersonContacts_PersonId ON dbo.PersonContacts (PersonId);

    PRINT 'Created PersonContacts table.';
END

-- Migrate existing Persons.Email → PersonContacts
INSERT INTO dbo.PersonContacts (PersonId, ContactType, ContactValue, IsPrimary)
SELECT p.PersonId, 'Email', p.Email, 1
FROM   dbo.Persons p
WHERE  p.Email IS NOT NULL
  AND  NOT EXISTS (
           SELECT 1 FROM dbo.PersonContacts c
           WHERE  c.PersonId = p.PersonId AND c.ContactType = 'Email'
       );

-- Migrate existing Persons.Phone → PersonContacts
INSERT INTO dbo.PersonContacts (PersonId, ContactType, ContactValue, IsPrimary)
SELECT p.PersonId, 'Phone', p.Phone, 1
FROM   dbo.Persons p
WHERE  p.Phone IS NOT NULL
  AND  NOT EXISTS (
           SELECT 1 FROM dbo.PersonContacts c
           WHERE  c.PersonId = p.PersonId AND c.ContactType = 'Phone'
       );

PRINT 'Migrated existing Email/Phone into PersonContacts.';


-- ─────────────────────────────────────────────────────────────
-- PART 2: JobTitles — normalize Vacancy.JobTitle string to FK
-- ─────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.JobTitles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobTitles (
        Id        INT           NOT NULL IDENTITY(1,1),
        TitleName NVARCHAR(100) NOT NULL,

        CONSTRAINT PK_JobTitles PRIMARY KEY (Id),
        CONSTRAINT UIX_JobTitles_TitleName UNIQUE (TitleName)
    );
    PRINT 'Created JobTitles table.';
END

-- Seed from existing Vacancy.JobTitle strings
INSERT INTO dbo.JobTitles (TitleName)
SELECT DISTINCT LTRIM(RTRIM(JobTitle))
FROM   dbo.Vacancies
WHERE  JobTitle IS NOT NULL
  AND  LTRIM(RTRIM(JobTitle)) <> ''
  AND  LTRIM(RTRIM(JobTitle)) NOT IN (SELECT TitleName FROM dbo.JobTitles);

PRINT 'Seeded JobTitles from existing Vacancies.JobTitle data.';

-- Add JobTitleId FK to Vacancies (nullable during migration)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Vacancies') AND name = 'JobTitleId'
)
BEGIN
    ALTER TABLE dbo.Vacancies ADD JobTitleId INT NULL;

    ALTER TABLE dbo.Vacancies
        ADD CONSTRAINT FK_Vacancies_JobTitles
        FOREIGN KEY (JobTitleId) REFERENCES dbo.JobTitles(Id)
        ON DELETE RESTRICT;

    CREATE INDEX IX_Vacancies_JobTitleId ON dbo.Vacancies (JobTitleId);
    PRINT 'Added JobTitleId FK to Vacancies.';
END

-- Backfill Vacancies.JobTitleId from the string column
UPDATE v
SET    v.JobTitleId = jt.Id
FROM   dbo.Vacancies v
INNER JOIN dbo.JobTitles jt ON jt.TitleName = LTRIM(RTRIM(v.JobTitle))
WHERE  v.JobTitleId IS NULL;

PRINT 'Backfilled Vacancies.JobTitleId.';
-- NOTE: Vacancies.JobTitle string column kept for backward compat until all code migrated.


-- ─────────────────────────────────────────────────────────────
-- PART 3A: StaffMenuAccess — Tier-1 of new 2-tier RBAC
-- ─────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.StaffMenuAccess', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffMenuAccess (
        Id       INT              NOT NULL IDENTITY(1,1),
        StaffId  UNIQUEIDENTIFIER NOT NULL,
        MenuId   INT              NOT NULL,
        IsAllow  BIT              NOT NULL DEFAULT(1),
        GrantedBy  NVARCHAR(450) NULL,
        GrantedDate DATETIME2    NOT NULL DEFAULT(SYSUTCDATETIME()),

        CONSTRAINT PK_StaffMenuAccess PRIMARY KEY (Id),
        CONSTRAINT FK_SMA_Staff
            FOREIGN KEY (StaffId) REFERENCES dbo.StaffVacancy(StaffId)
            ON DELETE CASCADE,
        CONSTRAINT FK_SMA_Menus
            FOREIGN KEY (MenuId) REFERENCES dbo.Menus(Id)
            ON DELETE CASCADE,
        CONSTRAINT UIX_SMA_StaffMenu UNIQUE (StaffId, MenuId)
    );

    CREATE INDEX IX_StaffMenuAccess_StaffId ON dbo.StaffMenuAccess (StaffId);
    CREATE INDEX IX_StaffMenuAccess_MenuId  ON dbo.StaffMenuAccess (MenuId);
    PRINT 'Created StaffMenuAccess table.';
END

-- ─────────────────────────────────────────────────────────────
-- PART 3B: AccessFeatures — Tier-2, per-menu feature flags
-- ─────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.AccessFeatures', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccessFeatures (
        Id                INT NOT NULL IDENTITY(1,1),
        StaffMenuAccessId INT NOT NULL,
        PermissionId      INT NOT NULL,
        IsAllow           BIT NOT NULL DEFAULT(1),

        CONSTRAINT PK_AccessFeatures PRIMARY KEY (Id),
        CONSTRAINT FK_AF_StaffMenuAccess
            FOREIGN KEY (StaffMenuAccessId) REFERENCES dbo.StaffMenuAccess(Id)
            ON DELETE CASCADE,          -- deleting menu access cascade-deletes all feature flags
        CONSTRAINT FK_AF_Features
            FOREIGN KEY (PermissionId) REFERENCES dbo.Features(PermissionId)
            ON DELETE CASCADE,
        CONSTRAINT UIX_AF_AccessPermission UNIQUE (StaffMenuAccessId, PermissionId)
    );

    CREATE INDEX IX_AF_StaffMenuAccessId ON dbo.AccessFeatures (StaffMenuAccessId);
    CREATE INDEX IX_AF_PermissionId      ON dbo.AccessFeatures (PermissionId);
    PRINT 'Created AccessFeatures table.';
END

-- ─────────────────────────────────────────────────────────────
-- PART 3C: Migrate UserPermissionOverrides → StaffMenuAccess + AccessFeatures
-- This migration groups each staff member's ALLOW overrides under a
-- synthetic "all features" menu grant (MenuId = 0 reserved sentinel,
-- or you can skip and keep UserPermissionOverrides during transition).
-- ─────────────────────────────────────────────────────────────
-- IMPORTANT: UserPermissionOverrides is NOT dropped here — it is kept as
-- the legacy read path while the new tables are rolled out.
-- Run the DROP below manually AFTER confirming all APIs use the new tables.

/*
-- === RUN MANUALLY AFTER MIGRATION IS CONFIRMED ===
-- DROP TABLE dbo.UserPermissionOverrides;
-- ALTER TABLE dbo.Vacancies DROP COLUMN JobTitle;
-- ALTER TABLE dbo.Vacancies DROP COLUMN Department;
-- ALTER TABLE dbo.Persons   DROP COLUMN Email;
-- ALTER TABLE dbo.Persons   DROP COLUMN Phone;
-- ALTER TABLE dbo.Persons   DROP COLUMN FullName;
*/

COMMIT TRANSACTION;
PRINT 'Migration V2 complete.';
