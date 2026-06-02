IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(128) NOT NULL,
    [ProviderKey] nvarchar(128) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(128) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260504105929_InitialCreate', N'9.0.5');

CREATE TABLE [dbo].[OrganizationTree] (
    [Id] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [Code] nvarchar(20) NULL,
    [Label] nvarchar(50) NOT NULL,
    [ParentId] int NULL,
    CONSTRAINT [PK_OrganizationTree] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrganizationTree_OrganizationTree_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[OrganizationTree] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_OrganizationTree_ParentId] ON [dbo].[OrganizationTree] ([ParentId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260505070331_AddOrganizationTree', N'9.0.5');

CREATE TABLE [Vacancies] (
    [VacancyId] int NOT NULL IDENTITY,
    [VacancyCode] nvarchar(50) NOT NULL,
    [JobTitle] nvarchar(100) NOT NULL,
    [Department] nvarchar(100) NULL,
    [IsFilled] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CreatedDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    [OrganizationId] int NOT NULL,
    CONSTRAINT [PK_Vacancies] PRIMARY KEY ([VacancyId]),
    CONSTRAINT [FK_Vacancies_OrganizationTree_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[OrganizationTree] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Staff] (
    [StaffId] int NOT NULL IDENTITY,
    [FullName] nvarchar(150) NOT NULL,
    [Email] nvarchar(150) NULL,
    [Phone] nvarchar(50) NULL,
    [JoiningDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    [VacancyId] int NULL,
    CONSTRAINT [PK_Staff] PRIMARY KEY ([StaffId]),
    CONSTRAINT [FK_Staff_Vacancies_VacancyId] FOREIGN KEY ([VacancyId]) REFERENCES [Vacancies] ([VacancyId]) ON DELETE SET NULL
);

CREATE INDEX [IX_Vacancies_OrganizationId] ON [Vacancies] ([OrganizationId]);

CREATE UNIQUE INDEX [IX_Staff_VacancyId] ON [Staff] ([VacancyId]) WHERE [VacancyId] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260506064049_AddVacancyAndStaff', N'9.0.5');

ALTER TABLE [Staff] ADD [PhotoUrl] nvarchar(500) NULL;

ALTER TABLE [dbo].[OrganizationTree] ADD [FlagUrl] nvarchar(500) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260506070926_AddFlagUrlAndPhotoUrl', N'9.0.5');

DROP TABLE [Staff];

DROP TABLE [Vacancies];

CREATE TABLE [Vacancies] (
    [VacancyId] uniqueidentifier NOT NULL DEFAULT (NEWID()),
    [VacancyCode] nvarchar(50) NOT NULL,
    [JobTitle] nvarchar(100) NOT NULL,
    [Department] nvarchar(100) NULL,
    [IsFilled] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CreatedDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    [OrganizationId] int NOT NULL,
    CONSTRAINT [PK_Vacancies] PRIMARY KEY ([VacancyId]),
    CONSTRAINT [FK_Vacancies_OrganizationTree_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[OrganizationTree] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Staff] (
    [StaffId] uniqueidentifier NOT NULL DEFAULT (NEWID()),
    [FullName] nvarchar(150) NOT NULL,
    [Email] nvarchar(150) NULL,
    [Phone] nvarchar(50) NULL,
    [PhotoUrl] nvarchar(500) NULL,
    [JoiningDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    [VacancyId] uniqueidentifier NULL,
    CONSTRAINT [PK_Staff] PRIMARY KEY ([StaffId]),
    CONSTRAINT [FK_Staff_Vacancies_VacancyId] FOREIGN KEY ([VacancyId]) REFERENCES [Vacancies] ([VacancyId]) ON DELETE SET NULL
);

CREATE INDEX [IX_Vacancies_OrganizationId] ON [Vacancies] ([OrganizationId]);

CREATE UNIQUE INDEX [IX_Staff_VacancyId] ON [Staff] ([VacancyId]) WHERE [VacancyId] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260507044956_GuidPrimaryKeys', N'9.0.5');

CREATE TABLE [Persons] (
    [PersonId] uniqueidentifier NOT NULL DEFAULT (NEWID()),
    [FullName] nvarchar(150) NOT NULL,
    [Phone] nvarchar(50) NULL,
    [Email] nvarchar(150) NULL,
    [Gender] nvarchar(20) NULL,
    [DateOfBirth] datetime2 NULL,
    [MaritalStatus] nvarchar(50) NULL,
    [ProfilePhotoUrl] nvarchar(500) NULL,
    [LoginId] nvarchar(30) NOT NULL,
    [IdentityUserId] nvarchar(450) NOT NULL,
    [CreatedDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT [PK_Persons] PRIMARY KEY ([PersonId])
);

CREATE UNIQUE INDEX [IX_Persons_LoginId] ON [Persons] ([LoginId]);

CREATE UNIQUE INDEX [IX_Persons_IdentityUserId] ON [Persons] ([IdentityUserId]);

CREATE TABLE [PersonAddresses] (
    [AddressId] uniqueidentifier NOT NULL DEFAULT (NEWID()),
    [PersonId] uniqueidentifier NOT NULL,
    [AddressType] nvarchar(20) NOT NULL,
    [AddressLine] nvarchar(250) NULL,
    [Country] nvarchar(100) NULL,
    [Province] nvarchar(100) NULL,
    [District] nvarchar(100) NULL,
    [City] nvarchar(100) NULL,
    [PostalCode] nvarchar(20) NULL,
    CONSTRAINT [PK_PersonAddresses] PRIMARY KEY ([AddressId]),
    CONSTRAINT [FK_PersonAddresses_Persons_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [Persons] ([PersonId]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_PersonAddresses_PersonId_AddressType] ON [PersonAddresses] ([PersonId], [AddressType]);

ALTER TABLE [Staff] ADD [PersonId] uniqueidentifier NULL;

CREATE UNIQUE INDEX [IX_Staff_PersonId] ON [Staff] ([PersonId]) WHERE [PersonId] IS NOT NULL;

ALTER TABLE [Staff] ADD CONSTRAINT [FK_Staff_Persons_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [Persons] ([PersonId]) ON DELETE SET NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260507130000_AddPersonsAndAddresses', N'9.0.5');

ALTER TABLE [Persons] ADD [BranchId] int NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260508050959_AddPersonBranchId', N'9.0.5');


CREATE OR ALTER VIEW dbo.vw_PersonProfiles AS
SELECT
    -- Person core
    p.PersonId,
    p.LoginId,
    p.FullName,
    p.Gender,
    p.DateOfBirth,
    p.MaritalStatus,
    p.Phone,
    p.Email,
    p.ProfilePhotoUrl,
    p.CreatedDate,
    p.BranchId,

    -- Org placement (Branch → Company → Country)
    branch.Name        AS BranchName,
    company.Name       AS CompanyName,
    country.Name       AS CountryName,
    country.FlagUrl    AS CountryFlag,

    -- Staff / Position info (NULL if not hired)
    s.StaffId,
    s.JoiningDate,
    v.VacancyId,
    v.VacancyCode,
    v.JobTitle,
    v.Department,
    CAST(CASE WHEN s.StaffId IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsHired,

    -- Current address
    ca.AddressLine  AS CurrentAddressLine,
    ca.Country      AS CurrentCountry,
    ca.Province     AS CurrentProvince,
    ca.District     AS CurrentDistrict,
    ca.City         AS CurrentCity,
    ca.PostalCode   AS CurrentPostalCode,

    -- Permanent address
    pa.AddressLine  AS PermanentAddressLine,
    pa.Country      AS PermanentCountry,
    pa.Province     AS PermanentProvince,
    pa.District     AS PermanentDistrict,
    pa.City         AS PermanentCity,
    pa.PostalCode   AS PermanentPostalCode

FROM dbo.Persons p

-- Org chain
LEFT JOIN dbo.OrganizationTree branch  ON branch.Id  = p.BranchId
LEFT JOIN dbo.OrganizationTree company ON company.Id = branch.ParentId
LEFT JOIN dbo.OrganizationTree country ON country.Id = company.ParentId

-- Staff & Vacancy (person may not be hired yet)
LEFT JOIN dbo.Staff   s ON s.PersonId  = p.PersonId
LEFT JOIN dbo.Vacancies v ON v.VacancyId = s.VacancyId

-- Addresses
LEFT JOIN dbo.PersonAddresses ca ON ca.PersonId = p.PersonId AND ca.AddressType = 'Current'
LEFT JOIN dbo.PersonAddresses pa ON pa.PersonId = p.PersonId AND pa.AddressType = 'Permanent';


INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260508120000_AddPersonProfileView', N'9.0.5');

DECLARE @var sysname;
SELECT @var = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Vacancies]') AND [c].[name] = N'VacancyId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [Vacancies] DROP CONSTRAINT [' + @var + '];');

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Staff]') AND [c].[name] = N'StaffId');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Staff] DROP CONSTRAINT [' + @var1 + '];');

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'PersonId');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var2 + '];');

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PersonAddresses]') AND [c].[name] = N'AddressId');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [PersonAddresses] DROP CONSTRAINT [' + @var3 + '];');

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260511063301_SyncModel', N'9.0.5');

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Vacancies]') AND [c].[name] = N'VacancyId');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Vacancies] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [Vacancies] ADD DEFAULT (NEWID()) FOR [VacancyId];

DECLARE @var5 sysname;
SELECT @var5 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Staff]') AND [c].[name] = N'StaffId');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Staff] DROP CONSTRAINT [' + @var5 + '];');
ALTER TABLE [Staff] ADD DEFAULT (NEWID()) FOR [StaffId];

DECLARE @var6 sysname;
SELECT @var6 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'PersonId');
IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var6 + '];');
ALTER TABLE [Persons] ADD DEFAULT (NEWID()) FOR [PersonId];

