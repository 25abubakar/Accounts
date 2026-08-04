using Accounts.Data;
using Accounts.Services;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Accounts.Tests.Helpers;

public sealed class AccountsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;

    public AccountsWebApplicationFactory() : this("Testing")
    {
    }

    internal AccountsWebApplicationFactory(string environment) =>
        _environment = environment;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase($"Accounts_Integration_{Guid.NewGuid()}"));

            var backgroundService = services.FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ProcessReportAutoTransferService));
            if (backgroundService != null)
                services.Remove(backgroundService);
        });
    }
}
