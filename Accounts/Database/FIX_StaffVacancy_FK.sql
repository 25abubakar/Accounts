-- ============================================================
-- FIX: Drop stale FK_StaffVacancy_AspNetUsers constraint
-- 
-- This FK was created by an old migration that stored IdentityUserId
-- directly on StaffVacancy. After the schema refactor, StaffVacancy
-- links to Persons (not AspNetUsers) directly. The old FK is now
-- a dangling constraint causing INSERT failures.
--
-- Run this script ONCE on the database to remove it.
-- ============================================================

-- Drop the stale FK if it exists
IF EXISTS (
    SELECT 1
    FROM   INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE  CONSTRAINT_TYPE = 'FOREIGN KEY'
    AND    TABLE_NAME       = 'StaffVacancy'
    AND    CONSTRAINT_NAME  = 'FK_StaffVacancy_AspNetUsers'
)
BEGIN
    ALTER TABLE [dbo].[StaffVacancy]
        DROP CONSTRAINT [FK_StaffVacancy_AspNetUsers];
    PRINT 'Dropped FK_StaffVacancy_AspNetUsers';
END
ELSE
BEGIN
    PRINT 'FK_StaffVacancy_AspNetUsers does not exist — nothing to drop.';
END

-- Also drop any stale IdentityUserId column on StaffVacancy if it exists
IF COL_LENGTH('dbo.StaffVacancy', 'IdentityUserId') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[StaffVacancy]
        DROP COLUMN [IdentityUserId];
    PRINT 'Dropped IdentityUserId column from StaffVacancy';
END
