-- ═══════════════════════════════════════════════════════════════════════════════
-- RBAC SCHEMA REFACTOR: String FeatureKey → Integer PermissionId FK
-- ═══════════════════════════════════════════════════════════════════════════════
-- This migration refactors the RBAC system to use integer FK instead of string keys
-- for optimal query performance and to eliminate N+1 database query loops.
--
-- EXECUTION ORDER:
--   1. Add new integer PK (PermissionId) to Features table
--   2. Add new integer FK columns to dependent tables
--   3. Migrate data: populate new FK columns based on existing FeatureKey values
--   4. Add indexes for optimal query performance
--   5. (Optional) Drop old FeatureKey FK columns if backward compatibility not needed
--
-- AUTHOR: Senior .NET Backend Architect
-- DATE: 2026-06-04
-- ═══════════════════════════════════════════════════════════════════════════════

BEGIN TRANSACTION;

PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT 'PHASE 1: ALTER Features TABLE - ADD INTEGER PK';
PRINT '═══════════════════════════════════════════════════════════════════════════════';

-- Step 1: Drop existing FK constraints referencing Features.FeatureKey
PRINT 'Dropping existing FK constraints...';

ALTER TABLE [dbo].[AccessGroupFeatures] DROP CONSTRAINT IF EXISTS [FK_AccessGroupFeatures_Features_FeatureKey];
ALTER TABLE [dbo].[DepartmentAccessMatrix] DROP CONSTRAINT IF EXISTS [FK_DepartmentAccessMatrix_Features_FeatureKey];
ALTER TABLE [dbo].[RolePermissions] DROP CONSTRAINT IF EXISTS [FK_RolePermissions_Features_FeatureKey];
ALTER TABLE [dbo].[UserPermissionOverrides] DROP CONSTRAINT IF EXISTS [FK_UserPermissionOverrides_Features_FeatureKey];

-- Step 2: Drop PK constraint on FeatureKey
PRINT 'Dropping PK constraint on Features.FeatureKey...';
ALTER TABLE [dbo].[Features] DROP CONSTRAINT IF EXISTS [PK_Features];

-- Step 3: Add new integer IDENTITY column as PK
PRINT 'Adding new PermissionId column to Features...';
ALTER TABLE [dbo].[Features]
ADD [PermissionId] INT IDENTITY(1,1) NOT NULL;

ALTER TABLE [dbo].[Features]
ADD CONSTRAINT [PK_Features] PRIMARY KEY CLUSTERED ([PermissionId]);

-- Step 4: Make FeatureKey unique but not PK
PRINT 'Creating unique index on FeatureKey for backward compatibility...';
CREATE UNIQUE NONCLUSTERED INDEX [IX_Features_FeatureKey] 
ON [dbo].[Features]([FeatureKey]);

-- Step 5: Add CreatedDate column if not exists
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Features]') AND name = 'CreatedDate')
BEGIN
    ALTER TABLE [dbo].[Features]
    ADD [CreatedDate] DATETIME NOT NULL DEFAULT GETDATE();
END;

PRINT '✓ Features table refactored successfully';
PRINT '';

PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT 'PHASE 2: ADD PermissionId FK COLUMNS TO DEPENDENT TABLES';
PRINT '═══════════════════════════════════════════════════════════════════════════════';

-- RolePermissions
PRINT 'Adding PermissionId to RolePermissions...';
ALTER TABLE [dbo].[RolePermissions]
ADD [PermissionId] INT NULL; -- Nullable temporarily for data migration

-- UserPermissionOverrides
PRINT 'Adding PermissionId to UserPermissionOverrides...';
ALTER TABLE [dbo].[UserPermissionOverrides]
ADD [PermissionId] INT NULL;

-- DepartmentAccessMatrix
PRINT 'Adding PermissionId to DepartmentAccessMatrix...';
ALTER TABLE [dbo].[DepartmentAccessMatrix]
ADD [PermissionId] INT NULL;

