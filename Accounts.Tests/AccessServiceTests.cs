using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;
using Xunit;

namespace Accounts.Tests
{
    /// <summary>
    /// Unit tests for AccessService.GetEffectiveAccessAsync and SyncGroupToDeptMatrixAsync.
    ///
    /// Each test uses a fresh in-memory database — fully isolated, no shared state.
    ///
    /// Test naming convention:
    ///   MethodName_Scenario_ExpectedResult
    /// </summary>
    public class AccessServiceTests
    {
        // ═══════════════════════════════════════════════════════════════════════
        // GetEffectiveAccessAsync — Merge Logic Tests
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetEffectiveAccess_WhenOnlyGroupGrantsFeature_HasAccessIsTrue()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW", "EMPLOYEE_VIEW");

            var (staff, _, _) = TestDbFactory.SeedStaff(db);
            var group         = TestDbFactory.SeedGroup(db, "HR Group");

            // Group has ORG_VIEW — individual matrix has nothing
            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW");

            var service = new AccessService(db);

            // Act
            var result = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            // Assert
            var orgView      = result.Features.Single(f => f.FeatureKey == "ORG_VIEW");
            var employeeView = result.Features.Single(f => f.FeatureKey == "EMPLOYEE_VIEW");

            Assert.True(orgView.HasAccess,        "ORG_VIEW should be accessible via group.");
            Assert.True(orgView.GroupAccess,      "ORG_VIEW GroupAccess should be true.");
            Assert.False(orgView.IndividualAccess,"ORG_VIEW IndividualAccess should be false.");
            Assert.Equal("Group", orgView.Source);

            Assert.False(employeeView.HasAccess,  "EMPLOYEE_VIEW should not be accessible.");
            Assert.Equal("None", employeeView.Source);
        }

