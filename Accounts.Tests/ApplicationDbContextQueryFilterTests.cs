using Accounts.Models;
using Accounts.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Tests;

public class ApplicationDbContextQueryFilterTests
{
    [Fact]
    public async Task TenantContext_SeesOnlyItsOwnOperationalRows()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(databaseName);

        await using var tenantA = TestDbFactory.Create(
            new TestTenantService(1),
            databaseName);

        var people = await tenantA.Persons.AsNoTracking().ToListAsync();
        var deductions = await tenantA.AttendanceDeductionRequests.AsNoTracking().ToListAsync();

        Assert.Single(people);
        Assert.Equal(1, people[0].TenantId);
        Assert.Single(deductions);
        Assert.Equal(1, deductions[0].TenantId);
    }

    [Fact]
    public async Task MissingTenantContext_SeesNoOperationalRows()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(databaseName);

        await using var noTenant = TestDbFactory.Create(
            new TestTenantService(null),
            databaseName);

        Assert.Empty(await noTenant.Persons.AsNoTracking().ToListAsync());
        Assert.Empty(await noTenant.AttendanceDeductionRequests.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SuperAdmin_SeesNoTenantOperationalRows()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(databaseName);

        await using var superAdmin = TestDbFactory.Create(
            new TestTenantService(null, isSuperAdmin: true),
            databaseName);

        Assert.Empty(await superAdmin.Persons.AsNoTracking().ToListAsync());
        Assert.Empty(await superAdmin.AttendanceDeductionRequests.AsNoTracking().ToListAsync());
    }

    private static async Task SeedTwoTenantsAsync(string databaseName)
    {
        await using var seed = TestDbFactory.Create(databaseName: databaseName);
        seed.Persons.AddRange(
            CreatePerson(1, "Tenant A"),
            CreatePerson(2, "Tenant B"));
        seed.AttendanceDeductionRequests.AddRange(
            new AttendanceDeductionRequest
            {
                TenantId = 1,
                Name = "Tenant A deduction",
                UserId = "tenant-a"
            },
            new AttendanceDeductionRequest
            {
                TenantId = 2,
                Name = "Tenant B deduction",
                UserId = "tenant-b"
            });
        await seed.SaveChangesAsync();
    }

    private static Person CreatePerson(int tenantId, string name) =>
        new()
        {
            TenantId = tenantId,
            FullName = name,
            FirstName = name,
            IdentityUserId = Guid.NewGuid().ToString()
        };
}