-- AccessGroupFeatures
PRINT 'Adding PermissionId to AccessGroupFeatures...';
ALTER TABLE [dbo].[AccessGroupFeatures]
ADD [PermissionId] INT NULL;

PRINT '✓ New FK columns added successfully';
PRINT '';

PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT 'PHASE 3: DATA MIGRATION - POPULATE PermissionId FROM FeatureKey';
PRINT '═══════════════════════════════════════════════════════════════════════════════';

-- Update RolePermissions
PRINT 'Migrating RolePermissions data...';
UPDATE rp
SET rp.PermissionId = f.PermissionId
FROM [dbo].[RolePermissions] rp
INNER JOIN [dbo].[Features] f ON rp.FeatureKey = f.FeatureKey;

DECLARE @RolePermsUpdated INT = @@ROWCOUNT;
PRINT CONCAT('  → Updated ', @RolePermsUpdated, ' rows');

-- Update UserPermissionOverrides
PRINT 'Migrating UserPermissionOverrides data...';
UPDATE upo
SET upo.PermissionId = f.PermissionId
FROM [dbo].[UserPermissionOverrides] upo
INNER JOIN [dbo].[Features] f ON upo.FeatureKey = f.FeatureKey;

DECLARE @UserOverridesUpdated INT = @@ROWCOUNT;
PRINT CONCAT('  → Updated ', @UserOverridesUpdated, ' rows');

-- Update DepartmentAccessMatrix
PRINT 'Migrating DepartmentAccessMatrix data...';
UPDATE dam
SET dam.PermissionId = f.PermissionId
FROM [dbo].[DepartmentAccessMatrix] dam
INNER JOIN [dbo].[Features] f ON dam.FeatureKey = f.FeatureKey;

DECLARE @MatrixUpdated INT = @@ROWCOUNT;
PRINT CONCAT('  → Updated ', @MatrixUpdated, ' rows');

-- Update AccessGroupFeatures
PRINT 'Migrating AccessGroupFeatures data...';
UPDATE agf
SET agf.PermissionId = f.PermissionId
FROM [dbo].[AccessGroupFeatures] agf
INNER JOIN [dbo].[Features] f ON agf.FeatureKey = f.FeatureKey;

DECLARE @GroupFeaturesUpdated INT = @@ROWCOUNT;
PRINT CONCAT('  → Updated ', @GroupFeaturesUpdated, ' rows');

PRINT '✓ Data migration completed successfully';
PRINT '';

PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT 'PHASE 4: MAKE PermissionId NOT NULL & ADD FK CONSTRAINTS';
PRINT '═══════════════════════════════════════════════════════════════════════════════';

-- Make columns NOT NULL
PRINT 'Setting PermissionId columns to NOT NULL...';
ALTER TABLE [dbo].[RolePermissions] ALTER COLUMN [PermissionId] INT NOT NULL;
ALTER TABLE [dbo].[UserPermissionOverrides] ALTER COLUMN [PermissionId] INT NOT NULL;
ALTER TABLE [dbo].[DepartmentAccessMatrix] ALTER COLUMN [PermissionId] INT NOT NULL;
ALTER TABLE [dbo].[AccessGroupFeatures] ALTER COLUMN [PermissionId] INT NOT NULL;

-- Add FK constraints
PRINT 'Adding FK constraints...';

ALTER TABLE [dbo].[RolePermissions]
ADD CONSTRAINT [FK_RolePermissions_Features_PermissionId]
FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Features]([PermissionId])
ON DELETE CASCADE;

ALTER TABLE [dbo].[UserPermissionOverrides]
ADD CONSTRAINT [FK_UserPermissionOverrides_Features_PermissionId]
FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Features]([PermissionId])
ON DELETE CASCADE;

ALTER TABLE [dbo].[DepartmentAccessMatrix]
ADD CONSTRAINT [FK_DepartmentAccessMatrix_Features_PermissionId]
FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Features]([PermissionId])
ON DELETE CASCADE;