        [Fact]
        public async Task GetEffectiveAccess_WhenOnlyIndividualGrantsFeature_HasAccessIsTrue()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "PERSON_VIEW", "VACANCY_VIEW");

            var (staff, vacancy, orgNode) = TestDbFactory.SeedStaff(db);
            var group                     = TestDbFactory.SeedGroup(db, "Empty Group");

            // Individual matrix has PERSON_VIEW — group has nothing
            TestDbFactory.SeedMatrixRow(db, staff.StaffId, orgNode.Id, "PERSON_VIEW", hasAccess: true);

            var service = new AccessService(db);

            // Act
            var result = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            // Assert
            var personView  = result.Features.Single(f => f.FeatureKey == "PERSON_VIEW");
            var vacancyView = result.Features.Single(f => f.FeatureKey == "VACANCY_VIEW");

            Assert.True(personView.HasAccess,         "PERSON_VIEW should be accessible via individual.");
            Assert.True(personView.IndividualAccess,  "PERSON_VIEW IndividualAccess should be true.");
            Assert.False(personView.GroupAccess,      "PERSON_VIEW GroupAccess should be false.");
            Assert.Equal("Individual", personView.Source);

            Assert.False(vacancyView.HasAccess, "VACANCY_VIEW should not be accessible.");
            Assert.Equal("None", vacancyView.Source);
        }

        [Fact]
        public async Task GetEffectiveAccess_WhenBothGrantFeature_SourceIsBoth()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "EMPLOYEE_EDIT");

            var (staff, vacancy, orgNode) = TestDbFactory.SeedStaff(db);
            var group                     = TestDbFactory.SeedGroup(db, "Manager Group");

            // Both individual AND group grant EMPLOYEE_EDIT
            TestDbFactory.SeedMatrixRow(db, staff.StaffId, orgNode.Id, "EMPLOYEE_EDIT", hasAccess: true);
            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "EMPLOYEE_EDIT");

            var service = new AccessService(db);

            // Act
            var result = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            // Assert
            var feature = result.Features.Single(f => f.FeatureKey == "EMPLOYEE_EDIT");

            Assert.True(feature.HasAccess,        "EMPLOYEE_EDIT should be accessible.");
            Assert.True(feature.IndividualAccess, "IndividualAccess should be true.");
            Assert.True(feature.GroupAccess,      "GroupAccess should be true.");
            Assert.Equal("Both", feature.Source);
        }

        [Fact]
        public async Task GetEffectiveAccess_WhenNeitherGrantsFeature_HasAccessIsFalse()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "REPORT_EXPORT");

            var (staff, _, _) = TestDbFactory.SeedStaff(db);
            var group         = TestDbFactory.SeedGroup(db, "Basic Group");
            // No matrix rows, no group features

            var service = new AccessService(db);

            // Act
            var result = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            // Assert
            var feature = result.Features.Single(f => f.FeatureKey == "REPORT_EXPORT");

            Assert.False(feature.HasAccess,        "REPORT_EXPORT should not be accessible.");
            Assert.False(feature.IndividualAccess, "IndividualAccess should be false.");
            Assert.False(feature.GroupAccess,      "GroupAccess should be false.");
            Assert.Equal("None", feature.Source);
        }

        [Fact]
        public async Task GetEffectiveAccess_WhenIndividualDeniedButGroupGrants_HasAccessIsTrue()
        {
            // Arrange — this tests the OR logic:
            // Individual matrix row exists but HasAccess = false
            // Group still grants it → final result should be TRUE
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "MENU_VIEW");

            var (staff, vacancy, orgNode) = TestDbFactory.SeedStaff(db);
            var group                     = TestDbFactory.SeedGroup(db, "Viewer Group");

            // Individual row explicitly denies MENU_VIEW
            TestDbFactory.SeedMatrixRow(db, staff.StaffId, orgNode.Id, "MENU_VIEW", hasAccess: false);

            // Group grants MENU_VIEW
            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "MENU_VIEW");

            var service = new AccessService(db);

            // Act
            var result = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            // Assert
            var feature = result.Features.Single(f => f.FeatureKey == "MENU_VIEW");

            Assert.True(feature.HasAccess,         "MENU_VIEW should be accessible because group grants it.");
            Assert.False(feature.IndividualAccess, "IndividualAccess should be false (explicitly denied).");
            Assert.True(feature.GroupAccess,       "GroupAccess should be true.");
            Assert.Equal("Group", feature.Source);
        }

        [Fact]
        public async Task GetEffectiveAccess_ReturnsAllFeatures_NotJustGranted()
        {
            // Arrange — result must include ALL features, not just the ones with access
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW", "ORG_CREATE", "ORG_EDIT", "ORG_DELETE");

            var (staff, _, _) = TestDbFactory.SeedStaff(db);
            var group         = TestDbFactory.SeedGroup(db);

            // Only grant 1 of 4
            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW");

            var service = new AccessService(db);

            // Act
            var result = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            // Assert
            Assert.Equal(4, result.Features.Count);
            Assert.Equal(1, result.TotalGranted);
            Assert.Equal(3, result.TotalDenied);
        }

        [Fact]
        public async Task GetEffectiveAccess_ReturnsCorrectStaffAndGroupNames()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW");

            var (staff, _, _) = TestDbFactory.SeedStaff(db, fullName: "Ali Khan");
            var group         = TestDbFactory.SeedGroup(db, groupName: "Software Team");

            var service = new AccessService(db);

            // Act
            var result = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            // Assert
            Assert.Equal("Ali Khan",      result.StaffName);
            Assert.Equal("Software Team", result.GroupName);
            Assert.Equal(staff.StaffId,   result.StaffId);
            Assert.Equal(group.GroupId,   result.GroupId);
        }

        [Fact]
        public async Task GetEffectiveAccess_WhenStaffNotFound_ThrowsKeyNotFoundException()
        {
            // Arrange
            using var db  = TestDbFactory.Create();
            var group     = TestDbFactory.SeedGroup(db);
            var service   = new AccessService(db);
            var fakeStaff = Guid.NewGuid(); // does not exist

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.GetEffectiveAccessAsync(fakeStaff, group.GroupId));
        }

        [Fact]
        public async Task GetEffectiveAccess_WhenGroupNotFound_ThrowsKeyNotFoundException()
        {
            // Arrange
            using var db  = TestDbFactory.Create();
            var (staff, _, _) = TestDbFactory.SeedStaff(db);
            var service   = new AccessService(db);
            const int fakeGroupId = 99999; // does not exist

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.GetEffectiveAccessAsync(staff.StaffId, fakeGroupId));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SyncGroupToDeptMatrixAsync — Sync Logic Tests
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task SyncGroup_WhenGroupHasFeatures_CreatesMatrixRowsForAllMembers()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW", "EMPLOYEE_VIEW");

            var (staff1, _, orgNode) = TestDbFactory.SeedStaff(db, "Staff One");
            var (staff2, _, _)       = TestDbFactory.SeedStaff(db, "Staff Two", orgNodeId: orgNode.Id);
            var group                = TestDbFactory.SeedGroup(db, "Dev Group");

            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW", "EMPLOYEE_VIEW");
            TestDbFactory.SeedStaffGroup(db, staff1.StaffId, group.GroupId);
            TestDbFactory.SeedStaffGroup(db, staff2.StaffId, group.GroupId);

            var service = new AccessService(db);

            // Act
            var (success, message, staffSynced, permissionsSynced) =
                await service.SyncGroupToDeptMatrixAsync(group.GroupId, "TestSync");

            // Assert
            Assert.True(success, $"Sync failed: {message}");
            Assert.Equal(2, staffSynced);
            Assert.Equal(4, permissionsSynced); // 2 staff × 2 features

            // Verify rows were actually created in DB
            var staff1Rows = db.DepartmentAccessMatrix
                .Where(m => m.StaffId == staff1.StaffId && m.HasAccess).ToList();
            var staff2Rows = db.DepartmentAccessMatrix
                .Where(m => m.StaffId == staff2.StaffId && m.HasAccess).ToList();

            Assert.Equal(2, staff1Rows.Count);
            Assert.Equal(2, staff2Rows.Count);
            Assert.Contains(staff1Rows, r => r.FeatureKey == "ORG_VIEW");
            Assert.Contains(staff1Rows, r => r.FeatureKey == "EMPLOYEE_VIEW");
        }

        [Fact]
        public async Task SyncGroup_WhenGroupFeatureRemoved_RevokesGroupSyncedRows()
        {
            // Arrange — staff has a row that was previously synced by the group
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW", "EMPLOYEE_VIEW");

            var (staff, vacancy, orgNode) = TestDbFactory.SeedStaff(db);
            var group                     = TestDbFactory.SeedGroup(db, "Dev Group");

            // Group currently only has ORG_VIEW (EMPLOYEE_VIEW was removed)
            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW");
            TestDbFactory.SeedStaffGroup(db, staff.StaffId, group.GroupId);

            // Simulate a previously synced EMPLOYEE_VIEW row
            TestDbFactory.SeedMatrixRow(
                db, staff.StaffId, orgNode.Id, "EMPLOYEE_VIEW",
                hasAccess: true, grantedBy: "GroupSync:Dev Group");

            var service = new AccessService(db);

            // Act
            await service.SyncGroupToDeptMatrixAsync(group.GroupId, "TestSync");

            // Assert — EMPLOYEE_VIEW should now be revoked
            var employeeRow = db.DepartmentAccessMatrix
                .FirstOrDefault(m => m.StaffId == staff.StaffId && m.FeatureKey == "EMPLOYEE_VIEW");

            Assert.NotNull(employeeRow);
            Assert.False(employeeRow!.HasAccess,
                "EMPLOYEE_VIEW should be revoked because group no longer has it.");

            // ORG_VIEW should still be granted
            var orgRow = db.DepartmentAccessMatrix
                .FirstOrDefault(m => m.StaffId == staff.StaffId && m.FeatureKey == "ORG_VIEW");

            Assert.NotNull(orgRow);
            Assert.True(orgRow!.HasAccess, "ORG_VIEW should still be granted.");
        }

        [Fact]
        public async Task SyncGroup_PreservesIndividualAdminOverrides()
        {
            // Arrange — admin manually granted REPORT_EXPORT to a staff member
            // Group does NOT have REPORT_EXPORT
            // Sync should NOT revoke this individual override
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW", "REPORT_EXPORT");

            var (staff, vacancy, orgNode) = TestDbFactory.SeedStaff(db);
            var group                     = TestDbFactory.SeedGroup(db, "Basic Group");

            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW");
            TestDbFactory.SeedStaffGroup(db, staff.StaffId, group.GroupId);

            // Admin manually granted REPORT_EXPORT — NOT a group sync row
            TestDbFactory.SeedMatrixRow(
                db, staff.StaffId, orgNode.Id, "REPORT_EXPORT",
                hasAccess: true, grantedBy: "admin");  // ← individual override

            var service = new AccessService(db);

            // Act
            await service.SyncGroupToDeptMatrixAsync(group.GroupId, "TestSync");

            // Assert — REPORT_EXPORT must NOT be revoked (it was set by admin, not group sync)
            var reportRow = db.DepartmentAccessMatrix
                .FirstOrDefault(m => m.StaffId == staff.StaffId && m.FeatureKey == "REPORT_EXPORT");

            Assert.NotNull(reportRow);
            Assert.True(reportRow!.HasAccess,
                "REPORT_EXPORT should NOT be revoked — it was an individual admin override.");
        }

        [Fact]
        public async Task SyncGroup_WhenGroupHasNoMembers_ReturnsSuccessWithZeroCounts()
        {
            // Arrange — group exists but has no staff assigned
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW");

            var group = TestDbFactory.SeedGroup(db, "Empty Group");
            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW");
            // No StaffAccessGroups seeded

            var service = new AccessService(db);

            // Act
            var (success, message, staffSynced, permissionsSynced) =
                await service.SyncGroupToDeptMatrixAsync(group.GroupId);

            // Assert
            Assert.True(success);
            Assert.Equal(0, staffSynced);
            Assert.Equal(0, permissionsSynced);
            Assert.Contains("no members", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SyncGroup_WhenGroupNotFound_ReturnsFalse()
        {
            // Arrange
            using var db    = TestDbFactory.Create();
            var service     = new AccessService(db);
            const int fakeId = 99999;

            // Act
            var (success, message, staffSynced, permissionsSynced) =
                await service.SyncGroupToDeptMatrixAsync(fakeId);

            // Assert
            Assert.False(success);
            Assert.Equal(0, staffSynced);
            Assert.Equal(0, permissionsSynced);
            Assert.Contains("not found", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SyncGroup_WhenRowAlreadyGranted_DoesNotDuplicateOrCount()
        {
            // Arrange — staff already has ORG_VIEW = true in matrix
            // Sync should NOT create a duplicate row or increment count
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW");

            var (staff, vacancy, orgNode) = TestDbFactory.SeedStaff(db);
            var group                     = TestDbFactory.SeedGroup(db, "Dev Group");

            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW");
            TestDbFactory.SeedStaffGroup(db, staff.StaffId, group.GroupId);

            // Row already exists and is already granted
            TestDbFactory.SeedMatrixRow(
                db, staff.StaffId, orgNode.Id, "ORG_VIEW",
                hasAccess: true, grantedBy: "GroupSync:Dev Group");

            var service = new AccessService(db);

            // Act
            var (success, _, staffSynced, permissionsSynced) =
                await service.SyncGroupToDeptMatrixAsync(group.GroupId);

            // Assert
            Assert.True(success);
            Assert.Equal(1, staffSynced);
            Assert.Equal(0, permissionsSynced); // already granted — nothing changed

            // Verify only one row exists (no duplicate)
            var rowCount = db.DepartmentAccessMatrix
                .Count(m => m.StaffId == staff.StaffId && m.FeatureKey == "ORG_VIEW");
            Assert.Equal(1, rowCount);
        }

        [Fact]
        public async Task SyncGroup_SetsGrantedByToGroupSyncPrefix()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "VACANCY_VIEW");

            var (staff, _, orgNode) = TestDbFactory.SeedStaff(db);
            var group               = TestDbFactory.SeedGroup(db, "HR Group");

            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "VACANCY_VIEW");
            TestDbFactory.SeedStaffGroup(db, staff.StaffId, group.GroupId);

            var service = new AccessService(db);

            // Act
            await service.SyncGroupToDeptMatrixAsync(group.GroupId, syncedBy: null);

            // Assert — GrantedBy should start with "GroupSync:" so individual overrides are preserved
            var row = db.DepartmentAccessMatrix
                .FirstOrDefault(m => m.StaffId == staff.StaffId && m.FeatureKey == "VACANCY_VIEW");

            Assert.NotNull(row);
            Assert.StartsWith("GroupSync:", row!.GrantedBy, StringComparison.Ordinal);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Integration: SetGroupFeatures → SyncGroup → GetEffectiveAccess
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task FullFlow_UpdateGroupFeatures_SyncToMatrix_EffectiveAccessReflectsChanges()
        {
            // Arrange
            using var db = TestDbFactory.Create();
            TestDbFactory.SeedFeatures(db, "ORG_VIEW", "ORG_CREATE", "ORG_DELETE");

            var (staff, _, orgNode) = TestDbFactory.SeedStaff(db, "Full Flow Staff");
            var group               = TestDbFactory.SeedGroup(db, "Full Flow Group");

            // Initial group features: ORG_VIEW + ORG_CREATE
            TestDbFactory.SeedGroupFeatures(db, group.GroupId, "ORG_VIEW", "ORG_CREATE");
            TestDbFactory.SeedStaffGroup(db, staff.StaffId, group.GroupId);

            var service = new AccessService(db);

            // Step 1: Initial sync
            await service.SyncGroupToDeptMatrixAsync(group.GroupId, "InitialSync");

            // Verify initial state
            var initial = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);
            Assert.True(initial.Features.Single(f => f.FeatureKey == "ORG_VIEW").HasAccess);
            Assert.True(initial.Features.Single(f => f.FeatureKey == "ORG_CREATE").HasAccess);
            Assert.False(initial.Features.Single(f => f.FeatureKey == "ORG_DELETE").HasAccess);

            // Step 2: Update group — remove ORG_CREATE, add ORG_DELETE
            await service.SetGroupFeaturesAsync(group.GroupId, ["ORG_VIEW", "ORG_DELETE"]);

            // Step 3: Sync again
            await service.SyncGroupToDeptMatrixAsync(group.GroupId, "UpdateSync");

            // Step 4: Check effective access reflects the update
            var updated = await service.GetEffectiveAccessAsync(staff.StaffId, group.GroupId);

            Assert.True(updated.Features.Single(f => f.FeatureKey == "ORG_VIEW").HasAccess,
                "ORG_VIEW should still be accessible.");
            Assert.False(updated.Features.Single(f => f.FeatureKey == "ORG_CREATE").HasAccess,
                "ORG_CREATE should be revoked after group update.");
            Assert.True(updated.Features.Single(f => f.FeatureKey == "ORG_DELETE").HasAccess,
                "ORG_DELETE should now be accessible after group update.");
        }
    }
}
