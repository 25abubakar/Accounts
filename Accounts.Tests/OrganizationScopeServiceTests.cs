using Accounts.Models;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;

namespace Accounts.Tests;

public class OrganizationScopeServiceTests
{
    [Fact]
    public async Task TenantScope_AllowsOnlyItsOwnOrganizationSubtree()
    {
        await using var db = TestDbFactory.Create();
        db.OrganizationTree.AddRange(
            new OrganizationTree { Id = 1, Name = "Country", Label = "Country" },
            new OrganizationTree { Id = 10, Name = "Tenant A", Label = "Company", ParentId = 1 },
            new OrganizationTree { Id = 11, Name = "A Department", Label = "Department", ParentId = 10 },
            new OrganizationTree { Id = 20, Name = "Tenant B", Label = "Company", ParentId = 1 },
            new OrganizationTree { Id = 21, Name = "B Department", Label = "Department", ParentId = 20 });
        db.Tenants.Add(new Tenant
        {
            Id = 1,
            TenantName = "Tenant A",
            TenantCode = "TA",
            OrganizationTreeId = 10,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new OrganizationScopeService(db);

        Assert.True(await service.IsWithinTenantSubtreeAsync(1, 10));
        Assert.True(await service.IsWithinTenantSubtreeAsync(1, 11));
        Assert.False(await service.IsWithinTenantSubtreeAsync(1, 20));
        Assert.False(await service.IsWithinTenantSubtreeAsync(1, 21));
    }
}