ALTER TABLE [dbo].[AccessGroupFeatures]
ADD CONSTRAINT [FK_AccessGroupFeatures_Features_PermissionId]
FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Features]([PermissionId])
ON DELETE CASCADE;

PRINT '✓ FK constraints added successfully';
PRINT '';

PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT 'PHASE 5: ADD OPTIMIZED INDEXES FOR PERFORMANCE';
PRINT '═══════════════════════════════════════════════════════════════════════════════';

-- RolePermissions: Covering indexes for common query patterns
PRINT 'Creating indexes on RolePermissions...';
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RolePermissions_JobTitle' AND object_id = OBJECT_ID('RolePermissions'))
    CREATE NONCLUSTERED INDEX [IX_RolePermissions_JobTitle] ON [dbo].[RolePermissions]([JobTitle]);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RolePermissions_JobTitle_DeptId' AND object_id = OBJECT_ID('RolePermissions'))
    CREATE NONCLUSTERED INDEX [IX_RolePermissions_JobTitle_DeptId] ON [dbo].[RolePermissions]([JobTitle], [DeptId]);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RolePermissions_PermissionId' AND object_id = OBJECT_ID('RolePermissions'))
    CREATE NONCLUSTERED INDEX [IX_RolePermissions_PermissionId] ON [dbo].[RolePermissions]([PermissionId]);

-- Drop old unique index and recreate with PermissionId
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RolePermissions_JobTitle_DeptId_FeatureKey' AND object_id = OBJECT_ID('RolePermissions'))
    DROP INDEX [IX_RolePermissions_JobTitle_DeptId_FeatureKey] ON [dbo].[RolePermissions];

CREATE UNIQUE NONCLUSTERED INDEX [IX_RolePermissions_JobTitle_DeptId_PermissionId]
ON [dbo].[RolePermissions]([JobTitle], [DeptId], [PermissionId]);

-- UserPermissionOverrides: Covering indexes
PRINT 'Creating indexes on UserPermissionOverrides...';
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserPermissionOverrides_StaffId' AND object_id = OBJECT_ID('UserPermissionOverrides'))
    CREATE NONCLUSTERED INDEX [IX_UserPermissionOverrides_StaffId] ON [dbo].[UserPermissionOverrides]([StaffId]);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserPermissionOverrides_PermissionId' AND object_id = OBJECT_ID('UserPermissionOverrides'))
    CREATE NONCLUSTERED INDEX [IX_UserPermissionOverrides_PermissionId] ON [dbo].[UserPermissionOverrides]([PermissionId]);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserPermissionOverrides_StaffId_Status' AND object_id = OBJECT_ID('UserPermissionOverrides'))
    CREATE NONCLUSTERED INDEX [IX_UserPermissionOverrides_StaffId_Status] ON [dbo].[UserPermissionOverrides]([StaffId], [Status]);

-- Drop old unique index and recreate with PermissionId
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserPermissionOverrides_StaffId_FeatureKey' AND object_id = OBJECT_ID('UserPermissionOverrides'))
    DROP INDEX [IX_UserPermissionOverrides_StaffId_FeatureKey] ON [dbo].[UserPermissionOverrides];

CREATE UNIQUE NONCLUSTERED INDEX [IX_UserPermissionOverrides_StaffId_PermissionId]
ON [dbo].[UserPermissionOverrides]([StaffId], [PermissionId]);

