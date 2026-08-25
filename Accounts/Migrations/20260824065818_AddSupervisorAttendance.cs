using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSupervisorAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupervisorRecordedByUserId",
                table: "AttendanceRecords",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupervisorRecordedDate",
                table: "AttendanceRecords",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupervisorRemarks",
                table: "AttendanceRecords",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql(
                """
                DECLARE @PermissionId int = (
                    SELECT TOP (1) [PermissionId]
                    FROM [Features]
                    WHERE [FeatureKey] = N'ATTENDANCE_BY_SUPERVISOR'
                );

                IF @PermissionId IS NULL
                BEGIN
                    INSERT INTO [Features]
                        ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES
                        (N'ATTENDANCE_BY_SUPERVISOR', N'Attendance by Supervisor', N'Attendance',
                         N'Manually record attendance for direct reports mapped to the By Supervisor attendance type.',
                         SYSUTCDATETIME());
                    SET @PermissionId = SCOPE_IDENTITY();
                END;

                DECLARE @AttendancePortalId int = (
                    SELECT TOP (1) [Id]
                    FROM [Menus]
                    WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                    ORDER BY [Id]
                );
                DECLARE @MenuId int = (
                    SELECT TOP (1) [Id]
                    FROM [Menus]
                    WHERE [Route] = N'/attendance/by-supervisor'
                       OR ([ParentId] = @AttendancePortalId AND [Title] = N'By Supervisor')
                    ORDER BY CASE WHEN [Route] = N'/attendance/by-supervisor' THEN 0 ELSE 1 END, [Id]
                );

                IF @MenuId IS NULL AND @AttendancePortalId IS NOT NULL
                BEGIN
                    DECLARE @NextSortOrder int = ISNULL((
                        SELECT MAX([SortOrder]) + 1
                        FROM [Menus]
                        WHERE [ParentId] = @AttendancePortalId
                    ), 1);

                    INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                    VALUES (N'By Supervisor', N'UserCheck', N'/attendance/by-supervisor',
                            @AttendancePortalId, @NextSortOrder, 1);
                    SET @MenuId = SCOPE_IDENTITY();
                END;

                IF @MenuId IS NOT NULL
                BEGIN
                    UPDATE [Menus]
                    SET [Title] = N'By Supervisor', [Icon] = N'UserCheck',
                        [Route] = N'/attendance/by-supervisor', [ParentId] = @AttendancePortalId,
                        [IsActive] = 1
                    WHERE [Id] = @MenuId;

                    IF NOT EXISTS (
                        SELECT 1 FROM [MenuPermissions]
                        WHERE [MenuId] = @MenuId AND [PermissionId] = @PermissionId)
                    BEGIN
                        INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                        VALUES (@MenuId, @PermissionId);
                    END;

                    INSERT INTO [TenantMenuPermissions]
                        ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete],
                         [GrantedOnUtc], [GrantedByUserId])
                    SELECT tenant.[Id], @MenuId, 1, 1, 1, 1, 0,
                           SYSUTCDATETIME(), N'System: Attendance by Supervisor'
                    FROM [Tenants] tenant
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [TenantMenuPermissions] existingPermission
                        WHERE existingPermission.[TenantId] = tenant.[Id]
                          AND existingPermission.[MenuId] = @MenuId);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @MenuId int = (
                    SELECT TOP (1) [Id]
                    FROM [Menus]
                    WHERE [Route] = N'/attendance/by-supervisor'
                    ORDER BY [Id]
                );
                DECLARE @PermissionId int = (
                    SELECT TOP (1) [PermissionId]
                    FROM [Features]
                    WHERE [FeatureKey] = N'ATTENDANCE_BY_SUPERVISOR'
                );

                DELETE feature
                FROM [AccessFeatures] feature
                JOIN [StaffMenuAccess] accessRow ON accessRow.[Id] = feature.[StaffMenuAccessId]
                WHERE accessRow.[MenuId] = @MenuId;

                DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @MenuId;
                DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @MenuId;
                DELETE FROM [MenuPermissions] WHERE [MenuId] = @MenuId;
                DELETE FROM [Menus] WHERE [Id] = @MenuId;

                IF @PermissionId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM [RolePermissions] WHERE [PermissionId] = @PermissionId)
                   AND NOT EXISTS (SELECT 1 FROM [UserPermissionOverrides] WHERE [PermissionId] = @PermissionId)
                   AND NOT EXISTS (SELECT 1 FROM [DepartmentAccessMatrix] WHERE [PermissionId] = @PermissionId)
                   AND NOT EXISTS (SELECT 1 FROM [AccessGroupFeatures] WHERE [PermissionId] = @PermissionId)
                BEGIN
                    DELETE FROM [Features] WHERE [PermissionId] = @PermissionId;
                END;
                """);

            migrationBuilder.DropColumn(
                name: "SupervisorRecordedByUserId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "SupervisorRecordedDate",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "SupervisorRemarks",
                table: "AttendanceRecords");
        }
    }
}
