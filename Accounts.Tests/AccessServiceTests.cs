using Accounts.Models;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;

namespace Accounts.Tests;

public class AccessServiceTests
{
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
}
