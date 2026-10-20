using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class MakePlatformSettingsGlobal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET XACT_ABORT ON;

                -- Do not silently resolve conflicting company definitions.
                IF EXISTS (SELECT Name FROM PlatformSettings.Actions GROUP BY Name HAVING COUNT(*) > 1)
                   OR EXISTS (SELECT Name FROM PlatformSettings.Statuses GROUP BY Name HAVING COUNT(*) > 1)
                   OR EXISTS (SELECT ColorCode FROM PlatformSettings.Colors GROUP BY ColorCode HAVING COUNT(*) > 1)
                    THROW 51000, 'Resolve duplicate platform master definitions before global conversion.', 1;

                -- Consolidate same-name tenant rows onto the oldest stable IDs.
                SELECT Id AS OldId, MIN(Id) OVER (PARTITION BY Name) AS NewId
                INTO #ActionMap
                FROM PlatformSettings.Actions;
                UPDATE mapRow SET ActionId = idMap.NewId
                FROM PlatformSettings.ActionStatuses mapRow
                JOIN #ActionMap idMap ON idMap.OldId = mapRow.ActionId
                WHERE idMap.OldId <> idMap.NewId;
                DELETE sourceRow
                FROM PlatformSettings.Actions sourceRow
                JOIN #ActionMap idMap ON idMap.OldId = sourceRow.Id
                WHERE idMap.OldId <> idMap.NewId;

                SELECT Id AS OldId, MIN(Id) OVER (PARTITION BY Name) AS NewId
                INTO #StatusMap
                FROM PlatformSettings.Statuses;
                UPDATE mapRow SET StatusId = idMap.NewId
                FROM PlatformSettings.ActionStatuses mapRow
                JOIN #StatusMap idMap ON idMap.OldId = mapRow.StatusId
                WHERE idMap.OldId <> idMap.NewId;
                UPDATE valueRow SET StatusId = idMap.NewId
                FROM PlatformSettings.StatusCrDbValues valueRow
                JOIN #StatusMap idMap ON idMap.OldId = valueRow.StatusId
                WHERE idMap.OldId <> idMap.NewId;
                DELETE sourceRow
                FROM PlatformSettings.Statuses sourceRow
                JOIN #StatusMap idMap ON idMap.OldId = sourceRow.Id
                WHERE idMap.OldId <> idMap.NewId;

                SELECT Id AS OldId, MIN(Id) OVER (PARTITION BY ColorCode) AS NewId
                INTO #ColorMap
                FROM PlatformSettings.Colors;
                UPDATE mapRow SET ColorId = idMap.NewId
                FROM PlatformSettings.ActionStatuses mapRow
                JOIN #ColorMap idMap ON idMap.OldId = mapRow.ColorId
                WHERE idMap.OldId <> idMap.NewId;
                DELETE sourceRow
                FROM PlatformSettings.Colors sourceRow
                JOIN #ColorMap idMap ON idMap.OldId = sourceRow.Id
                WHERE idMap.OldId <> idMap.NewId;

                -- One CR/DB definition per global status.
                ;WITH Ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY StatusId ORDER BY Id) AS RowNumber
                    FROM PlatformSettings.StatusCrDbValues
                )
                DELETE valueRow
                FROM PlatformSettings.StatusCrDbValues valueRow
                JOIN Ranked rankedRow ON rankedRow.Id = valueRow.Id
                WHERE rankedRow.RowNumber > 1;

                -- One compatibility ActionStatus per global action/status pair. Remap every FK first.
                SELECT Id AS OldId, MIN(Id) OVER (PARTITION BY ActionId, StatusId) AS NewId
                INTO #ActionStatusMap
                FROM PlatformSettings.ActionStatuses;

                DECLARE @UpdateReferences nvarchar(max) = N'';
                SELECT @UpdateReferences +=
                    N'UPDATE targetRow SET ' + QUOTENAME(parentColumn.name) + N' = idMap.NewId ' +
                    N'FROM ' + QUOTENAME(OBJECT_SCHEMA_NAME(foreignKey.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(foreignKey.parent_object_id)) + N' targetRow ' +
                    N'JOIN #ActionStatusMap idMap ON idMap.OldId = targetRow.' + QUOTENAME(parentColumn.name) + N' ' +
                    N'WHERE idMap.OldId <> idMap.NewId;'
                FROM sys.foreign_keys foreignKey
                JOIN sys.foreign_key_columns foreignKeyColumn ON foreignKeyColumn.constraint_object_id = foreignKey.object_id
                JOIN sys.columns parentColumn ON parentColumn.object_id = foreignKeyColumn.parent_object_id AND parentColumn.column_id = foreignKeyColumn.parent_column_id
                WHERE foreignKey.referenced_object_id = OBJECT_ID(N'PlatformSettings.ActionStatuses');
                EXEC sys.sp_executesql @UpdateReferences;

                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraPlatformActionStatusId') IS NOT NULL
                    UPDATE targetRow SET CameraPlatformActionStatusId = idMap.NewId
                    FROM dbo.AttendanceRecords targetRow
                    JOIN #ActionStatusMap idMap ON idMap.OldId = targetRow.CameraPlatformActionStatusId
                    WHERE idMap.OldId <> idMap.NewId;

                DELETE sourceRow
                FROM PlatformSettings.ActionStatuses sourceRow
                JOIN #ActionStatusMap idMap ON idMap.OldId = sourceRow.Id
                WHERE idMap.OldId <> idMap.NewId;

                -- Remove every tenant FK/index regardless of legacy/generated naming.
                DECLARE @DropSql nvarchar(max) = N'';
                SELECT @DropSql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' +
                    QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';'
                FROM sys.foreign_keys
                WHERE parent_object_id IN (
                    OBJECT_ID(N'PlatformSettings.Actions'), OBJECT_ID(N'PlatformSettings.Statuses'),
                    OBJECT_ID(N'PlatformSettings.Colors'), OBJECT_ID(N'PlatformSettings.ActionStatuses'),
                    OBJECT_ID(N'PlatformSettings.StatusCrDbValues'))
                  AND referenced_object_id = OBJECT_ID(N'dbo.Tenants');
                EXEC sys.sp_executesql @DropSql;

                SET @DropSql = N'';
                SELECT @DropSql += CASE WHEN indexes.is_unique_constraint = 1
                    THEN N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(indexes.object_id)) + N'.' + QUOTENAME(OBJECT_NAME(indexes.object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(indexes.name) + N';'
                    ELSE N'DROP INDEX ' + QUOTENAME(indexes.name) + N' ON ' + QUOTENAME(OBJECT_SCHEMA_NAME(indexes.object_id)) + N'.' + QUOTENAME(OBJECT_NAME(indexes.object_id)) + N';' END
                FROM sys.indexes indexes
                WHERE indexes.object_id IN (
                    OBJECT_ID(N'PlatformSettings.Actions'), OBJECT_ID(N'PlatformSettings.Statuses'),
                    OBJECT_ID(N'PlatformSettings.Colors'), OBJECT_ID(N'PlatformSettings.ActionStatuses'),
                    OBJECT_ID(N'PlatformSettings.StatusCrDbValues'))
                  AND EXISTS (
                    SELECT 1 FROM sys.index_columns indexColumn
                    JOIN sys.columns indexedColumn ON indexedColumn.object_id = indexColumn.object_id AND indexedColumn.column_id = indexColumn.column_id
                    WHERE indexColumn.object_id = indexes.object_id AND indexColumn.index_id = indexes.index_id AND indexedColumn.name = N'TenantId');
                EXEC sys.sp_executesql @DropSql;

                ALTER TABLE PlatformSettings.Statuses DROP COLUMN TenantId;
                ALTER TABLE PlatformSettings.StatusCrDbValues DROP COLUMN TenantId;
                ALTER TABLE PlatformSettings.Colors DROP COLUMN TenantId;
                ALTER TABLE PlatformSettings.ActionStatuses DROP COLUMN TenantId;
                ALTER TABLE PlatformSettings.Actions DROP COLUMN TenantId;
                """);

            migrationBuilder.CreateTable(
                name: "Processes",
                schema: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Processes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessStatuses",
                schema: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessId = table.Column<int>(type: "int", nullable: false),
                    StatusId = table.Column<int>(type: "int", nullable: false),
                    ColorId = table.Column<int>(type: "int", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessStatuses_Colors_ColorId",
                        column: x => x.ColorId,
                        principalSchema: "PlatformSettings",
                        principalTable: "Colors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessStatuses_Processes_ProcessId",
                        column: x => x.ProcessId,
                        principalSchema: "PlatformSettings",
                        principalTable: "Processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessStatuses_Statuses_StatusId",
                        column: x => x.StatusId,
                        principalSchema: "PlatformSettings",
                        principalTable: "Statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'PlatformSettings.StatusCrDbValues') AND name=N'IX_StatusCrDbValues_StatusId')
                    DROP INDEX IX_StatusCrDbValues_StatusId ON PlatformSettings.StatusCrDbValues;

                -- Preserve the operational tenant predicates; only shared lookup predicates change.
                DECLARE @ProcedureName sysname, @Definition nvarchar(max);
                DECLARE SettingsConsumers CURSOR LOCAL FAST_FORWARD FOR
                    SELECT name FROM sys.procedures WHERE schema_id=SCHEMA_ID(N'dbo')
                    AND name IN (N'usp_Attendance_DailyReport', N'usp_Attendance_EvaluateStatuses');
                OPEN SettingsConsumers;
                FETCH NEXT FROM SettingsConsumers INTO @ProcedureName;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @Definition=OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @ProcedureName));
                    SET @Definition=REPLACE(@Definition, N'CREATE OR ALTER', N'ALTER');
                    SET @Definition=REPLACE(@Definition, N'CREATE   PROCEDURE', N'ALTER PROCEDURE');
                    SET @Definition=REPLACE(@Definition, N'CREATE PROCEDURE', N'ALTER PROCEDURE');
                    SET @Definition=REPLACE(@Definition, N'CREATE  PROCEDURE', N'ALTER PROCEDURE');
                    SET @Definition=REPLACE(@Definition, N'ORDER BY CASE WHEN pas.TenantId=@TenantId THEN 0 ELSE 1 END', N'ORDER BY pas.Id');
                    SET @Definition=REPLACE(@Definition, N'AND (pas.TenantId = @TenantId OR pas.TenantId IS NULL)', N'');
                    SET @Definition=REPLACE(@Definition, N'AND (platformCrDb.TenantId=@TenantId OR platformCrDb.TenantId IS NULL)', N'');
                    SET @Definition=REPLACE(@Definition, N'AND (cameraCrDb.TenantId=@TenantId OR cameraCrDb.TenantId IS NULL)', N'');
                    SET @Definition=REPLACE(@Definition, N'AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)', N'');
                    SET @Definition=REPLACE(@Definition, N'AND actionStatus.TenantId = @TenantId', N'');
                    SET @Definition=REPLACE(@Definition, N'WHERE Name=N''Attendance'' AND TenantId=@TenantId', N'WHERE Name=N''Attendance''');
                    SET @Definition=REPLACE(@Definition, N'WHERE Name = N''Attendance''' + CHAR(13) + CHAR(10) + N'      AND TenantId = @TenantId', N'WHERE Name = N''Attendance''');
                    SET @Definition=REPLACE(@Definition, N'WHERE Name = N''Attendance''' + CHAR(10) + N'      AND TenantId = @TenantId', N'WHERE Name = N''Attendance''');
                    EXEC sys.sp_executesql @Definition;
                    FETCH NEXT FROM SettingsConsumers INTO @ProcedureName;
                END;
                CLOSE SettingsConsumers;
                DEALLOCATE SettingsConsumers;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Statuses_Name",
                schema: "PlatformSettings",
                table: "Statuses",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatusCrDbValues_StatusId",
                schema: "PlatformSettings",
                table: "StatusCrDbValues",
                column: "StatusId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Colors_ColorCode",
                schema: "PlatformSettings",
                table: "Colors",
                column: "ColorCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionStatuses_ActionId_StatusId",
                schema: "PlatformSettings",
                table: "ActionStatuses",
                columns: new[] { "ActionId", "StatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Actions_Name",
                schema: "PlatformSettings",
                table: "Actions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Processes_Name",
                schema: "PlatformSettings",
                table: "Processes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStatuses_ColorId",
                schema: "PlatformSettings",
                table: "ProcessStatuses",
                column: "ColorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStatuses_ProcessId_StatusId",
                schema: "PlatformSettings",
                table: "ProcessStatuses",
                columns: new[] { "ProcessId", "StatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStatuses_StatusId",
                schema: "PlatformSettings",
                table: "ProcessStatuses",
                column: "StatusId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Global settings cannot be assigned back to companies automatically. Restore the pre-migration database backup to roll back.");
#pragma warning disable CS0162
            migrationBuilder.DropTable(
                name: "ProcessStatuses",
                schema: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "Processes",
                schema: "PlatformSettings");

            migrationBuilder.DropIndex(
                name: "IX_Statuses_Name",
                schema: "PlatformSettings",
                table: "Statuses");

            migrationBuilder.DropIndex(
                name: "IX_StatusCrDbValues_StatusId",
                schema: "PlatformSettings",
                table: "StatusCrDbValues");

            migrationBuilder.DropIndex(
                name: "IX_Colors_ColorCode",
                schema: "PlatformSettings",
                table: "Colors");

            migrationBuilder.DropIndex(
                name: "IX_ActionStatuses_ActionId_StatusId",
                schema: "PlatformSettings",
                table: "ActionStatuses");

            migrationBuilder.DropIndex(
                name: "IX_Actions_Name",
                schema: "PlatformSettings",
                table: "Actions");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                schema: "PlatformSettings",
                table: "Statuses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                schema: "PlatformSettings",
                table: "StatusCrDbValues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                schema: "PlatformSettings",
                table: "Colors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                schema: "PlatformSettings",
                table: "ActionStatuses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                schema: "PlatformSettings",
                table: "Actions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Statuses_TenantId",
                schema: "PlatformSettings",
                table: "Statuses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Statuses_TenantId_Name",
                schema: "PlatformSettings",
                table: "Statuses",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatusCrDbValues_StatusId",
                schema: "PlatformSettings",
                table: "StatusCrDbValues",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_StatusCrDbValues_TenantId_StatusId",
                schema: "PlatformSettings",
                table: "StatusCrDbValues",
                columns: new[] { "TenantId", "StatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Colors_TenantId_ColorCode",
                schema: "PlatformSettings",
                table: "Colors",
                columns: new[] { "TenantId", "ColorCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionStatuses_ActionId",
                schema: "PlatformSettings",
                table: "ActionStatuses",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionStatuses_TenantId_ActionId_StatusId",
                schema: "PlatformSettings",
                table: "ActionStatuses",
                columns: new[] { "TenantId", "ActionId", "StatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Actions_TenantId",
                schema: "PlatformSettings",
                table: "Actions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Actions_TenantId_Name",
                schema: "PlatformSettings",
                table: "Actions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Actions_Tenants_TenantId",
                schema: "PlatformSettings",
                table: "Actions",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ActionStatuses_Tenants_TenantId",
                schema: "PlatformSettings",
                table: "ActionStatuses",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Colors_Tenants_TenantId",
                schema: "PlatformSettings",
                table: "Colors",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatusCrDbValues_Tenants_TenantId",
                schema: "PlatformSettings",
                table: "StatusCrDbValues",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Statuses_Tenants_TenantId",
                schema: "PlatformSettings",
                table: "Statuses",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
#pragma warning restore CS0162
        }
    }
}
