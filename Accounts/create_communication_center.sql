-- Communication Center Database Schema
-- Run this script to create the Communication Center tables

-- 1. AppLookupTypes
CREATE TABLE dbo.AppLookupTypes (
    LookupTypeId INT IDENTITY(1,1) PRIMARY KEY,
    LookupTypeCode NVARCHAR(100) NOT NULL UNIQUE,
    LookupTypeName NVARCHAR(150) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedOn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- 2. AppLookupValues
CREATE TABLE dbo.AppLookupValues (
    LookupValueId INT IDENTITY(1,1) PRIMARY KEY,
    LookupTypeId INT NOT NULL,
    ValueCode NVARCHAR(100) NOT NULL,
    DisplayText NVARCHAR(150) NOT NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    IsDefault BIT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    MetadataJson NVARCHAR(MAX) NULL,
    CreatedOn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AppLookupValues_AppLookupTypes FOREIGN KEY (LookupTypeId) REFERENCES dbo.AppLookupTypes(LookupTypeId)
);
GO

CREATE UNIQUE INDEX UX_AppLookupValues_Type_ValueCode ON dbo.AppLookupValues(LookupTypeId, ValueCode);
GO

-- 3. AppMenuDefinitions
CREATE TABLE dbo.AppMenuDefinitions (
    MenuDefinitionId INT IDENTITY(1,1) PRIMARY KEY,
    MenuCode NVARCHAR(150) NOT NULL UNIQUE,
    MenuName NVARCHAR(200) NOT NULL,
    ModuleName NVARCHAR(150) NULL,
    ParentMenuCode NVARCHAR(150) NULL,
    RoutePath NVARCHAR(300) NULL,
    IconCss NVARCHAR(100) NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedOn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- 4. AppNotes
CREATE TABLE dbo.AppNotes (
    NoteId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NULL,
    OrgUnitId INT NULL,
    Title NVARCHAR(250) NOT NULL,
    NoteBody NVARCHAR(MAX) NOT NULL,
    NoteTypeCode NVARCHAR(100) NOT NULL,
    SourceTypeCode NVARCHAR(100) NOT NULL,
    CategoryCode NVARCHAR(100) NULL,
    PriorityCode NVARCHAR(100) NOT NULL,
    VisibilityTypeCode NVARCHAR(100) NOT NULL,
    MenuCode NVARCHAR(150) NULL,
    ModuleName NVARCHAR(150) NULL,
    EntityType NVARCHAR(100) NULL,
    EntityId NVARCHAR(100) NULL,
    StartDateUtc DATETIME2 NULL,
    EndDateUtc DATETIME2 NULL,
    IsPublished BIT NOT NULL DEFAULT 1,
    IsPinned BIT NOT NULL DEFAULT 0,
    IsPopup BIT NOT NULL DEFAULT 0,
    RequireAcknowledgement BIT NOT NULL DEFAULT 0,
    AllowDismiss BIT NOT NULL DEFAULT 1,
    IsActive BIT NOT NULL DEFAULT 1,
    IsDeleted BIT NOT NULL DEFAULT 0,
    CreatedBy NVARCHAR(100) NULL,
    CreatedOnUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedBy NVARCHAR(100) NULL,
    UpdatedOnUtc DATETIME2 NULL,
    DeletedBy NVARCHAR(100) NULL,
    DeletedOnUtc DATETIME2 NULL
);
GO

CREATE INDEX IX_AppNotes_MenuCode ON dbo.AppNotes(MenuCode);
CREATE INDEX IX_AppNotes_SourceTypeCode ON dbo.AppNotes(SourceTypeCode);
CREATE INDEX IX_AppNotes_VisibilityTypeCode ON dbo.AppNotes(VisibilityTypeCode);
GO

-- 5. AppNoteTargets
CREATE TABLE dbo.AppNoteTargets (
    NoteTargetId INT IDENTITY(1,1) PRIMARY KEY,
    NoteId INT NOT NULL,
    TargetTypeCode NVARCHAR(100) NOT NULL,
    TargetValue NVARCHAR(150) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedOnUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AppNoteTargets_AppNotes FOREIGN KEY (NoteId) REFERENCES dbo.AppNotes(NoteId) ON DELETE CASCADE
);
GO

CREATE INDEX IX_AppNoteTargets_NoteId ON dbo.AppNoteTargets(NoteId);
GO

-- 6. AppNoteUserStatuses
CREATE TABLE dbo.AppNoteUserStatuses (
    NoteUserStatusId INT IDENTITY(1,1) PRIMARY KEY,
    NoteId INT NOT NULL,
    UserId NVARCHAR(100) NOT NULL,
    IsRead BIT NOT NULL DEFAULT 0,
    ReadOnUtc DATETIME2 NULL,
    IsAcknowledged BIT NOT NULL DEFAULT 0,
    AcknowledgedOnUtc DATETIME2 NULL,
    IsDismissed BIT NOT NULL DEFAULT 0,
    DismissedOnUtc DATETIME2 NULL,
    CreatedOnUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedOnUtc DATETIME2 NULL,
    CONSTRAINT FK_AppNoteUserStatuses_AppNotes FOREIGN KEY (NoteId) REFERENCES dbo.AppNotes(NoteId) ON DELETE CASCADE
);
GO

CREATE UNIQUE INDEX UX_AppNoteUserStatuses_Note_UserId ON dbo.AppNoteUserStatuses(NoteId, UserId);
GO

-- 7. AppNoteAttachments
CREATE TABLE dbo.AppNoteAttachments (
    AttachmentId INT IDENTITY(1,1) PRIMARY KEY,
    NoteId INT NOT NULL,
    FileName NVARCHAR(250) NULL,
    FilePath NVARCHAR(500) NULL,
    FileType NVARCHAR(100) NULL,
    FileSizeBytes BIGINT NULL,
    ExternalUrl NVARCHAR(500) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedBy NVARCHAR(100) NULL,
    CreatedOnUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AppNoteAttachments_AppNotes FOREIGN KEY (NoteId) REFERENCES dbo.AppNotes(NoteId) ON DELETE CASCADE
);
GO

CREATE INDEX IX_AppNoteAttachments_NoteId ON dbo.AppNoteAttachments(NoteId);
GO

-- Seed Lookup Types
INSERT INTO dbo.AppLookupTypes (LookupTypeCode, LookupTypeName, IsActive, CreatedOn)
VALUES 
    ('NOTE_TYPE', 'Note Type', 1, SYSUTCDATETIME()),
    ('SOURCE_TYPE', 'Source Type', 1, SYSUTCDATETIME()),
    ('PRIORITY', 'Priority', 1, SYSUTCDATETIME()),
    ('VISIBILITY_TYPE', 'Visibility Type', 1, SYSUTCDATETIME()),
    ('TARGET_TYPE', 'Target Type', 1, SYSUTCDATETIME()),
    ('CATEGORY', 'Category', 1, SYSUTCDATETIME());
GO

-- Seed Lookup Values
DECLARE @NoteTypeId INT = (SELECT LookupTypeId FROM dbo.AppLookupTypes WHERE LookupTypeCode = 'NOTE_TYPE');
DECLARE @SourceTypeId INT = (SELECT LookupTypeId FROM dbo.AppLookupTypes WHERE LookupTypeCode = 'SOURCE_TYPE');
DECLARE @PriorityId INT = (SELECT LookupTypeId FROM dbo.AppLookupTypes WHERE LookupTypeCode = 'PRIORITY');
DECLARE @VisibilityId INT = (SELECT LookupTypeId FROM dbo.AppLookupTypes WHERE LookupTypeCode = 'VISIBILITY_TYPE');
DECLARE @TargetId INT = (SELECT LookupTypeId FROM dbo.AppLookupTypes WHERE LookupTypeCode = 'TARGET_TYPE');
DECLARE @CategoryId INT = (SELECT LookupTypeId FROM dbo.AppLookupTypes WHERE LookupTypeCode = 'CATEGORY');

INSERT INTO dbo.AppLookupValues (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, CreatedOn)
VALUES
    (@NoteTypeId, 'ANNOUNCEMENT', 'Announcement', 1, 0, 1, SYSUTCDATETIME()),
    (@NoteTypeId, 'INSTRUCTION', 'Instruction', 2, 1, 1, SYSUTCDATETIME()),
    (@NoteTypeId, 'WARNING', 'Warning', 3, 0, 1, SYSUTCDATETIME()),
    (@NoteTypeId, 'USER_NOTE', 'User Note', 4, 0, 1, SYSUTCDATETIME()),
    (@NoteTypeId, 'HISTORY', 'History', 5, 0, 1, SYSUTCDATETIME()),
    (@NoteTypeId, 'FOLLOW_UP', 'Follow-up', 6, 0, 1, SYSUTCDATETIME()),
    (@SourceTypeId, 'ADMIN', 'Admin', 1, 1, 1, SYSUTCDATETIME()),
    (@SourceTypeId, 'USER', 'User', 2, 0, 1, SYSUTCDATETIME()),
    (@SourceTypeId, 'SYSTEM', 'System', 3, 0, 1, SYSUTCDATETIME()),
    (@PriorityId, 'LOW', 'Low', 1, 0, 1, SYSUTCDATETIME()),
    (@PriorityId, 'NORMAL', 'Normal', 2, 1, 1, SYSUTCDATETIME()),
    (@PriorityId, 'HIGH', 'High', 3, 0, 1, SYSUTCDATETIME()),
    (@PriorityId, 'CRITICAL', 'Critical', 4, 0, 1, SYSUTCDATETIME()),
    (@VisibilityId, 'GENERAL', 'General - Show Everywhere', 1, 1, 1, SYSUTCDATETIME()),
    (@VisibilityId, 'ALL_USERS', 'All Users', 2, 0, 1, SYSUTCDATETIME()),
    (@VisibilityId, 'MENU', 'Menu Specific', 3, 0, 1, SYSUTCDATETIME()),
    (@VisibilityId, 'RECORD', 'Record Specific', 4, 0, 1, SYSUTCDATETIME()),
    (@VisibilityId, 'USER', 'Specific User', 5, 0, 1, SYSUTCDATETIME()),
    (@VisibilityId, 'ROLE', 'Role Based', 6, 0, 1, SYSUTCDATETIME()),
    (@VisibilityId, 'PRIVATE', 'Private Note', 7, 0, 1, SYSUTCDATETIME()),
    (@TargetId, 'USER', 'User', 1, 0, 1, SYSUTCDATETIME()),
    (@TargetId, 'ROLE', 'Role', 2, 0, 1, SYSUTCDATETIME()),
    (@TargetId, 'OFFICE', 'Office', 3, 0, 1, SYSUTCDATETIME()),
    (@TargetId, 'DEPARTMENT', 'Department', 4, 0, 1, SYSUTCDATETIME()),
    (@CategoryId, 'GENERAL', 'General', 1, 1, 1, SYSUTCDATETIME()),
    (@CategoryId, 'BILLING', 'Billing', 2, 0, 1, SYSUTCDATETIME()),
    (@CategoryId, 'SCHEDULING', 'Scheduling', 3, 0, 1, SYSUTCDATETIME()),
    (@CategoryId, 'ALLERGY', 'Allergy', 4, 0, 1, SYSUTCDATETIME()),
    (@CategoryId, 'PROVIDER', 'Provider', 5, 0, 1, SYSUTCDATETIME()),
    (@CategoryId, 'CLAIM', 'Claim', 6, 0, 1, SYSUTCDATETIME());
GO

-- Seed Menu Definitions
INSERT INTO dbo.AppMenuDefinitions (MenuCode, MenuName, ModuleName, RoutePath, IconCss, SortOrder, IsActive, CreatedOn)
VALUES
    ('DASHBOARD', 'Dashboard', 'Core', '/dashboard', 'fa fa-home', 1, 1, SYSUTCDATETIME()),
    ('STAFF', 'Staff Members', 'HR', '/staff', 'fa fa-users', 2, 1, SYSUTCDATETIME()),
    ('PERSONS', 'Persons', 'HR', '/persons', 'fa fa-user', 3, 1, SYSUTCDATETIME()),
    ('VACANCIES', 'Vacancies', 'HR', '/vacancies', 'fa fa-briefcase', 4, 1, SYSUTCDATETIME()),
    ('ACCESS_GROUPS', 'Access Groups', 'Security', '/access/groups', 'fa fa-shield', 5, 1, SYSUTCDATETIME()),
    ('DEPT_MATRIX', 'Dept Matrix', 'Security', '/access/dept-matrix', 'fa fa-table', 6, 1, SYSUTCDATETIME()),
    ('ORG_TREE', 'Organization', 'Core', '/org-tree', 'fa fa-sitemap', 7, 1, SYSUTCDATETIME()),
    ('MENU_MANAGER', 'Menu Manager', 'Settings', '/settings/menus', 'fa fa-bars', 8, 1, SYSUTCDATETIME()),
    ('COMM_CENTER', 'Communication', 'Core', '/communication', 'fa fa-bell', 9, 1, SYSUTCDATETIME());
GO
