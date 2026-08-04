using Accounts.Models;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;

namespace Accounts.Tests;

public class TenantMenuCeilingServiceTests
{
    [Fact]
    public async Task ViewOnlyTenantGrant_DeniesAddEditAndDelete()
    {
        await using var db = TestDbFactory.Create();
        var menu = new Menu { Title = "People", Route = "/hr/people" };
        var view = new Feature { FeatureKey = "PERSON_VIEW", FeatureName = "View", Module = "People" };
        var add = new Feature { FeatureKey = "PERSON_REGISTER", FeatureName = "Add", Module = "People" };
        var edit = new Feature { FeatureKey = "PERSON_EDIT", FeatureName = "Edit", Module = "People" };
        var delete = new Feature { FeatureKey = "PERSON_DELETE", FeatureName = "Delete", Module = "People" };
        db.AddRange(menu, view, add, edit, delete);
        await db.SaveChangesAsync();

        db.MenuPermissions.AddRange(
            new MenuPermission { MenuId = menu.Id, PermissionId = view.PermissionId },
            new MenuPermission { MenuId = menu.Id, PermissionId = add.PermissionId },
            new MenuPermission { MenuId = menu.Id, PermissionId = edit.PermissionId },
            new MenuPermission { MenuId = menu.Id, PermissionId = delete.PermissionId });
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = 1,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanAdd = false,
            CanEdit = false,
            CanDelete = false
        });
        await db.SaveChangesAsync();

        var service = new TenantMenuCeilingService(db);

        Assert.True(await service.AllowsFeatureAsync(1, "PERSON_VIEW"));
        Assert.False(await service.AllowsFeatureAsync(1, "PERSON_REGISTER"));
        Assert.False(await service.AllowsFeatureAsync(1, "PERSON_EDIT"));
        Assert.False(await service.AllowsFeatureAsync(1, "PERSON_DELETE"));
    }

    [Fact]
    public async Task DelegationValidation_RejectsPermissionOutsideTenantCeiling()
    {
        await using var db = TestDbFactory.Create();
        var menu = new Menu { Title = "People", Route = "/hr/people" };
        var view = new Feature { FeatureKey = "MENU_1_VIEW", FeatureName = "View", Module = "People" };
        var add = new Feature { FeatureKey = "MENU_1_ADD", FeatureName = "Add", Module = "People" };
        db.AddRange(menu, view, add);
        await db.SaveChangesAsync();
        db.MenuPermissions.AddRange(
            new MenuPermission { MenuId = menu.Id, PermissionId = view.PermissionId },
            new MenuPermission { MenuId = menu.Id, PermissionId = add.PermissionId });
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = 1,
            MenuId = menu.Id,
            CanView = true,
            CanAdd = false,
            CanEdit = false,
            CanDelete = false
        });
        await db.SaveChangesAsync();

        var result = await new TenantMenuCeilingService(db)
            .ValidatePermissionIdsAsync(1, new[] { view.PermissionId, add.PermissionId });

        Assert.False(result.IsValid);
        Assert.Equal(new[] { add.PermissionId }, result.InvalidPermissionIds);
    }
}
