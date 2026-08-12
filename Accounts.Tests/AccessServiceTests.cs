using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;
using System.Security.Claims;

namespace Accounts.Tests;

public class AccessServiceTests
{
    [Fact]
    public async Task RbacEffectivePermissions_NeverExceedTenantCrudCeiling()
    {
        await using var db = TestDbFactory.Create();
        var staffId = Guid.NewGuid();
        const int tenantId = 2007;

        var menu = new Menu { Title = "Staff", Route = "/hr/staff", IsActive = true };
        var view = new Feature { FeatureKey = "MENU_1_VIEW", FeatureName = "View Staff", Module = "Staff" };
        var edit = new Feature { FeatureKey = "MENU_1_EDIT", FeatureName = "Edit Staff", Module = "Staff" };
        db.Menus.Add(menu);
        db.Features.AddRange(view, edit);
        await db.SaveChangesAsync();

        // Use the generated menu id in case the provider does not start at one.
        view.FeatureKey = $"MENU_{menu.Id}_VIEW";
        edit.FeatureKey = $"MENU_{menu.Id}_EDIT";
        db.StaffVacancies.Add(new StaffVacancy { StaffId = staffId, TenantId = tenantId });
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = tenantId,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanAdd = false,
            CanEdit = false,
            CanDelete = false
        });
        var grant = new StaffMenuAccess { StaffId = staffId, MenuId = menu.Id, IsAllow = true };
        grant.AccessFeatures.Add(new AccessFeature { PermissionId = view.PermissionId, IsAllow = true });
        grant.AccessFeatures.Add(new AccessFeature { PermissionId = edit.PermissionId, IsAllow = true });
        db.StaffMenuAccesses.Add(grant);
        await db.SaveChangesAsync();

        var effective = await new RbacService(db).GetEffectivePermissionsAsync(staffId);

        Assert.Contains($"MENU_{menu.Id}", effective);
        Assert.Contains(view.FeatureKey, effective);
        Assert.DoesNotContain(edit.FeatureKey, effective);
    }

    [Fact]
    public async Task RbacEffectivePermissions_ImmediatelyReflectSuperAdminRevoke()
    {
        await using var db = TestDbFactory.Create();
        var staffId = Guid.NewGuid();
        const int tenantId = 2007;

        var menu = new Menu { Title = "Types", Route = "/settings/types", IsActive = true };
        db.Menus.Add(menu);
        await db.SaveChangesAsync();
        var edit = new Feature
        {
            FeatureKey = $"MENU_{menu.Id}_EDIT",
            FeatureName = "Edit Types",
            Module = "Platform Settings"
        };
        db.Features.Add(edit);
        await db.SaveChangesAsync();
        db.StaffVacancies.Add(new StaffVacancy { StaffId = staffId, TenantId = tenantId });
        var ceiling = new TenantMenuPermission
        {
            TenantId = tenantId,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanEdit = true
        };
        db.TenantMenuPermissions.Add(ceiling);
        var grant = new StaffMenuAccess { StaffId = staffId, MenuId = menu.Id, IsAllow = true };
        grant.AccessFeatures.Add(new AccessFeature { PermissionId = edit.PermissionId, IsAllow = true });
        db.StaffMenuAccesses.Add(grant);
        await db.SaveChangesAsync();

        var service = new RbacService(db);
        Assert.True(await service.HasAccessAsync(staffId, edit.FeatureKey));

        // Simulate the Super Admin lowering the tenant ceiling while an old
        // delegated staff row still exists. Runtime resolution must fail closed.
        ceiling.CanEdit = false;
        await db.SaveChangesAsync();

        Assert.False(await service.HasAccessAsync(staffId, edit.FeatureKey));
    }

    [Fact]
    public async Task RbacEffectivePermissions_GrantsMenuViewWithoutUnlockingAllCrud()
    {
        await using var db = TestDbFactory.Create();
        var staffId = Guid.NewGuid();
        const int tenantId = 2011;

        var menu = new Menu { Title = "Attendance", Route = "/attendance", IsActive = true };
        db.Menus.Add(menu);
        await db.SaveChangesAsync();
        var add = new Feature
        {
            FeatureKey = $"MENU_{menu.Id}_ADD",
            FeatureName = "Add Attendance",
            Module = "Attendance"
        };
        db.Features.Add(add);
        db.StaffVacancies.Add(new StaffVacancy { StaffId = staffId, TenantId = tenantId });
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = tenantId,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanAdd = true,
            CanEdit = true,
            CanDelete = true
        });
        // Menu grant only — no AccessFeatures. Must not unlock ADD/EDIT/DELETE.
        db.StaffMenuAccesses.Add(new StaffMenuAccess { StaffId = staffId, MenuId = menu.Id, IsAllow = true });
        await db.SaveChangesAsync();

        var effective = (await new RbacService(db).GetEffectivePermissionsAsync(staffId)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains($"MENU_{menu.Id}", effective);
        Assert.Contains($"MENU_{menu.Id}_VIEW", effective);
        Assert.DoesNotContain(add.FeatureKey, effective);
    }

    [Fact]
    public async Task RbacEffectivePermissions_SemanticKeyHonorsMenuCeiling()
    {
        await using var db = TestDbFactory.Create();
        var staffId = Guid.NewGuid();
        const int tenantId = 2012;

        var menu = new Menu { Title = "Persons", Route = "/hr/persons", IsActive = true };
        var personEdit = new Feature { FeatureKey = "PERSON_EDIT", FeatureName = "Edit Person", Module = "People" };
        db.Menus.Add(menu);
        db.Features.Add(personEdit);
        await db.SaveChangesAsync();
        db.MenuPermissions.Add(new MenuPermission { MenuId = menu.Id, PermissionId = personEdit.PermissionId });
        db.StaffVacancies.Add(new StaffVacancy { StaffId = staffId, TenantId = tenantId });
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = tenantId,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanEdit = false
        });
        var grant = new StaffMenuAccess { StaffId = staffId, MenuId = menu.Id, IsAllow = true };
        grant.AccessFeatures.Add(new AccessFeature { PermissionId = personEdit.PermissionId, IsAllow = true });
        db.StaffMenuAccesses.Add(grant);
        await db.SaveChangesAsync();

        Assert.False(await new RbacService(db).HasAccessAsync(staffId, "PERSON_EDIT"));
    }

    [Fact]
    public async Task GetAllFeaturesAsync_OrdersByModuleAndKey()
    {
        await using var db = TestDbFactory.Create();
        db.Features.AddRange(
            new Feature { FeatureKey = "Z_VIEW", FeatureName = "Z", Module = "B" },
            new Feature { FeatureKey = "A_EDIT", FeatureName = "A edit", Module = "A" },
            new Feature { FeatureKey = "A_VIEW", FeatureName = "A view", Module = "A" });
        await db.SaveChangesAsync();

        var results = (await new AccessService(db).GetAllFeaturesAsync()).ToList();

        Assert.Equal(3, results.Count);
        Assert.Contains("A_EDIT", results[0].ToString());
        Assert.Contains("A_VIEW", results[1].ToString());
        Assert.Contains("Z_VIEW", results[2].ToString());
    }

    [Fact]
    public async Task GetFeaturesByModuleAsync_IsCaseInsensitive()
    {
        await using var db = TestDbFactory.Create();
        db.Features.AddRange(
            new Feature { FeatureKey = "PERSON_VIEW", FeatureName = "View", Module = "People" },
            new Feature { FeatureKey = "ORG_VIEW", FeatureName = "View", Module = "Organization" });
        await db.SaveChangesAsync();

        var results = (await new AccessService(db).GetFeaturesByModuleAsync("people")).ToList();

        Assert.Single(results);
        Assert.Contains("PERSON_VIEW", results[0].ToString());
    }

    [Fact]
    public async Task DeprecatedPermissionWrites_ReturnExplicitFailure()
    {
        await using var db = TestDbFactory.Create();
        var service = new AccessService(db);

        var toggle = await service.TogglePermissionAsync(Guid.NewGuid(), "X", true, null);
        var grant = await service.GrantAllAsync(Guid.NewGuid(), 1, null);
        var revoke = await service.RevokeAllAsync(Guid.NewGuid(), null);

        Assert.False(toggle.Success);
        Assert.Equal(0, grant.Count);
        Assert.Equal(0, revoke.Count);
    }

    [Fact]
    public async Task GetStaffPermissionsAsync_DeprecatedPathReturnsEmpty()
    {
        await using var db = TestDbFactory.Create();

        var results = await new AccessService(db).GetStaffPermissionsAsync(Guid.NewGuid());

        Assert.Empty(results);
    }

    [Fact]
    public async Task TenantPermissionService_HasMenuRouteAsync_HonorsCrudCeiling()
    {
        await using var db = TestDbFactory.Create();
        const int tenantId = 2007;
        var menu = new Menu { Title = "Staff", Route = "/hr/staff", IsActive = true };
        db.Menus.Add(menu);
        await db.SaveChangesAsync();
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = tenantId,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanAdd = false,
            CanEdit = false,
            CanDelete = false
        });
        await db.SaveChangesAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ITenantService.ClaimTenantId, tenantId.ToString()),
            new Claim(ITenantService.ClaimIsTenantAdmin, "true"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
        ], "test"));

        var service = new TenantPermissionService(db);
        Assert.True(await service.HasMenuRouteAsync(user, ["/hr/staff"], "VIEW"));
        Assert.False(await service.HasMenuRouteAsync(user, ["/hr/staff"], "ADD"));
    }

    [Fact]
    public async Task TenantPermissionService_HasFeatureAsync_ResolvesSemanticKeyThroughMenuCeiling()
    {
        await using var db = TestDbFactory.Create();
        const int tenantId = 2007;
        var menu = new Menu { Title = "Persons", Route = "/hr/persons", IsActive = true };
        var feature = new Feature { FeatureKey = "PERSON_EDIT", FeatureName = "Edit Person", Module = "People" };
        db.Menus.Add(menu);
        db.Features.Add(feature);
        await db.SaveChangesAsync();
        db.MenuPermissions.Add(new MenuPermission { MenuId = menu.Id, PermissionId = feature.PermissionId });
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = tenantId,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanEdit = true
        });
        await db.SaveChangesAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ITenantService.ClaimTenantId, tenantId.ToString()),
            new Claim(ITenantService.ClaimIsTenantAdmin, "true"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
        ], "test"));

        var service = new TenantPermissionService(db);
        Assert.True(await service.HasFeatureAsync(user, "PERSON_EDIT"));
    }
}
