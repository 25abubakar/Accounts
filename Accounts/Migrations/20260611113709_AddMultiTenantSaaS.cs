using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenantSaaS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ══════════════════════════════════════════════════════════════════
            // ALL DDL IS FULLY IDEMPOTENT — every statement is wrapped in
            // IF NOT EXISTS / COL_LENGTH guards.
            //
            // The database already has all these objects from a partial previous
            // run.  This migration re-applies safely without errors.
            // ══════════════════════════════════════════════════════════════════

            // ── 1. ApplicationUser tenant columns ─────────────────────────────
            migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.AspNetUsers', 'IsSuperAdmin') IS NULL
                    ALTER TABLE dbo.AspNetUsers ADD IsSuperAdmin bit NOT NULL DEFAULT 0;

                IF COL_LENGTH('dbo.AspNetUsers', 'IsTenantAdmin') IS NULL
                    ALTER TABLE dbo.AspNetUsers ADD IsTenantAdmin bit NOT NULL DEFAULT 0;

                IF COL_LENGTH('dbo.AspNetUsers', 'TenantId') IS NULL
                    ALTER TABLE dbo.AspNetUsers ADD TenantId int NULL;
            ");

            // ── 2. Alter Identity key column lengths (only if still 128) ──────
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='AspNetUserTokens' AND COLUMN_NAME='Name'
                      AND CHARACTER_MAXIMUM_LENGTH=128
                )
                    ALTER TABLE dbo.AspNetUserTokens ALTER COLUMN [Name] nvarchar(450) NOT NULL;

                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='AspNetUserTokens' AND COLUMN_NAME='LoginProvider'
                      AND CHARACTER_MAXIMUM_LENGTH=128
                )
                    ALTER TABLE dbo.AspNetUserTokens ALTER COLUMN LoginProvider nvarchar(450) NOT NULL;

                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='AspNetUserLogins' AND COLUMN_NAME='ProviderKey'
                      AND CHARACTER_MAXIMUM_LENGTH=128
                )
                    ALTER TABLE dbo.AspNetUserLogins ALTER COLUMN ProviderKey nvarchar(450) NOT NULL;

                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='AspNetUserLogins' AND COLUMN_NAME='LoginProvider'
                      AND CHARACTER_MAXIMUM_LENGTH=128
                )
                    ALTER TABLE dbo.AspNetUserLogins ALTER COLUMN LoginProvider nvarchar(450) NOT NULL;
            ");

            // ── 3. TenantId columns on operational tables ─────────────────────
            migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.Vacancies',    'TenantId') IS NULL
                    ALTER TABLE dbo.Vacancies    ADD TenantId int NOT NULL DEFAULT 0;

                IF COL_LENGTH('dbo.StaffVacancy', 'TenantId') IS NULL
                    ALTER TABLE dbo.StaffVacancy ADD TenantId int NOT NULL DEFAULT 0;

                IF COL_LENGTH('dbo.Persons',      'TenantId') IS NULL
                    ALTER TABLE dbo.Persons      ADD TenantId int NOT NULL DEFAULT 0;

                IF COL_LENGTH('dbo.JobTitles',    'TenantId') IS NULL
                    ALTER TABLE dbo.JobTitles    ADD TenantId int NOT NULL DEFAULT 0;
            ");

            // ── 4. Tenants table ──────────────────────────────────────────────
            migrationBuilder.Sql(@"
                IF OBJECT_ID('dbo.Tenants','U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Tenants (
                        Id                 int IDENTITY(1,1) NOT NULL,
                        OrganizationTreeId int           NOT NULL,
                        TenantName         nvarchar(150) NOT NULL,
                        TenantCode         nvarchar(20)  NOT NULL,
                        IsActive           bit           NOT NULL DEFAULT 1,
                        CreatedOnUtc       datetime2     NOT NULL DEFAULT SYSUTCDATETIME(),
                        CreatedByUserId    nvarchar(450) NULL,
                        CONSTRAINT PK_Tenants PRIMARY KEY (Id),
                        CONSTRAINT FK_Tenants_OrganizationTree_OrganizationTreeId
                            FOREIGN KEY (OrganizationTreeId)
                            REFERENCES dbo.OrganizationTree(Id) ON DELETE NO ACTION
                    );
                    CREATE UNIQUE INDEX IX_Tenants_OrganizationTreeId ON dbo.Tenants(OrganizationTreeId);
                    CREATE UNIQUE INDEX IX_Tenants_TenantCode          ON dbo.Tenants(TenantCode);
                END;
            ");

            // ── 5. TenantMenuPermissions table ────────────────────────────────
            migrationBuilder.Sql(@"
                IF OBJECT_ID('dbo.TenantMenuPermissions','U') IS NULL
                BEGIN
                    CREATE TABLE dbo.TenantMenuPermissions (
                        TenantId        int           NOT NULL,
                        MenuId          int           NOT NULL,
                        IsAllow         bit           NOT NULL DEFAULT 1,
                        GrantedOnUtc    datetime2     NOT NULL DEFAULT SYSUTCDATETIME(),
                        GrantedByUserId nvarchar(450) NULL,
                        CONSTRAINT PK_TenantMenuPermissions PRIMARY KEY (TenantId, MenuId),
                        CONSTRAINT FK_TMP_Tenants  FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
                        CONSTRAINT FK_TMP_Menus    FOREIGN KEY (MenuId)   REFERENCES dbo.Menus(Id)   ON DELETE CASCADE
                    );
                    CREATE INDEX IX_TenantMenuPermissions_MenuId   ON dbo.TenantMenuPermissions(MenuId);
                    CREATE INDEX IX_TenantMenuPermissions_TenantId ON dbo.TenantMenuPermissions(TenantId);
                END;
            ");

            // ── 6. TenantRolePermissions table ────────────────────────────────
            migrationBuilder.Sql(@"
                IF OBJECT_ID('dbo.TenantRolePermissions','U') IS NULL
                BEGIN
                    CREATE TABLE dbo.TenantRolePermissions (
                        Id           int IDENTITY(1,1) NOT NULL,
                        TenantId     int           NOT NULL,
                        JobTitle     nvarchar(100) NOT NULL,
                        DeptId       int           NULL,
                        PermissionId int           NOT NULL,
                        IsAllowed    bit           NOT NULL DEFAULT 0,
                        CreatedOnUtc datetime2     NOT NULL DEFAULT SYSUTCDATETIME(),
                        SetByUserId  nvarchar(450) NULL,
                        CONSTRAINT PK_TenantRolePermissions PRIMARY KEY (Id),
                        CONSTRAINT FK_TRP_Tenants  FOREIGN KEY (TenantId)     REFERENCES dbo.Tenants(Id)             ON DELETE CASCADE,
                        CONSTRAINT FK_TRP_Features FOREIGN KEY (PermissionId) REFERENCES dbo.Features(PermissionId)  ON DELETE CASCADE,
                        CONSTRAINT FK_TRP_OrgTree  FOREIGN KEY (DeptId)       REFERENCES dbo.OrganizationTree(Id)    ON DELETE NO ACTION
                    );
                    CREATE INDEX IX_TenantRolePermissions_TenantId     ON dbo.TenantRolePermissions(TenantId);
                    CREATE INDEX IX_TenantRolePermissions_PermissionId ON dbo.TenantRolePermissions(PermissionId);
                    CREATE INDEX IX_TenantRolePermissions_DeptId       ON dbo.TenantRolePermissions(DeptId);
                    CREATE INDEX IX_TenantRolePermissions_TenantId_JobTitle
                        ON dbo.TenantRolePermissions(TenantId, JobTitle);
                    CREATE UNIQUE INDEX IX_TenantRolePermissions_Unique
                        ON dbo.TenantRolePermissions(TenantId, JobTitle, DeptId, PermissionId)
                        WHERE DeptId IS NOT NULL;
                END;
            ");

            // ── 7. Indexes on operational tables (idempotent) ─────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Vacancies_TenantId'   AND object_id=OBJECT_ID('dbo.Vacancies'))
                    CREATE INDEX IX_Vacancies_TenantId   ON dbo.Vacancies(TenantId);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_StaffVacancy_TenantId' AND object_id=OBJECT_ID('dbo.StaffVacancy'))
                    CREATE INDEX IX_StaffVacancy_TenantId ON dbo.StaffVacancy(TenantId);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Persons_TenantId'     AND object_id=OBJECT_ID('dbo.Persons'))
                    CREATE INDEX IX_Persons_TenantId     ON dbo.Persons(TenantId);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_JobTitles_TenantId'   AND object_id=OBJECT_ID('dbo.JobTitles'))
                    CREATE INDEX IX_JobTitles_TenantId   ON dbo.JobTitles(TenantId);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_JobTitles_TenantId_TitleName' AND object_id=OBJECT_ID('dbo.JobTitles'))
                    CREATE UNIQUE INDEX IX_JobTitles_TenantId_TitleName ON dbo.JobTitles(TenantId, TitleName);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AspNetUsers_TenantId' AND object_id=OBJECT_ID('dbo.AspNetUsers'))
                    CREATE INDEX IX_AspNetUsers_TenantId ON dbo.AspNetUsers(TenantId);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AppNotes_TenantId'    AND object_id=OBJECT_ID('dbo.AppNotes'))
                    CREATE INDEX IX_AppNotes_TenantId    ON dbo.AppNotes(TenantId);
            ");

            // ── 8. Data backfill ──────────────────────────────────────────────
            migrationBuilder.Sql(@"
                -- Seed Tenants for each Company/Group node not yet registered
                INSERT INTO dbo.Tenants (OrganizationTreeId, TenantName, TenantCode, IsActive, CreatedOnUtc)
                SELECT
                    ot.Id, ot.Name,
                    UPPER(LEFT(REPLACE(ot.Name,' ',''), 3))
                        + CAST((ABS(CHECKSUM(NEWID())) % 9000 + 1000) AS VARCHAR(4)),
                    1, SYSUTCDATETIME()
                FROM dbo.OrganizationTree ot
                WHERE ot.Label IN ('Company','Group')
                  AND ot.Id NOT IN (SELECT OrganizationTreeId FROM dbo.Tenants);

                -- Ensure at least one fallback tenant
                IF NOT EXISTS (SELECT 1 FROM dbo.Tenants)
                BEGIN
                    DECLARE @rootId INT = (SELECT TOP 1 Id FROM dbo.OrganizationTree ORDER BY Id);
                    IF @rootId IS NULL SET @rootId = 1;
                    INSERT INTO dbo.Tenants (OrganizationTreeId, TenantName, TenantCode, IsActive, CreatedOnUtc)
                    VALUES (@rootId, 'Default Tenant', 'DEF0', 1, SYSUTCDATETIME());
                END;

                -- Backfill Vacancies via recursive CTE
                ;WITH OrgHierarchy AS (
                    SELECT Id AS StartOrgId, Id AS CurrentOrgId, ParentId, Label
                    FROM dbo.OrganizationTree
                    UNION ALL
                    SELECT h.StartOrgId, t.Id, t.ParentId, t.Label
                    FROM OrgHierarchy h
                    INNER JOIN dbo.OrganizationTree t ON h.ParentId = t.Id
                    WHERE h.Label NOT IN ('Company','Group')
                )
                UPDATE v SET v.TenantId = ten.Id
                FROM dbo.Vacancies v
                INNER JOIN OrgHierarchy h ON v.OrganizationId = h.StartOrgId AND h.Label IN ('Company','Group')
                INNER JOIN dbo.Tenants ten ON ten.OrganizationTreeId = h.CurrentOrgId
                WHERE v.TenantId = 0;

                -- Backfill StaffVacancy from Vacancies
                UPDATE sv SET sv.TenantId = v.TenantId
                FROM dbo.StaffVacancy sv
                INNER JOIN dbo.Vacancies v ON sv.VacancyId = v.VacancyId
                WHERE sv.TenantId = 0;

                -- Backfill Persons from StaffVacancy
                UPDATE p SET p.TenantId = sv.TenantId
                FROM dbo.Persons p
                INNER JOIN dbo.StaffVacancy sv ON p.PersonId = sv.PersonId
                WHERE p.TenantId = 0;

                -- Fallback zeros to first available tenant
                DECLARE @FallbackId INT = (SELECT TOP 1 Id FROM dbo.Tenants ORDER BY Id);
                UPDATE dbo.JobTitles    SET TenantId = @FallbackId WHERE TenantId = 0;
                UPDATE dbo.Vacancies    SET TenantId = @FallbackId WHERE TenantId = 0;
                UPDATE dbo.StaffVacancy SET TenantId = @FallbackId WHERE TenantId = 0;
                UPDATE dbo.Persons      SET TenantId = @FallbackId WHERE TenantId = 0;
                UPDATE dbo.AppNotes     SET TenantId = @FallbackId WHERE TenantId IS NULL OR TenantId = 0;
            ");

            // ── 9. Foreign key constraints on operational tables (idempotent) ─
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_AppNotes_Tenants_TenantId')
                    ALTER TABLE dbo.AppNotes    ADD CONSTRAINT FK_AppNotes_Tenants_TenantId
                        FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION;

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_JobTitles_Tenants_TenantId')
                    ALTER TABLE dbo.JobTitles   ADD CONSTRAINT FK_JobTitles_Tenants_TenantId
                        FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION;

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Persons_Tenants_TenantId')
                    ALTER TABLE dbo.Persons     ADD CONSTRAINT FK_Persons_Tenants_TenantId
                        FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION;

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_StaffVacancy_Tenants_TenantId')
                    ALTER TABLE dbo.StaffVacancy ADD CONSTRAINT FK_StaffVacancy_Tenants_TenantId
                        FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION;

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Vacancies_Tenants_TenantId')
                    ALTER TABLE dbo.Vacancies   ADD CONSTRAINT FK_Vacancies_Tenants_TenantId
                        FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- Drop FKs on operational tables
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_AppNotes_Tenants_TenantId')
                    ALTER TABLE dbo.AppNotes    DROP CONSTRAINT FK_AppNotes_Tenants_TenantId;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_JobTitles_Tenants_TenantId')
                    ALTER TABLE dbo.JobTitles   DROP CONSTRAINT FK_JobTitles_Tenants_TenantId;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Persons_Tenants_TenantId')
                    ALTER TABLE dbo.Persons     DROP CONSTRAINT FK_Persons_Tenants_TenantId;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_StaffVacancy_Tenants_TenantId')
                    ALTER TABLE dbo.StaffVacancy DROP CONSTRAINT FK_StaffVacancy_Tenants_TenantId;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Vacancies_Tenants_TenantId')
                    ALTER TABLE dbo.Vacancies   DROP CONSTRAINT FK_Vacancies_Tenants_TenantId;

                -- Drop tenant tables
                IF OBJECT_ID('dbo.TenantMenuPermissions','U') IS NOT NULL DROP TABLE dbo.TenantMenuPermissions;
                IF OBJECT_ID('dbo.TenantRolePermissions','U') IS NOT NULL DROP TABLE dbo.TenantRolePermissions;
                IF OBJECT_ID('dbo.Tenants',              'U') IS NOT NULL DROP TABLE dbo.Tenants;

                -- Drop indexes
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Vacancies_TenantId'            AND object_id=OBJECT_ID('dbo.Vacancies'))    DROP INDEX IX_Vacancies_TenantId    ON dbo.Vacancies;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_StaffVacancy_TenantId'          AND object_id=OBJECT_ID('dbo.StaffVacancy')) DROP INDEX IX_StaffVacancy_TenantId ON dbo.StaffVacancy;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Persons_TenantId'               AND object_id=OBJECT_ID('dbo.Persons'))      DROP INDEX IX_Persons_TenantId     ON dbo.Persons;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_JobTitles_TenantId'             AND object_id=OBJECT_ID('dbo.JobTitles'))     DROP INDEX IX_JobTitles_TenantId   ON dbo.JobTitles;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_JobTitles_TenantId_TitleName'   AND object_id=OBJECT_ID('dbo.JobTitles'))     DROP INDEX IX_JobTitles_TenantId_TitleName ON dbo.JobTitles;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AspNetUsers_TenantId'           AND object_id=OBJECT_ID('dbo.AspNetUsers'))   DROP INDEX IX_AspNetUsers_TenantId ON dbo.AspNetUsers;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AppNotes_TenantId'              AND object_id=OBJECT_ID('dbo.AppNotes'))      DROP INDEX IX_AppNotes_TenantId    ON dbo.AppNotes;

                -- Drop TenantId columns from operational tables
                IF COL_LENGTH('dbo.Vacancies',   'TenantId') IS NOT NULL ALTER TABLE dbo.Vacancies    DROP COLUMN TenantId;
                IF COL_LENGTH('dbo.StaffVacancy','TenantId') IS NOT NULL ALTER TABLE dbo.StaffVacancy DROP COLUMN TenantId;
                IF COL_LENGTH('dbo.Persons',     'TenantId') IS NOT NULL ALTER TABLE dbo.Persons      DROP COLUMN TenantId;
                IF COL_LENGTH('dbo.JobTitles',   'TenantId') IS NOT NULL ALTER TABLE dbo.JobTitles    DROP COLUMN TenantId;

                -- Drop ApplicationUser tenant columns
                IF COL_LENGTH('dbo.AspNetUsers','IsSuperAdmin')  IS NOT NULL ALTER TABLE dbo.AspNetUsers DROP COLUMN IsSuperAdmin;
                IF COL_LENGTH('dbo.AspNetUsers','IsTenantAdmin') IS NOT NULL ALTER TABLE dbo.AspNetUsers DROP COLUMN IsTenantAdmin;
                IF COL_LENGTH('dbo.AspNetUsers','TenantId')      IS NOT NULL ALTER TABLE dbo.AspNetUsers DROP COLUMN TenantId;
            ");
        }
    }
}
