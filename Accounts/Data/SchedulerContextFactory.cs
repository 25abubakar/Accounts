using Accounts.Services.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accounts.Data;

public sealed class SchedulerContextFactory : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    public SchedulerDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SchedulerDbContext>().UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__SchedulerConnection") ??
            "Server=(localdb)\\MSSQLLocalDB;Database=AccountsScheduler;Trusted_Connection=True;TrustServerCertificate=True").Options,
        new SchedulerTenant(new HttpContextAccessor()));
}