-- DepartmentAccessMatrix: Covering indexes
PRINT 'Creating indexes on DepartmentAccessMatrix...';
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DepartmentAccessMatrix_StaffId' AND object_id = OBJECT_ID('DepartmentAccessMatrix'))
    CREATE NONCLUSTERED INDEX [IX_DepartmentAccessMatrix_StaffId] ON [dbo].[DepartmentAccessMatrix]([StaffId]);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DepartmentAccessMatrix_DeptId' AND object_id = OBJECT_ID('DepartmentAccessMatrix'))
    CREATE NONCLUSTERED INDEX [IX_DepartmentAccessMatrix_DeptId] ON [dbo].[DepartmentAccessMatrix]([DeptId]);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DepartmentAccessMatrix_PermissionId' AND object_id = OBJECT_ID('DepartmentAccessMatrix'))
    CREATE NONCLUSTERED INDEX [IX_DepartmentAccessMatrix_PermissionId] ON [dbo].[DepartmentAccessMatrix]([PermissionId]);

-- Drop old unique index and recreate with PermissionId
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DepartmentAccessMatrix_StaffId_FeatureKey' AND object_id = OBJECT_ID('DepartmentAccessMatrix'))
    DROP INDEX [IX_DepartmentAccessMatrix_StaffId_FeatureKey] ON [dbo].[DepartmentAccessMatrix];

CREATE UNIQUE NONCLUSTERED INDEX [IX_DepartmentAccessMatrix_StaffId_PermissionId]
ON [dbo].[DepartmentAccessMatrix]([StaffId], [PermissionId]);

-- AccessGroupFeatures: Covering indexes
PRINT 'Creating indexes on AccessGroupFeatures...';
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessGroupFeatures_GroupId' AND object_id = OBJECT_ID('AccessGroupFeatures'))
    CREATE NONCLUSTERED INDEX [IX_AccessGroupFeatures_GroupId] ON [dbo].[AccessGroupFeatures]([GroupId]);

-- Drop old PK and recreate with PermissionId
ALTER TABLE [dbo].[AccessGroupFeatures] DROP CONSTRAINT IF EXISTS [PK_AccessGroupFeatures];
ALTER TABLE [dbo].[AccessGroupFeatures]
ADD CONSTRAINT [PK_AccessGroupFeatures] PRIMARY KEY CLUSTERED ([GroupId], [PermissionId]);

PRINT '✓ All indexes created successfully';
PRINT '';

PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT 'PHASE 6 (OPTIONAL): CLEANUP - REMOVE OLD FeatureKey COLUMNS';
PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT 'NOTE: Uncomment the following section if you want to drop the old FeatureKey columns';
PRINT '      This will BREAK backward compatibility with old code!';
PRINT '';

/*
-- Remove FeatureKey columns from dependent tables (BREAKING CHANGE!)
ALTER TABLE [dbo].[RolePermissions] DROP COLUMN [FeatureKey];
ALTER TABLE [dbo].[UserPermissionOverrides] DROP COLUMN [FeatureKey];
ALTER TABLE [dbo].[DepartmentAccessMatrix] DROP COLUMN [FeatureKey];
ALTER TABLE [dbo].[AccessGroupFeatures] DROP COLUMN [FeatureKey];

PRINT '✓ Old FeatureKey columns removed';
*/

COMMIT TRANSACTION;

PRINT '';
PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT '✓✓✓ MIGRATION COMPLETED SUCCESSFULLY ✓✓✓';
PRINT '═══════════════════════════════════════════════════════════════════════════════';
PRINT '';
PRINT 'SUMMARY:';
PRINT '  - Features table now uses PermissionId (INT) as PK';
PRINT '  - All dependent tables migrated to use PermissionId FK';
PRINT '  - Optimized indexes created for maximum query performance';
PRINT '  - FeatureKey columns retained for backward compatibility';
PRINT '';
PRINT 'NEXT STEPS:';
PRINT '  1. Test new OptimizedMenuController endpoints (/api/v2/menu/session)';
PRINT '  2. Update frontend to use new API endpoints';
PRINT '  3. Monitor query performance (should see <5 queries per login)';
PRINT '  4. Gradually migrate old code to use PermissionId instead of FeatureKey';
PRINT '  5. Once all code migrated, uncomment Phase 6 to drop old columns';
PRINT '';
PRINT '═══════════════════════════════════════════════════════════════════════════════';

GO
