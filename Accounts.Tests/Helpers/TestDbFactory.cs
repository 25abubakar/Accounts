using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Accounts.Tests.Helpers;

public static class TestDbFactory
{
    public static ApplicationDbContext Create(ITenantService? tenantService = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new ApplicationDbContext(options, tenantService);
        db.Database.EnsureCreated();
        return db;
    }
}