DECLARE @var7 sysname;
SELECT @var7 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PersonAddresses]') AND [c].[name] = N'AddressId');
IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [PersonAddresses] DROP CONSTRAINT [' + @var7 + '];');
ALTER TABLE [PersonAddresses] ADD DEFAULT (NEWID()) FOR [AddressId];


IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VacancyCounters')
BEGIN
    CREATE TABLE [VacancyCounters] (
        [Prefix] nvarchar(200) NOT NULL,
        [LastNumber] int NOT NULL DEFAULT 0,
        CONSTRAINT [PK_VacancyCounters] PRIMARY KEY ([Prefix])
    );
END

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260512131542_AddVacancyCounters', N'9.0.5');

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260514045710_AddIdentityTables', N'9.0.5');

CREATE TABLE [Menus] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(100) NOT NULL,
    [Icon] nvarchar(50) NULL,
    [Route] nvarchar(200) NULL,
    [ParentId] int NULL,
    [SortOrder] int NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Menus] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Menus_Menus_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Menus] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [MenuRoles] (
    [MenuId] int NOT NULL,
    [RoleName] nvarchar(50) NOT NULL,
    CONSTRAINT [PK_MenuRoles] PRIMARY KEY ([MenuId], [RoleName]),
    CONSTRAINT [FK_MenuRoles_Menus_MenuId] FOREIGN KEY ([MenuId]) REFERENCES [Menus] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_Menus_ParentId] ON [Menus] ([ParentId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260514063357_AddMenus', N'9.0.5');

CREATE TABLE [AccessGroups] (
    [GroupId] int NOT NULL IDENTITY,
    [GroupName] nvarchar(100) NOT NULL,
    [Description] nvarchar(250) NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [CreatedDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT [PK_AccessGroups] PRIMARY KEY ([GroupId])
);

CREATE TABLE [Features] (
    [FeatureKey] nvarchar(100) NOT NULL,
    [FeatureName] nvarchar(150) NOT NULL,
    [Module] nvarchar(100) NOT NULL,
    [Description] nvarchar(250) NULL,
    CONSTRAINT [PK_Features] PRIMARY KEY ([FeatureKey])
);

CREATE TABLE [StaffAccessGroups] (
    [StaffId] uniqueidentifier NOT NULL,
    [GroupId] int NOT NULL,
    [AssignedBy] nvarchar(450) NULL,
    [AssignedDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    [Note] nvarchar(250) NULL,
    CONSTRAINT [PK_StaffAccessGroups] PRIMARY KEY ([StaffId], [GroupId]),
    CONSTRAINT [FK_StaffAccessGroups_AccessGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [AccessGroups] ([GroupId]) ON DELETE CASCADE,
    CONSTRAINT [FK_StaffAccessGroups_Staff_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [Staff] ([StaffId]) ON DELETE CASCADE
);

CREATE TABLE [AccessGroupFeatures] (
    [GroupId] int NOT NULL,
    [FeatureKey] nvarchar(100) NOT NULL,
    CONSTRAINT [PK_AccessGroupFeatures] PRIMARY KEY ([GroupId], [FeatureKey]),
    CONSTRAINT [FK_AccessGroupFeatures_AccessGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [AccessGroups] ([GroupId]) ON DELETE CASCADE,
    CONSTRAINT [FK_AccessGroupFeatures_Features_FeatureKey] FOREIGN KEY ([FeatureKey]) REFERENCES [Features] ([FeatureKey]) ON DELETE CASCADE
);

CREATE TABLE [DepartmentAccessMatrix] (
    [Id] int NOT NULL IDENTITY,
    [StaffId] uniqueidentifier NOT NULL,
    [DeptId] int NOT NULL,
    [FeatureKey] nvarchar(100) NOT NULL,
    [HasAccess] bit NOT NULL DEFAULT CAST(0 AS bit),
    [GrantedBy] nvarchar(450) NULL,
    [GrantedDate] datetime2 NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT [PK_DepartmentAccessMatrix] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DepartmentAccessMatrix_Features_FeatureKey] FOREIGN KEY ([FeatureKey]) REFERENCES [Features] ([FeatureKey]) ON DELETE CASCADE,
    CONSTRAINT [FK_DepartmentAccessMatrix_OrganizationTree_DeptId] FOREIGN KEY ([DeptId]) REFERENCES [dbo].[OrganizationTree] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DepartmentAccessMatrix_Staff_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [Staff] ([StaffId]) ON DELETE CASCADE
);

CREATE INDEX [IX_AccessGroupFeatures_FeatureKey] ON [AccessGroupFeatures] ([FeatureKey]);

CREATE INDEX [IX_DepartmentAccessMatrix_DeptId] ON [DepartmentAccessMatrix] ([DeptId]);

CREATE INDEX [IX_DepartmentAccessMatrix_FeatureKey] ON [DepartmentAccessMatrix] ([FeatureKey]);

CREATE UNIQUE INDEX [IX_DepartmentAccessMatrix_StaffId_FeatureKey] ON [DepartmentAccessMatrix] ([StaffId], [FeatureKey]);

CREATE INDEX [IX_StaffAccessGroups_GroupId] ON [StaffAccessGroups] ([GroupId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260515061554_AddPBACTables', N'9.0.5');

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260515121159_AddHierarchicalRBAC', N'9.0.5');

CREATE TABLE [AppNoteUserStates] (
    [AppNoteUserStateId] int NOT NULL IDENTITY,
    [NoteId] int NOT NULL,
    [StaffId] nvarchar(100) NOT NULL,
    [IsRead] bit NOT NULL,
    [IsAcknowledged] bit NOT NULL,
    [IsDismissed] bit NOT NULL,
    [ReadOnUtc] datetime2 NULL,
    [AcknowledgedOnUtc] datetime2 NULL,
    [DismissedOnUtc] datetime2 NULL,
    CONSTRAINT [PK_AppNoteUserStates] PRIMARY KEY ([AppNoteUserStateId]),
    CONSTRAINT [FK_AppNoteUserStates_AppNotes_NoteId] FOREIGN KEY ([NoteId]) REFERENCES [AppNotes] ([NoteId]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_AppNoteUserStates_NoteId_StaffId] ON [AppNoteUserStates] ([NoteId], [StaffId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260601130027_AddAppNoteUserStates', N'9.0.5');

ALTER TABLE [DepartmentAccessMatrix] DROP CONSTRAINT [FK_DepartmentAccessMatrix_Staff_StaffId];

ALTER TABLE [StaffAccessGroups] DROP CONSTRAINT [FK_StaffAccessGroups_Staff_StaffId];

ALTER TABLE [UserPermissionOverrides] DROP CONSTRAINT [FK_UserPermissionOverrides_Staff_StaffId];

ALTER TABLE [Staff] DROP CONSTRAINT [FK_Staff_Persons_PersonId];

ALTER TABLE [Staff] DROP CONSTRAINT [FK_Staff_Vacancies_VacancyId];

EXEC sp_rename N'[Staff]', N'StaffVacancy', 'OBJECT';

ALTER TABLE [StaffVacancy] DROP CONSTRAINT [PK_Staff];

ALTER TABLE [StaffVacancy] ADD CONSTRAINT [PK_StaffVacancy] PRIMARY KEY ([StaffId]);

DROP INDEX [IX_Staff_PersonId] ON [StaffVacancy];

DROP INDEX [IX_Staff_VacancyId] ON [StaffVacancy];

ALTER TABLE [StaffVacancy] ADD [LoginId] nvarchar(50) NULL;


UPDATE sv
SET sv.LoginId = p.LoginId
FROM StaffVacancy sv
INNER JOIN Persons p ON p.PersonId = sv.PersonId
WHERE sv.LoginId IS NULL;


DECLARE @var8 sysname;
SELECT @var8 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StaffVacancy]') AND [c].[name] = N'Email');
IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [StaffVacancy] DROP CONSTRAINT [' + @var8 + '];');
ALTER TABLE [StaffVacancy] DROP COLUMN [Email];

DECLARE @var9 sysname;
SELECT @var9 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StaffVacancy]') AND [c].[name] = N'FullName');
IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [StaffVacancy] DROP CONSTRAINT [' + @var9 + '];');
ALTER TABLE [StaffVacancy] DROP COLUMN [FullName];

DECLARE @var10 sysname;
SELECT @var10 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StaffVacancy]') AND [c].[name] = N'JoiningDate');
IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [StaffVacancy] DROP CONSTRAINT [' + @var10 + '];');
ALTER TABLE [StaffVacancy] DROP COLUMN [JoiningDate];

DECLARE @var11 sysname;
SELECT @var11 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StaffVacancy]') AND [c].[name] = N'Phone');
IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [StaffVacancy] DROP CONSTRAINT [' + @var11 + '];');
ALTER TABLE [StaffVacancy] DROP COLUMN [Phone];

DECLARE @var12 sysname;
SELECT @var12 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StaffVacancy]') AND [c].[name] = N'PhotoUrl');
IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [StaffVacancy] DROP CONSTRAINT [' + @var12 + '];');
ALTER TABLE [StaffVacancy] DROP COLUMN [PhotoUrl];

DROP INDEX [IX_Persons_LoginId] ON [Persons];

DECLARE @var13 sysname;
SELECT @var13 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'BranchId');
IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var13 + '];');
ALTER TABLE [Persons] DROP COLUMN [BranchId];

DECLARE @var14 sysname;
SELECT @var14 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'LoginId');
IF @var14 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var14 + '];');
ALTER TABLE [Persons] DROP COLUMN [LoginId];

ALTER TABLE [Persons] ADD [PersonalEmail] nvarchar(256) NULL;

CREATE UNIQUE INDEX [IX_StaffVacancy_LoginId] ON [StaffVacancy] ([LoginId]) WHERE [LoginId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_StaffVacancy_PersonId] ON [StaffVacancy] ([PersonId]) WHERE [PersonId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_StaffVacancy_VacancyId] ON [StaffVacancy] ([VacancyId]) WHERE [VacancyId] IS NOT NULL;

ALTER TABLE [StaffVacancy] ADD CONSTRAINT [FK_StaffVacancy_Persons_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [Persons] ([PersonId]) ON DELETE SET NULL;

ALTER TABLE [StaffVacancy] ADD CONSTRAINT [FK_StaffVacancy_Vacancies_VacancyId] FOREIGN KEY ([VacancyId]) REFERENCES [Vacancies] ([VacancyId]) ON DELETE SET NULL;

ALTER TABLE [DepartmentAccessMatrix] ADD CONSTRAINT [FK_DepartmentAccessMatrix_StaffVacancy_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [StaffVacancy] ([StaffId]) ON DELETE CASCADE;

ALTER TABLE [StaffAccessGroups] ADD CONSTRAINT [FK_StaffAccessGroups_StaffVacancy_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [StaffVacancy] ([StaffId]) ON DELETE CASCADE;

ALTER TABLE [UserPermissionOverrides] ADD CONSTRAINT [FK_UserPermissionOverrides_StaffVacancy_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [StaffVacancy] ([StaffId]) ON DELETE CASCADE;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260602120317_RefactorStaffVacancyAndPersons', N'9.0.5');

COMMIT;
GO

