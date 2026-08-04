using Accounts.Models;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;

namespace Accounts.Tests;

public class AccessServiceTests
{
    [Fact]
    public async Task GetAllFeaturesAsync_OrdersByModuleAndKey()
    {
        var tenantService = new TestTenantService(1, isTenantAdmin: true);
        await using var db = TestDbFactory.Create(tenantService);
        db.Features.AddRange(
            new Feature { FeatureKey = "Z_VIEW", FeatureName = "Z", Module = "B" },
            new Feature { FeatureKey = "A_EDIT", FeatureName = "A edit", Module = "A" },
            new Feature { FeatureKey = "A_VIEW", FeatureName = "A view", Module = "A" });
        await db.SaveChangesAsync();
        await GrantAllSeededFeaturesAsync(db);

        var results = (await CreateService(db, tenantService).GetAllFeaturesAsync()).ToList();

        Assert.Equal(3, results.Count);
        Assert.Contains("A_EDIT", results[0].ToString());
        Assert.Contains("A_VIEW", results[1].ToString());
        Assert.Contains("Z_VIEW", results[2].ToString());
    }

    [Fact]
    public async Task GetFeaturesByModuleAsync_IsCaseInsensitive()
    {
        var tenantService = new TestTenantService(1, isTenantAdmin: true);
        await using var db = TestDbFactory.Create(tenantService);
        db.Features.AddRange(
            new Feature { FeatureKey = "PERSON_VIEW", FeatureName = "View", Module = "People" },
            new Feature { FeatureKey = "ORG_VIEW", FeatureName = "View", Module = "Organization" });
        await db.SaveChangesAsync();
        await GrantAllSeededFeaturesAsync(db);

        var results = (await CreateService(db, tenantService).GetFeaturesByModuleAsync("people")).ToList();

        Assert.Single(results);
        Assert.Contains("PERSON_VIEW", results[0].ToString());
    }

    [Fact]
    public async Task DeprecatedPermissionWrites_ReturnExplicitFailure()
    {
        await using var db = TestDbFactory.Create();
        var tenantService = new TestTenantService(1, isTenantAdmin: true);
        var service = CreateService(db, tenantService);

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
        var tenantService = new TestTenantService(1, isTenantAdmin: true);

        var results = await CreateService(db, tenantService).GetStaffPermissionsAsync(Guid.NewGuid());

        Assert.Empty(results);
    }

    private static AccessService CreateService(
        Accounts.Data.ApplicationDbContext db,
        TestTenantService tenantService) =>
        new(db, tenantService, new TenantMenuCeilingService(db));

    private static async Task GrantAllSeededFeaturesAsync(Accounts.Data.ApplicationDbContext db)
    {
        var menu = new Menu { Title = "Access", Route = "/access/admin", IsActive = true };
        db.Menus.Add(menu);
        await db.SaveChangesAsync();
        db.MenuPermissions.AddRange(db.Features.Select(feature =>
            new MenuPermission { MenuId = menu.Id, PermissionId = feature.PermissionId }));
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = 1,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanAdd = true,
            CanEdit = true,
            CanDelete = true
        });
        await db.SaveChangesAsync();
    }
}
