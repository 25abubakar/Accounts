using Accounts.Authorization;
using Accounts.Data;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public static class SchedulerModule
{
    // API registration only: starting Accounts never starts a second execution engine.
    public static IServiceCollection AddAccountsScheduler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSchedulerStorage(configuration);
        services.AddScoped<SchedulerAccessFilter>();
        services.AddAuthorization(options => options.AddPolicy("SchedulerAdmin", policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasClaim("is_tenant_admin", "true") || context.User.IsInRole("TenantAdmin"))));
        return services;
    }

    public static IServiceCollection AddSchedulerStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var business = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Accounts connection is required.");
        var scheduler = configuration.GetConnectionString("SchedulerConnection") ?? new SqlConnectionStringBuilder(business) { InitialCatalog = "AccountsScheduler" }.ConnectionString;
        if (new SqlConnectionStringBuilder(scheduler).InitialCatalog.Equals(new SqlConnectionStringBuilder(business).InitialCatalog, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Scheduler storage must use its own database.");
        services.AddSingleton(new SchedulerStorage(scheduler, business));
        services.AddHttpContextAccessor();
        services.AddScoped<SchedulerTenant>();
        services.AddDbContext<SchedulerDbContext>(options => options.UseSqlServer(scheduler));
        services.AddScoped<BusinessReadConnection>();
        services.AddScoped<IModuleJob, SchedulerHealthJob>();
        services.AddScoped<IModuleJob, AttendanceStatusCheck>();
        services.AddScoped<IModuleJob, AssessmentCycleCheck>();
        services.AddSingleton<ScheduleSynchronizer>();
        services.AddScoped<ModuleJobDispatcher>();
        services.AddSingleton<SchedulerQueue>();
        return services;
    }
}
