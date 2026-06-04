-- Align Account database with Frontend (React) routes and person-based RBAC.
-- Safe to run multiple times on (localdb)\MSSQLLocalDB, database Account.

USE [Account];
GO

SET NOCOUNT ON;

-- ── 1. Normalize menu routes (matches POST /api/menus/sync-routes) ─────────
UPDATE dbo.Menus SET Route = '/access/groups' WHERE Route IN ('/ACCESS/GROUPS', '/Access/Groups', '/access/group');
UPDATE dbo.Menus SET Route = '/organization'   WHERE Route = '/groups/hierarchy';
UPDATE dbo.Menus SET Route = '/hr/vacancies'   WHERE Route IN ('/groups/registration', '/hr/positions');
UPDATE dbo.Menus SET Route = '/hr/staff'       WHERE Route = '/groups/staff';
UPDATE dbo.Menus SET Route = '/hr/staff/register' WHERE Route = '/staff/register';
UPDATE dbo.Menus SET Route = LOWER(Route) WHERE Route IS NOT NULL AND Route <> LOWER(Route);

PRINT 'Menu routes normalized.';

-- ── 2. PersonMenus / PersonFeatures (if missing) ───────────────────────────
IF OBJECT_ID(N'dbo.PersonMenus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PersonMenus (
        PersonId     uniqueidentifier NOT NULL,
        MenuId       int              NOT NULL,
        GrantedBy    nvarchar(450)    NULL,
        GrantedOnUtc datetime2        NOT NULL,
        CONSTRAINT PK_PersonMenus PRIMARY KEY (PersonId, MenuId),
        CONSTRAINT FK_PersonMenus_Menus_MenuId FOREIGN KEY (MenuId) REFERENCES dbo.Menus (Id) ON DELETE CASCADE,
        CONSTRAINT FK_PersonMenus_Persons_PersonId FOREIGN KEY (PersonId) REFERENCES dbo.Persons (PersonId) ON DELETE CASCADE
    );
    CREATE INDEX IX_PersonMenus_MenuId ON dbo.PersonMenus (MenuId);
    CREATE INDEX IX_PersonMenus_PersonId ON dbo.PersonMenus (PersonId);
    PRINT 'Created PersonMenus.';
END

IF OBJECT_ID(N'dbo.PersonFeatures', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PersonFeatures (
        PersonId       uniqueidentifier NOT NULL,
        PermissionId   int              NOT NULL,
        GrantedBy      nvarchar(450)    NULL,
        GrantedOnUtc   datetime2        NOT NULL,
        CONSTRAINT PK_PersonFeatures PRIMARY KEY (PersonId, PermissionId),
        CONSTRAINT FK_PersonFeatures_Features_PermissionId FOREIGN KEY (PermissionId) REFERENCES dbo.Features (PermissionId) ON DELETE CASCADE,
        CONSTRAINT FK_PersonFeatures_Persons_PersonId FOREIGN KEY (PersonId) REFERENCES dbo.Persons (PersonId) ON DELETE CASCADE
    );
    CREATE INDEX IX_PersonFeatures_PermissionId ON dbo.PersonFeatures (PermissionId);
    CREATE INDEX IX_PersonFeatures_PersonId ON dbo.PersonFeatures (PersonId);
    PRINT 'Created PersonFeatures.';
END

-- ── 3. EF migration history (orphan migrations without Designer chain) ─────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = N'20260603160000_NormalizeMenuRoutes')
    INSERT INTO [__EFMigrationsHistory] (MigrationId, ProductVersion)
    VALUES (N'20260603160000_NormalizeMenuRoutes', N'9.0.5');

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = N'20260604120000_AddPersonMenuAndPersonFeature')
    INSERT INTO [__EFMigrationsHistory] (MigrationId, ProductVersion)
    VALUES (N'20260604120000_AddPersonMenuAndPersonFeature', N'9.0.5');

PRINT 'EF migration history updated.';

-- ── 4. Verify ──────────────────────────────────────────────────────────────
SELECT MigrationId FROM [__EFMigrationsHistory] WHERE MigrationId LIKE '20260604%' OR MigrationId LIKE '2026060316%';
SELECT Id, Title, Route FROM dbo.Menus WHERE Route IS NOT NULL ORDER BY Route;
